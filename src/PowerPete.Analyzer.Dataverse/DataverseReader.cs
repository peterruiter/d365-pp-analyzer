namespace PowerPete.Analyzer.Dataverse;

using System.Globalization;
using System.Net.Http.Json;
using System.Text;
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
    /// <summary>
    /// Every solution in the environment, in or out of scope.
    /// </summary>
    /// <remarks>
    /// Separate from the read, because the question it answers is different. The read says
    /// what was analysed; this says what exists. A report covering four of nineteen
    /// solutions and one covering all nineteen look identical on the cover page unless
    /// somebody has counted both.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<IReadOnlyList<SolutionSummary>> ListSolutionsAsync(CancellationToken cancellationToken = default)
    {
        var solutions = new List<SolutionSummary>();

        await foreach (var solution in PageAsync(
            "solutions?$select=uniquename,friendlyname,version,ismanaged&$expand=publisherid($select=customizationprefix,friendlyname)",
            cancellationToken).ConfigureAwait(false))
        {
            var uniqueName = Str(solution, "uniquename");
            if (uniqueName is null) continue;

            // The default solution and the system ones are in this list and are not somebody
            // customising anything. Counted, because they exist, and the caller decides.
            solutions.Add(new SolutionSummary(
                uniqueName,
                Str(solution, "friendlyname"),
                Str(solution, "version"),
                Bool(solution, "ismanaged"),
                solution.TryGetProperty("publisherid", out var publisher)
                    ? Str(publisher, "customizationprefix")
                    : null,
                solution.TryGetProperty("publisherid", out var owner)
                    ? Str(owner, "friendlyname")
                    : null,
                null));
        }

        return solutions;
    }

    /// <summary>
    /// Exports one solution, as the platform would if somebody clicked Export.
    /// </summary>
    /// <remarks>
    /// This is why a live connection is the richest one and not the poorest.
    ///
    /// Fourteen of this product's rules read a solution file: what is inside a code
    /// component's bundle, whether a flow handles its errors, how much script a form loads,
    /// whether a definition carries a secret. Three more need Microsoft's checker, which
    /// takes a file. None of that is in the Web API, so a run against a live environment
    /// reported seventeen rules as not assessed and an uploaded zip reported them fine,
    /// which made the offline mode look like the better one.
    ///
    /// It was never true. A connection that can read an environment can ask it for the same
    /// zip a person would download, and the extraction-sources contract has always said so:
    /// every live mode declares solutionZip and checker as fully reached. The declaration
    /// was right and nothing implemented it.
    ///
    /// Slow, and worth saying so. One small solution took seventy seconds against a real
    /// environment, so a dozen is a quarter of an hour: this belongs behind a switch the
    /// person starting the run can see, not in the quick scan that promises an answer in
    /// fifteen minutes.
    ///
    /// Nothing is held. The response is a zip base64 encoded inside a JSON string, and it is
    /// piped through <see cref="Base64Property"/> into the destination as it arrives, so a
    /// solution of any size costs a read buffer rather than three copies of itself.
    /// </remarks>
    /// <param name="uniqueName">The solution's unique name.</param>
    /// <param name="destination">Where the zip is written. Streamed, never held. Left open.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Whether a file was written. False means the environment refused to produce one.</returns>
    public async Task<bool> ExportSolutionAsync(
        string uniqueName,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);

        // Unmanaged. A managed export strips the customisations the rules read, so it would
        // come back looking like an estate nobody had built anything in.
        var payload = new { SolutionName = uniqueName, Managed = false };

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{Api}ExportSolution", UriKind.Relative))
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };

        // Headers rather than content, which is the difference between streaming a file and
        // not. PostAsync does not return until it has buffered the whole response, so the
        // entire solution would be in memory before a single byte reached the destination:
        // exactly what writing it to a blob is meant to avoid.
        using var response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return false;

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            return await Base64Property
                .CopyAsync(stream, "ExportSolutionFile", destination, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <param name="solutionUniqueNames">Which solutions are in scope. Empty reads every unmanaged solution.</param>
    /// <param name="includeRuntime">Whether to read run history and trace logs, which need more than a reader.</param>
    /// <param name="progress">Where to say how far through it is. This takes minutes against a real estate.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<Result> ReadAsync(
        IReadOnlyList<string> solutionUniqueNames,
        bool includeRuntime,
        IProgress<StageNote>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solutionUniqueNames);

        var components = new List<DiscoveredComponent>();
        var links = new List<ComponentLink>();
        var reads = new List<EntityRead>();

        // What the chosen solutions actually contain, read before anything else so a failure
        // to work it out stops the run rather than silently widening the scope back to the
        // whole environment.
        //
        // This parameter existed, was documented as "which solutions are in scope", was
        // passed an empty list by its only caller and was never read by this method. A read
        // of a client environment therefore always covered everything, including forty-odd
        // solutions of Microsoft's, which is the slowest possible way to produce a report
        // about somebody else's product.
        var scope = await ScopeAsync(solutionUniqueNames, progress, cancellationToken).ConfigureAwait(false);

        var identity = (await TestAsync(cancellationToken).ConfigureAwait(false)).Identity;

        // Eleven entity reads, twelve with run history. Named here rather than counted in a
        // constant, because a reader added below and a total left up here is how a progress
        // bar ends up saying twelve of eleven.
        var total = includeRuntime ? 12 : 11;
        var done = 0;

        Task Step(string componentTypeId, Func<Task<int>> read)
        {
            progress?.Report(new StageNote("reading", componentTypeId, ++done, total));
            return Attempt(reads, componentTypeId, read);
        }

        await Step("solution", () => ReadSolutionsAsync(components, cancellationToken)).ConfigureAwait(false);
        await Step("table", () => ReadTablesAsync(components, cancellationToken)).ConfigureAwait(false);
        await Step("classicWorkflowBackground", () => ReadProcessesAsync(components, links, cancellationToken)).ConfigureAwait(false);
        await Step("pluginAssembly", () => ReadPluginAssembliesAsync(components, cancellationToken)).ConfigureAwait(false);
        await Step("pluginStep", () => ReadPluginStepsAsync(components, links, cancellationToken)).ConfigureAwait(false);
        await Step("securityRole", () => ReadRolesAsync(components, cancellationToken)).ConfigureAwait(false);
        await Step("connectionReference", () => ReadConnectionReferencesAsync(components, cancellationToken)).ConfigureAwait(false);
        await Step("environmentVariable", () => ReadEnvironmentVariablesAsync(components, cancellationToken)).ConfigureAwait(false);
        await Step("serviceEndpoint", () => ReadServiceEndpointsAsync(components, cancellationToken)).ConfigureAwait(false);
        await Step("report", () => ReadReportsAsync(components, cancellationToken)).ConfigureAwait(false);

        if (includeRuntime)
        {
            await Step("flowRun", () => ReadFlowRunStatisticsAsync(components, cancellationToken)).ConfigureAwait(false);
        }
        else
        {
            reads.Add(new EntityRead("flowRun", false, null,
                "Runtime evidence was not requested on this run, so cloud flow run history was not read. " +
                "Every rule that needs it is reported as not assessed rather than as passing."));
        }

        if (scope is not null)
        {
            components.RemoveAll(component => !InScope(component, scope, solutionUniqueNames));

            // Which solution each surviving component came out of. The reader had no way to
            // know this: it reads tables and plug-ins and flows, and nothing in those
            // records says which solution carries them. solutioncomponent does, and it has
            // already been read to work out the scope.
            for (var index = 0; index < components.Count; index++)
            {
                var component = components[index];

                if (component.SolutionUniqueName is { Length: > 0 }) continue;

                // A solution is its own answer. It is not a component of itself, so it is
                // not in the map.
                if (string.Equals(component.TypeId, "solution", StringComparison.Ordinal))
                {
                    components[index] = component with { SolutionUniqueName = component.SchemaName };
                    continue;
                }

                if (component.PlatformId is { Length: > 0 } platformId
                    && Guid.TryParse(platformId, out var id)
                    && scope.TryGetValue(id, out var solution))
                {
                    components[index] = component with { SolutionUniqueName = solution };
                }
            }

            // The counts are recounted rather than left as read, so every number downstream
            // describes the same set of components. A read that says it found four hundred
            // tables beside a report that discusses twelve is a report nobody trusts.
            for (var index = 0; index < reads.Count; index++)
            {
                var read = reads[index];
                if (!read.Succeeded) continue;

                reads[index] = read with
                {
                    RecordCount = components.Count(component =>
                        string.Equals(component.TypeId, read.ComponentTypeId, StringComparison.Ordinal))
                };
            }
        }

        return new Result(components, links, reads, identity);
    }

    /// <summary>
    /// The platform identifiers the chosen solutions contain.
    /// </summary>
    /// <remarks>
    /// Null when nothing was chosen, which means everything is in scope. An empty set is a
    /// different answer: it means the chosen solutions are genuinely empty, and returning
    /// null for that would read the whole environment instead.
    ///
    /// Read from solutioncomponent rather than inferred from a prefix. A prefix says who
    /// published a component, not which solution carries it, and the two diverge the moment
    /// anybody adds an existing table to a new solution.
    /// </remarks>
    /// <param name="solutionUniqueNames">The chosen solutions.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <param name="progress">Where to say which solution it is on.</param>
    private async Task<Dictionary<Guid, string>?> ScopeAsync(
        IReadOnlyList<string> solutionUniqueNames,
        IProgress<StageNote>? progress,
        CancellationToken cancellationToken)
    {
        if (solutionUniqueNames.Count == 0) return null;

        // Which solution, not just whether. The same read answers both questions and the
        // inventory had no answer to the first: every component came back with no solution
        // on it, so a consultant looking at a finding could not say where it lived and the
        // report could not group by solution at all.
        var solutionIds = new Dictionary<Guid, string>();
        var filter = string.Join(" or ", solutionUniqueNames.Select(name => $"uniquename eq '{Escape(name)}'"));

        await foreach (var solution in PageAsync(
            $"solutions?$select=solutionid,uniquename&$filter={Uri.EscapeDataString(filter)}",
            cancellationToken).ConfigureAwait(false))
        {
            if (solution.TryGetProperty("solutionid", out var id)
                && Guid.TryParse(id.GetString(), out var parsed))
            {
                solutionIds[parsed] = Str(solution, "uniquename") ?? string.Empty;
            }
        }

        var objectIds = new Dictionary<Guid, string>();

        // One request per solution rather than one filter listing them all. A filter naming
        // nineteen solutions is a URL long enough to be refused, and the refusal reads as a
        // bad request rather than as a URL length.
        var scoped = 0;

        foreach (var (solutionId, uniqueName) in solutionIds)
        {
            // One solution's components can be tens of thousands of rows. Saying which one
            // is the difference between a run that is working through a large estate and one
            // that has stopped on the first.
            progress?.Report(new StageNote("scoping", uniqueName, ++scoped, solutionIds.Count));

            await foreach (var component in PageAsync(
                $"solutioncomponents?$select=objectid&$filter=_solutionid_value eq {solutionId}",
                cancellationToken).ConfigureAwait(false))
            {
                if (component.TryGetProperty("objectid", out var objectId)
                    && Guid.TryParse(objectId.GetString(), out var parsed))
                {
                    // First wins. A component can be in more than one solution, and the
                    // report needs one answer to "where does this live" rather than a list
                    // nobody can act on. The chosen solutions are read in the order the
                    // picker showed them.
                    objectIds.TryAdd(parsed, uniqueName);
                }
            }
        }

        return objectIds;
    }

    /// <summary>Whether one component belongs to the chosen solutions.</summary>
    /// <remarks>
    /// Two ways in, because the readers fill two different things. Most carry the platform
    /// identifier, which is what solutioncomponent lists. The solution read itself carries
    /// the unique name, and a solution somebody chose has to survive the filter that the
    /// choice created.
    /// </remarks>
    /// <param name="component">The component.</param>
    /// <param name="scope">Identifiers the chosen solutions contain.</param>
    /// <param name="chosen">The chosen unique names.</param>
    private static bool InScope(
        DiscoveredComponent component, Dictionary<Guid, string> scope, IReadOnlyList<string> chosen)
    {
        // A solution is not a component of itself, so its own identifier is not in
        // solutioncomponent and the scope check below threw all eleven chosen solutions out
        // of the inventory they defined. The run read their contents and reported that it
        // had read no solutions.
        //
        // The reader puts the unique name in the schema name for this type, which is what
        // the choice was made against.
        if (string.Equals(component.TypeId, "solution", StringComparison.Ordinal))
        {
            return component.SchemaName is { Length: > 0 } name
                && chosen.Contains(name, StringComparer.OrdinalIgnoreCase);
        }

        if (component.SolutionUniqueName is { Length: > 0 } solution
            && chosen.Contains(solution, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return component.PlatformId is { Length: > 0 } platformId
            && Guid.TryParse(platformId, out var id)
            && scope.ContainsKey(id);
    }

    /// <summary>A literal safe to put in an OData filter.</summary>
    /// <param name="value">The literal.</param>
    private static string Escape(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);

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
            // owninguser, not ownerid. ownerid is a polymorphic Owner lookup, which can be a
            // user or a team, and Dataverse refuses a $select inside a $expand on one with a
            // 400. Every classic workflow read against a real environment failed on it, and
            // the failure was correctly reported as one entity read of eleven rather than
            // taking the run down, which is why it survived to be found here.
            //
            // owninguser is the non-polymorphic half and carries the UPN this wants. A
            // workflow owned by a team has none, which is a null owner rather than an error.
            "workflows?$select=workflowid,name,category,mode,type,statecode,primaryentity,description," +
            "triggeroncreate,triggerondelete,triggeronupdateattributelist,createdon,modifiedon" +
            "&$filter=type eq 1&$expand=owninguser($select=domainname,fullname)",
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
                process.TryGetProperty("owninguser", out var owner) ? Str(owner, "domainname") : null,
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
    /// How often each cloud flow ran in the last thirty days, and how often it failed.
    /// </summary>
    /// <remarks>
    /// Read from Dataverse's own flowrun table rather than from the Power Automate
    /// management API. This used to throw on the grounds that run history was not in
    /// Dataverse, which stopped being true: solution-aware cloud flows write a row per run,
    /// and reading it needs no second token and no separate consent.
    ///
    /// Two things make this safe to get wrong. The read is wrapped by <see cref="Attempt"/>,
    /// so an environment without the table, or without the privilege, records a failed read
    /// and every rule needing run history reports as not assessed, which is exactly what
    /// happened before this existed. And the status is classified from its label rather than
    /// its number, strictly: a status this code does not recognise fails the whole read
    /// instead of being counted as a success.
    ///
    /// That last part is the difference between useful and dangerous. Quietly treating an
    /// unrecognised status as "not a failure" would report a flow that fails every night as
    /// running perfectly, and a critical severity rule would go silent on the one estate
    /// that needed it.
    ///
    /// Only the flows that actually have rows are touched. A flow with no runs in the window
    /// is left alone rather than being stamped with a zero: the table does not retain runs
    /// forever, and "no runs in the last thirty days" and "no rows retained" are different
    /// statements that would both read as a dormant flow.
    /// </remarks>
    /// <param name="components">Everything read so far, including the cloud flows to attach to.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    private async Task<int> ReadFlowRunStatisticsAsync(
        List<DiscoveredComponent> components,
        CancellationToken cancellationToken)
    {
        // By index, because the statistics are merged into the component by replacing it.
        // DiscoveredComponent is a record with a read only attribute dictionary, and casting
        // that back to the concrete type it happened to be built from would work today and
        // break the moment somebody hands one in from somewhere else.
        var flows = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < components.Count; index++)
        {
            if (components[index] is { TypeId: "cloudFlow", PlatformId: { } id }) flows[id] = index;
        }

        if (flows.Count == 0) return 0;

        var since = DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        var tally = new Dictionary<string, FlowTally>(StringComparer.OrdinalIgnoreCase);

        await foreach (var run in PageAsync(
            "flowruns?$select=starttime,endtime,status,errormessage,_workflow_value" +
            $"&$filter=starttime ge {since}",
            cancellationToken).ConfigureAwait(false))
        {
            var workflow = Str(run, "_workflow_value");
            if (workflow is null || !flows.ContainsKey(workflow)) continue;

            // The label rather than the number. An option set's integers are not a contract
            // and this code has no environment to check them against.
            var label = Str(run, "status@OData.Community.Display.V1.FormattedValue")
                ?? throw new HttpRequestException(
                    "A flow run came back with no readable status, so the failure rate cannot be counted. "
                    + "Every rule needing run history is reported as not assessed rather than as passing.");

            var failed = label switch
            {
                "Succeeded" => false,
                "Failed" => true,

                // Neither. A run still going, or one somebody stopped, is not evidence about
                // whether the flow works and must not move the rate in either direction.
                "Running" or "Cancelled" or "Cancelling" or "Waiting" or "Paused" => (bool?)null,

                _ => throw new HttpRequestException(
                    $"A flow run came back with the status '{label}', which this reader does not know how to "
                    + "classify. Counting it as a success would report a failing flow as healthy, so the whole "
                    + "read is refused and every rule needing run history is reported as not assessed.")
            };

            if (!tally.TryGetValue(workflow, out var current))
            {
                current = new FlowTally();
                tally[workflow] = current;
            }

            if (failed is null)
            {
                current.Unfinished++;
                continue;
            }

            current.Finished++;
            if (failed.Value) current.Failed++;

            if (failed.Value && Str(run, "errormessage") is { Length: > 0 } message)
            {
                current.LastFailure = message;
            }

            if (Duration(run) is { } duration)
            {
                current.TotalMilliseconds += duration;
                current.Timed++;
            }
        }

        foreach (var (workflow, counts) in tally)
        {
            var index = flows[workflow];
            var flow = components[index];

            // Merged into the component the extraction already produced. The attributes
            // dictionary is what every rule reads, and a second collection keyed on the same
            // flow would be a second answer to the same question.
            var attributes = new Dictionary<string, object?>(flow.Attributes, StringComparer.Ordinal);

            attributes["runCount30d"] = counts.Finished;
            attributes["failureCount30d"] = counts.Failed;

            attributes["failureRate30d"] = counts.Finished == 0
                ? 0m
                : Math.Round((decimal)counts.Failed / counts.Finished, 3);

            if (counts.Timed > 0)
            {
                attributes["averageDurationMs"] = (int)(counts.TotalMilliseconds / counts.Timed);
            }

            if (counts.LastFailure is not null) attributes["lastFailureMessage"] = counts.LastFailure;

            components[index] = flow with { Attributes = attributes };
        }

        return tally.Count;
    }

    /// <summary>How long one run took, where both ends of it were recorded.</summary>
    /// <param name="run">The run.</param>
    private static double? Duration(JsonElement run)
    {
        if (!DateTimeOffset.TryParse(Str(run, "starttime"), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal, out var start))
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(Str(run, "endtime"), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal, out var end))
        {
            // Still running, or the end was never written. Not a duration.
            return null;
        }

        var elapsed = (end - start).TotalMilliseconds;

        return elapsed >= 0 ? elapsed : null;
    }

    /// <summary>What one flow's runs added up to.</summary>
    private sealed class FlowTally
    {
        /// <summary>Runs that reached an end, which is the denominator of the rate.</summary>
        public int Finished { get; set; }

        /// <summary>Runs still going or cancelled, counted and deliberately not in the rate.</summary>
        public int Unfinished { get; set; }

        /// <summary>How many of the finished ones failed.</summary>
        public int Failed { get; set; }

        /// <summary>Runs with both ends recorded, so an average means something.</summary>
        public int Timed { get; set; }

        /// <summary>The sum of those durations.</summary>
        public double TotalMilliseconds { get; set; }

        /// <summary>The most recent failure's message, which is what a consultant reads first.</summary>
        public string? LastFailure { get; set; }
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
