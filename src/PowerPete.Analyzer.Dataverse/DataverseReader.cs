namespace PowerPete.Analyzer.Dataverse;

using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using PowerPete.Analyzer.Domain;

/// <summary>
/// Reads a live Dataverse environment.
/// </summary>
/// <remarks>
/// What the offline reader cannot see: components outside the analysed solutions, the default
/// solution, row counts, role assignments, plugin step registrations, and every piece of
/// runtime evidence. Twenty-one rules in the catalogue have no answer without this.
///
/// Nothing here writes. There is no create, no update and no delete in this file, and there is
/// no code path anywhere in the product that puts one here. That is what makes a discovery
/// runnable in a first conversation.
///
/// Every read is recorded separately, successful or not. Twenty-three reads each failing on
/// their own, summed, look exactly like an estate with nothing in it, and that report would be
/// believed.
/// </remarks>
public sealed class DataverseReader(HttpClient client)
{
    private const string Api = "/api/data/v9.2/";

    /// <summary>What one read attempt did.</summary>
    /// <param name="ComponentTypeId">What was being read.</param>
    /// <param name="Succeeded">Whether it worked.</param>
    /// <param name="RecordCount">How many, on success.</param>
    /// <param name="FailureReason">Why not, in the sentence a consultant reads aloud when a client asks why a section is empty.</param>
    public sealed record EntityRead(string ComponentTypeId, bool Succeeded, int? RecordCount, string? FailureReason);

    /// <summary>What a read produced.</summary>
    /// <param name="Components">Everything found.</param>
    /// <param name="Links">Edges the reader could resolve.</param>
    /// <param name="Reads">One record per component type attempted.</param>
    /// <param name="Identity">Who the connection authenticated as.</param>
    public sealed record Result(
        IReadOnlyList<DiscoveredComponent> Components,
        IReadOnlyList<ComponentLink> Links,
        IReadOnlyList<EntityRead> Reads,
        string? Identity);

    /// <summary>
    /// Confirms the connection works and says who it is.
    /// </summary>
    /// <remarks>
    /// Called before anything else and its answer is stored on the connection. A service
    /// principal that authenticated but holds no role reads an environment with nothing in it,
    /// and the identity on the report is how somebody spots that before the client does.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<(bool Succeeded, string? Identity, string Message)> TestAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await client
                .GetAsync(new Uri($"{Api}WhoAmI", UriKind.Relative), cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return (false, null,
                    $"The environment answered {(int)response.StatusCode}. A 401 means the credential is wrong; " +
                    "a 403 means it authenticated and holds no role, which reads as an empty estate rather than as an error.");
            }

            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

            var userId = document.RootElement.TryGetProperty("UserId", out var value) ? value.GetString() : null;
            var name = await ResolveUserAsync(userId, cancellationToken).ConfigureAwait(false);

            return (true, name ?? userId, "Connected.");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            return (false, null, $"Could not reach the environment: {exception.Message}");
        }
    }

    private async Task<string?> ResolveUserAsync(string? userId, CancellationToken cancellationToken)
    {
        if (userId is null) return null;

        try
        {
            using var document = await GetAsync(
                $"systemusers({userId})?$select=fullname,domainname,applicationid",
                cancellationToken).ConfigureAwait(false);

            if (document is null) return null;

            var name = document.RootElement.TryGetProperty("fullname", out var full) ? full.GetString() : null;
            var isApplication = document.RootElement.TryGetProperty("applicationid", out var app)
                && app.ValueKind != JsonValueKind.Null;

            // Saying which kind matters. A report produced as a system administrator is not
            // evidence that a least privileged integration could have produced it.
            return isApplication ? $"{name} (application user)" : name;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            return userId;
        }
    }

    /// <summary>Reads everything this connection can reach.</summary>
    /// <param name="solutionUniqueNames">Which solutions are in scope. Empty reads every unmanaged solution.</param>
    /// <param name="includeRuntime">Whether to read run history and trace logs, which need more than a reader.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<Result> ReadAsync(
        IReadOnlyList<string> solutionUniqueNames,
        bool includeRuntime,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solutionUniqueNames);

        var components = new List<DiscoveredComponent>();
        var links = new List<ComponentLink>();
        var reads = new List<EntityRead>();

        var identity = (await TestAsync(cancellationToken).ConfigureAwait(false)).Identity;

        await Attempt(reads, "solution", () => ReadSolutionsAsync(components, cancellationToken)).ConfigureAwait(false);
        await Attempt(reads, "table", () => ReadTablesAsync(components, cancellationToken)).ConfigureAwait(false);
        await Attempt(reads, "classicWorkflowBackground", () => ReadProcessesAsync(components, links, cancellationToken)).ConfigureAwait(false);
        await Attempt(reads, "pluginAssembly", () => ReadPluginAssembliesAsync(components, cancellationToken)).ConfigureAwait(false);
        await Attempt(reads, "pluginStep", () => ReadPluginStepsAsync(components, links, cancellationToken)).ConfigureAwait(false);
        await Attempt(reads, "securityRole", () => ReadRolesAsync(components, cancellationToken)).ConfigureAwait(false);
        await Attempt(reads, "connectionReference", () => ReadConnectionReferencesAsync(components, cancellationToken)).ConfigureAwait(false);
        await Attempt(reads, "environmentVariable", () => ReadEnvironmentVariablesAsync(components, cancellationToken)).ConfigureAwait(false);
        await Attempt(reads, "serviceEndpoint", () => ReadServiceEndpointsAsync(components, cancellationToken)).ConfigureAwait(false);
        await Attempt(reads, "report", () => ReadReportsAsync(components, cancellationToken)).ConfigureAwait(false);

        if (includeRuntime)
        {
            await Attempt(reads, "flowRun", () => ReadFlowRunStatisticsAsync(components, cancellationToken)).ConfigureAwait(false);
        }
        else
        {
            reads.Add(new EntityRead("flowRun", false, null,
                "Runtime evidence was not requested, or this connection does not hold the privilege to read it. " +
                "Every rule that needs run history is reported as not assessed."));
        }

        return new Result(components, links, reads, identity);
    }

    /// <summary>
    /// Runs one read and records what happened, whichever it was.
    /// </summary>
    /// <remarks>
    /// The single most important method in this file. A read that throws must not take the run
    /// with it and must not vanish. The sibling product shipped a discovery where twenty-three
    /// entity reads each failed separately and the run finished green, reporting an empty
    /// estate and a ninety-six hour estimate. It was one `if` away the whole time.
    /// </remarks>
    private static async Task Attempt(List<EntityRead> reads, string componentTypeId, Func<Task<int>> read)
    {
        try
        {
            reads.Add(new EntityRead(componentTypeId, true, await read().ConfigureAwait(false), null));
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            reads.Add(new EntityRead(componentTypeId, false, null, exception.Message));
        }
    }

    private async Task<JsonDocument?> GetAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(new Uri(Api + path, UriKind.Relative), cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            // A refusal on one collection is not a failure of the whole read. Thrown so the
            // wrapper records which collection, rather than swallowed so the section is empty.
            throw new HttpRequestException(
                $"The connection is not allowed to read {path.Split('?')[0]}. " +
                "Every rule depending on it is reported as not assessed rather than as passing.");
        }

        response.EnsureSuccessStatusCode();

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Pages a collection.
    /// </summary>
    /// <remarks>
    /// Follows the next link rather than trusting a count, and stops on an empty page. A page
    /// count that lies is a thing that happens, and a reader that trusts one either stops early
    /// or loops.
    /// </remarks>
    private async IAsyncEnumerable<JsonElement> PageAsync(
        string path,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var next = path;

        while (next is not null)
        {
            using var document = await GetAsync(next, cancellationToken).ConfigureAwait(false);
            if (document is null) yield break;

            if (!document.RootElement.TryGetProperty("value", out var values)) yield break;

            var any = false;
            foreach (var item in values.EnumerateArray())
            {
                any = true;
                yield return item.Clone();
            }

            if (!any) yield break;

            next = document.RootElement.TryGetProperty("@odata.nextLink", out var link)
                ? link.GetString()?.Split(Api, StringSplitOptions.None).LastOrDefault()
                : null;
        }
    }

    private async Task<int> ReadSolutionsAsync(List<DiscoveredComponent> components, CancellationToken cancellationToken)
    {
        var count = 0;

        await foreach (var solution in PageAsync(
            "solutions?$select=uniquename,friendlyname,version,ismanaged,installedon,solutionid&$expand=publisherid($select=customizationprefix,friendlyname)",
            cancellationToken).ConfigureAwait(false))
        {
            var uniqueName = Str(solution, "uniquename");
            if (uniqueName is null) continue;

            components.Add(Component("solution", Str(solution, "solutionid"), Str(solution, "friendlyname") ?? uniqueName, uniqueName,
                Bool(solution, "ismanaged"), null, new Dictionary<string, object?>
                {
                    ["version"] = Str(solution, "version"),
                    ["isManaged"] = Bool(solution, "ismanaged"),
                    ["installedOn"] = Str(solution, "installedon"),
                    ["publisherPrefix"] = solution.TryGetProperty("publisherid", out var publisher)
                        ? Str(publisher, "customizationprefix") : null
                }));

            count++;
        }

        return count;
    }

    private async Task<int> ReadTablesAsync(List<DiscoveredComponent> components, CancellationToken cancellationToken)
    {
        var count = 0;

        await foreach (var table in PageAsync(
            "EntityDefinitions?$select=LogicalName,DisplayName,IsCustomEntity,IsActivity,OwnershipType,IsAuditEnabled,MetadataId&$filter=IsCustomEntity eq true",
            cancellationToken).ConfigureAwait(false))
        {
            var logicalName = Str(table, "LogicalName");
            if (logicalName is null) continue;

            components.Add(Component("table", Str(table, "MetadataId"), logicalName, logicalName, false, null,
                new Dictionary<string, object?>
                {
                    ["isCustom"] = Bool(table, "IsCustomEntity"),
                    ["isActivity"] = Bool(table, "IsActivity"),
                    ["ownershipType"] = Str(table, "OwnershipType")
                }));

            count++;
        }

        return count;
    }

    /// <summary>
    /// Every process: classic workflows, dialogs, business rules, actions, BPFs and cloud flows.
    /// </summary>
    /// <remarks>
    /// They all live in one table and only the category number separates them, exactly as in a
    /// solution export. Category is the load bearing value in this whole reader, and getting it
    /// wrong does not throw: it classifies a business rule as a classic workflow and the report
    /// then recommends migrating forty things that are not there.
    /// </remarks>
    private async Task<int> ReadProcessesAsync(
        List<DiscoveredComponent> components,
        List<ComponentLink> links,
        CancellationToken cancellationToken)
    {
        var count = 0;

        await foreach (var process in PageAsync(
            "workflows?$select=workflowid,name,category,mode,type,statecode,primaryentity,description," +
            "triggeroncreate,triggerondelete,triggeronupdateattributelist,createdon,modifiedon" +
            "&$filter=type eq 1&$expand=ownerid($select=fullname,domainname)",
            cancellationToken).ConfigureAwait(false))
        {
            var category = Int(process, "category") ?? -1;
            var mode = Int(process, "mode") ?? 0;

            var typeId = category switch
            {
                0 => mode == 1 ? "classicWorkflowRealtime" : "classicWorkflowBackground",
                1 => "dialog",
                2 => "businessRule",
                3 => "customProcessAction",
                4 => "businessProcessFlow",
                5 => "cloudFlow",
                6 => "desktopFlow",
                _ => null
            };

            if (typeId is null) continue;

            var name = Str(process, "name") ?? "Unnamed";
            var table = Str(process, "primaryentity");

            var component = Component(typeId, Str(process, "workflowid"), name, name, false,
                process.TryGetProperty("ownerid", out var owner) ? Str(owner, "domainname") : null,
                new Dictionary<string, object?>
                {
                    ["category"] = category,
                    ["mode"] = mode,
                    // Statecode 1 is activated. Everything that separates a live workflow from
                    // a draft one hangs off this single integer.
                    ["statecode"] = Int(process, "statecode")?.ToString(CultureInfo.InvariantCulture),
                    ["primaryEntity"] = table,
                    ["description"] = Str(process, "description"),
                    ["triggerOnCreate"] = Bool(process, "triggeroncreate"),
                    ["triggerOnDelete"] = Bool(process, "triggerondelete"),
                    ["triggerOnUpdateAttributeList"] = Str(process, "triggeronupdateattributelist"),
                    ["modifiedOn"] = Str(process, "modifiedon")
                });

            components.Add(component);

            if (!string.IsNullOrWhiteSpace(table))
            {
                links.Add(new ComponentLink(component.StableKey, StableKeys.ForComponent("table", null, table), "runsOn"));
            }

            count++;
        }

        return count;
    }

    private async Task<int> ReadPluginAssembliesAsync(List<DiscoveredComponent> components, CancellationToken cancellationToken)
    {
        var count = 0;

        await foreach (var assembly in PageAsync(
            "pluginassemblies?$select=pluginassemblyid,name,version,isolationmode,sourcetype,description,ismanaged",
            cancellationToken).ConfigureAwait(false))
        {
            var name = Str(assembly, "name");
            if (name is null) continue;

            components.Add(Component("pluginAssembly", Str(assembly, "pluginassemblyid"), name, name,
                Bool(assembly, "ismanaged"), null, new Dictionary<string, object?>
                {
                    // Isolation mode 2 is sandbox, source type 0 is database. Anything else
                    // cannot be imported into an online environment at all.
                    ["isolationMode"] = Int(assembly, "isolationmode")?.ToString(CultureInfo.InvariantCulture),
                    ["sourceType"] = Int(assembly, "sourcetype")?.ToString(CultureInfo.InvariantCulture),
                    ["version"] = Str(assembly, "version"),
                    ["description"] = Str(assembly, "description")
                }));

            count++;
        }

        return count;
    }

    private async Task<int> ReadPluginStepsAsync(
        List<DiscoveredComponent> components,
        List<ComponentLink> links,
        CancellationToken cancellationToken)
    {
        var count = 0;

        await foreach (var step in PageAsync(
            "sdkmessageprocessingsteps?$select=sdkmessageprocessingstepid,name,stage,mode,filteringattributes," +
            "statecode,ismanaged,description&$expand=sdkmessageid($select=name)",
            cancellationToken).ConfigureAwait(false))
        {
            var name = Str(step, "name") ?? "Step";
            var message = step.TryGetProperty("sdkmessageid", out var sdk) ? Str(sdk, "name") : null;

            // The table a step is registered against lives on a filter record rather than on
            // the step, and reading it is a second call per step. Left for a later pass rather
            // than faked: a step with no table attached is reported as such.
            var component = Component("pluginStep", Str(step, "sdkmessageprocessingstepid"), name, name,
                Bool(step, "ismanaged"), null, new Dictionary<string, object?>
                {
                    ["message"] = message,
                    ["stage"] = Int(step, "stage")?.ToString(CultureInfo.InvariantCulture),
                    ["mode"] = Int(step, "mode")?.ToString(CultureInfo.InvariantCulture),
                    ["filteringAttributes"] = Str(step, "filteringattributes"),
                    ["statecode"] = Int(step, "statecode")?.ToString(CultureInfo.InvariantCulture),
                    ["description"] = Str(step, "description")
                });

            components.Add(component);
            _ = links;
            count++;
        }

        return count;
    }

    /// <summary>
    /// Security roles, with the assignment counts the sprawl and privilege rules need.
    /// </summary>
    /// <remarks>
    /// The counts are the whole point. A role read without them cannot be called unassigned,
    /// and the handler refuses to fire when both come back null rather than recommending a
    /// deletion on no evidence.
    /// </remarks>
    private async Task<int> ReadRolesAsync(List<DiscoveredComponent> components, CancellationToken cancellationToken)
    {
        var count = 0;

        await foreach (var role in PageAsync(
            "roles?$select=roleid,name,ismanaged,description&$filter=ismanaged eq false",
            cancellationToken).ConfigureAwait(false))
        {
            var name = Str(role, "name");
            var roleId = Str(role, "roleid");
            if (name is null || roleId is null) continue;

            var users = await CountAsync($"systemuserroles?$filter=roleid eq {roleId}&$count=true&$top=1", cancellationToken)
                .ConfigureAwait(false);
            var teams = await CountAsync($"teamroles?$filter=roleid eq {roleId}&$count=true&$top=1", cancellationToken)
                .ConfigureAwait(false);

            components.Add(Component("securityRole", roleId, name, name, false, null, new Dictionary<string, object?>
            {
                ["userCount"] = users,
                ["teamCount"] = teams,
                ["description"] = Str(role, "description")
            }));

            count++;
        }

        return count;
    }

    private async Task<int?> CountAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await GetAsync(path, cancellationToken).ConfigureAwait(false);

            return document?.RootElement.TryGetProperty("@odata.count", out var value) == true
                ? value.GetInt32()
                : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            // Null rather than zero, and the difference matters: zero means nobody holds this
            // role, null means nobody could find out.
            return null;
        }
    }

    private async Task<int> ReadConnectionReferencesAsync(List<DiscoveredComponent> components, CancellationToken cancellationToken)
    {
        var count = 0;

        await foreach (var reference in PageAsync(
            "connectionreferences?$select=connectionreferenceid,connectionreferencelogicalname," +
            "connectionreferencedisplayname,connectorid,description,ismanaged",
            cancellationToken).ConfigureAwait(false))
        {
            var logicalName = Str(reference, "connectionreferencelogicalname");
            if (logicalName is null) continue;

            var connectorId = Str(reference, "connectorid");

            components.Add(Component("connectionReference", Str(reference, "connectionreferenceid"),
                Str(reference, "connectionreferencedisplayname") ?? logicalName, logicalName,
                Bool(reference, "ismanaged"), null, new Dictionary<string, object?>
                {
                    ["connectorId"] = connectorId,
                    ["description"] = Str(reference, "description")
                }));

            count++;
        }

        return count;
    }

    private async Task<int> ReadEnvironmentVariablesAsync(List<DiscoveredComponent> components, CancellationToken cancellationToken)
    {
        var count = 0;

        await foreach (var definition in PageAsync(
            "environmentvariabledefinitions?$select=environmentvariabledefinitionid,schemaname,displayname," +
            "type,defaultvalue,description,ismanaged",
            cancellationToken).ConfigureAwait(false))
        {
            var schemaName = Str(definition, "schemaname");
            if (schemaName is null) continue;

            components.Add(Component("environmentVariable", Str(definition, "environmentvariabledefinitionid"),
                Str(definition, "displayname") ?? schemaName, schemaName,
                Bool(definition, "ismanaged"), null, new Dictionary<string, object?>
                {
                    ["type"] = Int(definition, "type")?.ToString(CultureInfo.InvariantCulture),
                    ["hasDefaultValue"] = !string.IsNullOrWhiteSpace(Str(definition, "defaultvalue")),
                    // The value itself is never carried. It ends up in a work item, an Excel
                    // export and a PDF, and a secret typed into a variable would land in all three.
                    ["description"] = Str(definition, "description")
                }));

            count++;
        }

        return count;
    }

    private async Task<int> ReadServiceEndpointsAsync(List<DiscoveredComponent> components, CancellationToken cancellationToken)
    {
        var count = 0;

        await foreach (var endpoint in PageAsync(
            "serviceendpoints?$select=serviceendpointid,name,contract,url,authtype,description,ismanaged",
            cancellationToken).ConfigureAwait(false))
        {
            var name = Str(endpoint, "name");
            if (name is null) continue;

            components.Add(Component("serviceEndpoint", Str(endpoint, "serviceendpointid"), name, name,
                Bool(endpoint, "ismanaged"), null, new Dictionary<string, object?>
                {
                    ["contract"] = Int(endpoint, "contract")?.ToString(CultureInfo.InvariantCulture),
                    ["url"] = Str(endpoint, "url"),
                    ["authType"] = Int(endpoint, "authtype")?.ToString(CultureInfo.InvariantCulture),
                    ["description"] = Str(endpoint, "description")
                }));

            count++;
        }

        return count;
    }

    private async Task<int> ReadReportsAsync(List<DiscoveredComponent> components, CancellationToken cancellationToken)
    {
        var count = 0;

        await foreach (var report in PageAsync(
            "reports?$select=reportid,name,description,ismanaged,modifiedon",
            cancellationToken).ConfigureAwait(false))
        {
            var name = Str(report, "name");
            if (name is null) continue;

            components.Add(Component("report", Str(report, "reportid"), name, name,
                Bool(report, "ismanaged"), null, new Dictionary<string, object?>
                {
                    ["description"] = Str(report, "description"),
                    ["modifiedOn"] = Str(report, "modifiedon")
                }));

            count++;
        }

        return count;
    }

    /// <summary>
    /// Flow run statistics, which are not in Dataverse's own tables.
    /// </summary>
    /// <remarks>
    /// Deliberately left unimplemented rather than faked. Run history lives behind the Power
    /// Automate management API, not the Dataverse Web API, and it needs its own token and its
    /// own consent. Returning zeros here would make every flow look like it never fails.
    /// </remarks>
    private Task<int> ReadFlowRunStatisticsAsync(List<DiscoveredComponent> components, CancellationToken cancellationToken)
    {
        _ = components;
        _ = cancellationToken;

        throw new HttpRequestException(
            "Flow run history is not in the Dataverse Web API. It needs the Power Automate management API, " +
            "a separate token and separate consent, and this reader does not have them yet. Every rule that " +
            "depends on run history is reported as not assessed rather than as passing.");
    }

    private static DiscoveredComponent Component(
        string typeId,
        string? platformId,
        string displayName,
        string schemaName,
        bool isManaged,
        string? ownerUpn,
        Dictionary<string, object?> attributes) =>
        new(Guid.NewGuid(),
            StableKeys.ForComponent(typeId, platformId, schemaName),
            typeId,
            displayName,
            schemaName,
            platformId,
            null,
            isManaged,
            ownerUpn,
            attributes);

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
