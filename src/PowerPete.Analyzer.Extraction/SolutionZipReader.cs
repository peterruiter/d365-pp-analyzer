namespace PowerPete.Analyzer.Extraction;

using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PowerPete.Analyzer.Domain;

/// <summary>
/// Reads an exported solution file.
/// </summary>
/// <remarks>
/// The mode every engagement starts in. Needs no credential, no application user and no
/// security review: somebody at the client exports a solution and sends it, which happens in
/// an afternoon rather than in the three weeks an access request takes.
///
/// It reaches everything static and nothing operational. No row counts, no user assignments,
/// no run history, and no sight of components outside the solutions that were exported. Each
/// of those absences is recorded as an unreachable evidence source rather than left to be
/// inferred from an empty section of a report.
///
/// Every numeric code this reader maps, the workflow categories in particular, comes from
/// documentation rather than from an environment. A wrong code does not throw. It silently
/// classifies a business rule as a classic workflow, and the report then tells a client to
/// migrate forty things that are not there. Verify them against a real export before the
/// analysis believes any of it.
/// </remarks>
public sealed class SolutionZipReader
{
    private static readonly XNamespace None = XNamespace.None;

    /// <summary>
    /// Workflow categories, as they appear in customizations.xml.
    /// </summary>
    /// <remarks>
    /// The single most load bearing mapping in this reader. Everything in the logic domain
    /// arrives as a Workflow node and only this number tells a cloud flow from a dialog.
    /// </remarks>
    private static readonly Dictionary<int, string> WorkflowCategories = new()
    {
        [0] = "classicWorkflow",       // resolved further by Mode, below
        [1] = "dialog",
        [2] = "businessRule",
        [3] = "customProcessAction",
        [4] = "businessProcessFlow",
        [5] = "cloudFlow",
        [6] = "desktopFlow"
    };

    /// <summary>What was read, and what could not be.</summary>
    /// <param name="Components">Everything found.</param>
    /// <param name="Links">References this reader could resolve from the file alone.</param>
    /// <param name="Unresolved">References pointing outside the file.</param>
    /// <param name="Solutions">The solutions the file contained.</param>
    /// <param name="Reads">One record per component type attempted, successful or not.</param>
    public sealed record Result(
        IReadOnlyList<DiscoveredComponent> Components,
        IReadOnlyList<ComponentLink> Links,
        IReadOnlyList<UnresolvedLink> Unresolved,
        IReadOnlyList<SolutionHeader> Solutions,
        IReadOnlyList<EntityRead> Reads);

    /// <summary>One solution, from its manifest.</summary>
    /// <param name="UniqueName">The name everything else keys on.</param>
    /// <param name="FriendlyName">What a person calls it.</param>
    /// <param name="Version">Its version.</param>
    /// <param name="IsManaged">Whether it was exported managed.</param>
    /// <param name="PublisherPrefix">The customisation prefix.</param>
    /// <param name="PublisherName">Who published it.</param>
    public sealed record SolutionHeader(
        string UniqueName,
        string FriendlyName,
        string? Version,
        bool IsManaged,
        string? PublisherPrefix,
        string? PublisherName);

    /// <summary>One attempt to read one component type.</summary>
    /// <param name="ComponentTypeId">What was being read.</param>
    /// <param name="Succeeded">Whether it worked.</param>
    /// <param name="RecordCount">How many, on success.</param>
    /// <param name="FailureReason">Why not, on failure. What a consultant reads when a client asks why a section is empty.</param>
    public sealed record EntityRead(string ComponentTypeId, bool Succeeded, int? RecordCount, string? FailureReason);

    /// <summary>
    /// Reads one exported solution file.
    /// </summary>
    /// <param name="stream">The zip. Left open: the caller owns it.</param>
    /// <exception cref="InvalidDataException">The file is not a solution export.</exception>
    public static Result Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        var manifest = archive.GetEntry("solution.xml")
            ?? throw new InvalidDataException(
                "No solution.xml in the file. This is a zip but it is not an exported solution. " +
                "The most common cause is somebody sending the managed and unmanaged exports zipped together.");

        var customisations = archive.GetEntry("customizations.xml")
            ?? throw new InvalidDataException(
                "No customizations.xml in the file. A solution export always has one, so this file is either " +
                "truncated or was assembled by hand.");

        var components = new List<DiscoveredComponent>();
        var links = new List<ComponentLink>();
        var unresolved = new List<UnresolvedLink>();
        var reads = new List<EntityRead>();

        var solution = ReadManifest(manifest);
        var root = Load(customisations);

        Attempt(reads, components, "table", () => ReadEntities(root, solution, components, links));
        Attempt(reads, components, "cloudFlow", () => ReadWorkflows(archive, root, solution, components, links, unresolved));
        Attempt(reads, components, "jsWebResource", () => ReadWebResources(archive, root, solution, components));
        Attempt(reads, components, "connectionReference", () => ReadConnectionReferences(root, solution, components));
        Attempt(reads, components, "environmentVariable", () => ReadEnvironmentVariables(archive, root, solution, components));
        Attempt(reads, components, "securityRole", () => ReadRoles(root, solution, components));
        Attempt(reads, components, "pluginAssembly", () => ReadPluginAssemblies(root, solution, components));
        Attempt(reads, components, "canvasApp", () => ReadCanvasApps(root, solution, components));
        Attempt(reads, components, "choice", () => ReadGlobalChoices(root, solution, components));
        Attempt(reads, components, "customApi", () => ReadCustomApis(archive, solution, components));
        Attempt(reads, components, "pcfControl", () => ReadCustomControls(archive, root, solution, components));
        Attempt(reads, components, "modelDrivenApp", () => ReadApps(root, solution, components));

        // Everything this mode cannot see, said out loud. A section of a report that is empty
        // because nobody could read it has to look different from one that is empty because
        // there was nothing there.
        reads.Add(new EntityRead("pluginStep", false, null,
            "Step registrations are not in a solution export in a form this reader can use. Connect an environment to assess them."));
        reads.Add(new EntityRead("dataflow", false, null,
            "Dataflows do not travel in a solution. They are invisible to an offline read, which is itself one of the findings about them."));

        // A solution that yields nothing is a fault until proven otherwise.
        //
        // Every read above can succeed and return zero, and ten zeroes sum to a clean empty
        // estate, which is the most damaging thing this product can report. It happened on
        // the first real export it was pointed at: a solution holding one PCF control came
        // back with no components and no failures, because nothing read CustomControls.
        //
        // A solution file always contains something, so nothing found means this reader
        // cannot see what is in it rather than that there is nothing there. Said as a failed
        // read so the report carries it into the not assessed section instead of printing a
        // zero next to a client's name.
        if (components.Count == 0)
        {
            reads.Add(new EntityRead("solution", false, null,
                "This solution file was read and produced no components at all. A solution always contains "
                + "something, so this means the reader does not understand what is in this one rather than "
                + "that the solution is empty. Nothing about this estate is reported as assessed."));
        }

        return new Result(components, links, unresolved, [solution], reads);
    }

    /// <summary>
    /// Runs one read and records what it actually produced, whichever way it went.
    /// </summary>
    /// <remarks>
    /// The whole reason this wrapper exists. A read that throws must not take the run with it
    /// and must not vanish: twenty-three reads each failing separately, summed, look exactly
    /// like an estate with nothing in it, and that report would be believed.
    ///
    /// The counts are taken from the components the read added rather than from a number the
    /// read kept for itself, and they are recorded per component type. Those two things used
    /// to be the same number and they are not: reading the entities of a real export
    /// reported "table: 284" for a solution holding ten tables, because the same pass also
    /// produces the columns, the views and the forms. A coverage section built on that
    /// overstates what was read by a factor of twenty-eight, in a product whose entire
    /// argument is that it does not overstate what it read.
    ///
    /// The label is only used when a read produced nothing, so a read that came back empty
    /// still says which component type came back empty.
    /// </remarks>
    private static void Attempt(
        List<EntityRead> reads,
        List<DiscoveredComponent> components,
        string componentTypeId,
        Func<int> read)
    {
        var before = components.Count;

        try
        {
            read();
        }
        catch (Exception exception) when (exception is System.Xml.XmlException or JsonException or InvalidDataException or IOException)
        {
            reads.Add(new EntityRead(componentTypeId, false, null, exception.Message));
            return;
        }

        var produced = components
            .Skip(before)
            .GroupBy(component => component.TypeId, StringComparer.Ordinal)
            .ToList();

        if (produced.Count == 0)
        {
            reads.Add(new EntityRead(componentTypeId, true, 0, null));
            return;
        }

        foreach (var group in produced)
        {
            // Summed rather than replaced. Two reads can legitimately produce the same type,
            // and the second one overwriting the first would report the smaller number.
            var existing = reads.FindIndex(entry => entry.ComponentTypeId == group.Key && entry.Succeeded);

            if (existing >= 0)
            {
                reads[existing] = reads[existing] with
                {
                    RecordCount = (reads[existing].RecordCount ?? 0) + group.Count()
                };

                continue;
            }

            reads.Add(new EntityRead(group.Key, true, group.Count(), null));
        }
    }

    private static XElement Load(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream).Root
            ?? throw new InvalidDataException($"{entry.FullName} is empty.");
    }

    private static SolutionHeader ReadManifest(ZipArchiveEntry entry)
    {
        var root = Load(entry);
        var manifest = root.Descendants(None + "SolutionManifest").FirstOrDefault()
            ?? throw new InvalidDataException("solution.xml has no SolutionManifest element.");

        var publisher = manifest.Element(None + "Publisher");

        return new SolutionHeader(
            Value(manifest, "UniqueName") ?? "unknown",
            manifest.Element(None + "LocalizedNames")?.Elements().FirstOrDefault()?.Attribute("description")?.Value
                ?? Value(manifest, "UniqueName") ?? "unknown",
            Value(manifest, "Version"),
            Value(manifest, "Managed") == "1",
            publisher is null ? null : Value(publisher, "CustomizationPrefix"),
            publisher?.Element(None + "LocalizedNames")?.Elements().FirstOrDefault()?.Attribute("description")?.Value);
    }

    private static string? Value(XElement parent, string name) => parent.Element(None + name)?.Value?.Trim();

    private static int ReadEntities(XElement root, SolutionHeader solution, List<DiscoveredComponent> components, List<ComponentLink> links)
    {
        var count = 0;

        foreach (var entity in root.Descendants(None + "Entity"))
        {
            var name = entity.Element(None + "Name");
            if (name is null) continue;

            var logicalName = name.Value.Trim();
            var tableKey = Add(components, "table", null, logicalName, logicalName, solution, new Dictionary<string, object?>
            {
                ["isCustom"] = name.Attribute("IsCustomEntity")?.Value == "1",
                ["isActivity"] = entity.Descendants(None + "IsActivity").FirstOrDefault()?.Value == "1",
                ["ownershipType"] = entity.Descendants(None + "OwnershipTypeMask").FirstOrDefault()?.Value,
                ["auditEnabled"] = entity.Descendants(None + "IsAuditEnabled").FirstOrDefault()?.Value == "1"
            });
            count++;

            foreach (var attribute in entity.Descendants(None + "attribute"))
            {
                var physicalName = attribute.Attribute("PhysicalName")?.Value;
                if (string.IsNullOrWhiteSpace(physicalName)) continue;

                var columnKey = Add(components, "column", null, physicalName, $"{logicalName}.{physicalName}", solution, new Dictionary<string, object?>
                {
                    ["table"] = logicalName,
                    ["attributeType"] = attribute.Element(None + "Type")?.Value,
                    ["requiredLevel"] = attribute.Element(None + "RequiredLevel")?.Value,
                    ["maxLength"] = attribute.Element(None + "MaxLength")?.Value,
                    ["isCustom"] = attribute.Element(None + "IsCustomField")?.Value == "1",
                    ["isAuditEnabled"] = attribute.Element(None + "IsAuditEnabled")?.Value == "1",
                    ["description"] = attribute.Descendants(None + "Description").FirstOrDefault()?.Attribute("description")?.Value
                });

                links.Add(new ComponentLink(columnKey, tableKey, "belongsTo"));
                count++;
            }

            // Forms and views live inside their entity. The form's XML is what says which
            // scripts it loads, which is the evidence behind two rules in the catalogue.
            foreach (var form in entity.Descendants(None + "systemform"))
            {
                var formId = form.Element(None + "formid")?.Value;
                var formName = form.Descendants(None + "label").FirstOrDefault()?.Attribute("description")?.Value ?? "Form";
                var formXml = form.Element(None + "form");

                var libraries = formXml?.Descendants(None + "Library")
                    .Select(library => library.Attribute("name")?.Value)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList() ?? [];

                var formKey = Add(components, "form", formId, formName, $"{logicalName}.form.{formId ?? formName}", solution, new Dictionary<string, object?>
                {
                    ["table"] = logicalName,
                    ["formType"] = form.Element(None + "type")?.Value,
                    ["tabCount"] = formXml?.Descendants(None + "tab").Count() ?? 0,
                    ["fieldCount"] = formXml?.Descendants(None + "control").Count() ?? 0,
                    ["scriptLibraryCount"] = libraries.Count,
                    ["hasOnLoadScript"] = formXml?.Descendants(None + "event").Any(e => e.Attribute("name")?.Value == "onload") ?? false
                });

                foreach (var library in libraries)
                {
                    links.Add(new ComponentLink(formKey, StableKeys.ForComponent("jsWebResource", null, library), "loadsScript"));
                }

                count++;
            }

            foreach (var view in entity.Descendants(None + "savedquery"))
            {
                var viewId = view.Element(None + "savedqueryid")?.Value;
                var fetch = view.Descendants(None + "fetchxml").FirstOrDefault();

                Add(components, "view", viewId, view.Descendants(None + "LocalizedName").FirstOrDefault()?.Attribute("description")?.Value ?? "View",
                    $"{logicalName}.view.{viewId}", solution, new Dictionary<string, object?>
                    {
                        ["table"] = logicalName,
                        ["isDefault"] = view.Element(None + "isdefault")?.Value == "1",
                        ["queryType"] = view.Element(None + "querytype")?.Value,
                        ["columnCount"] = fetch?.Descendants(None + "attribute").Count() ?? 0,
                        ["hasLinkedEntities"] = fetch?.Descendants(None + "link-entity").Any() ?? false,
                        ["linkedEntityCount"] = fetch?.Descendants(None + "link-entity").Count() ?? 0
                    });
                count++;
            }
        }

        return count;
    }

    private static int ReadWorkflows(
        ZipArchive archive,
        XElement root,
        SolutionHeader solution,
        List<DiscoveredComponent> components,
        List<ComponentLink> links,
        List<UnresolvedLink> unresolved)
    {
        var count = 0;

        foreach (var workflow in root.Descendants(None + "Workflow"))
        {
            var id = workflow.Attribute("WorkflowId")?.Value;
            var name = workflow.Attribute("Name")?.Value ?? "Unnamed";

            var category = int.TryParse(Value(workflow, "Category"), out var parsedCategory) ? parsedCategory : -1;
            var mode = int.TryParse(Value(workflow, "Mode"), out var parsedMode) ? parsedMode : 0;

            if (!WorkflowCategories.TryGetValue(category, out var typeId))
            {
                // Not dropped. A workflow category this reader does not recognise is either a
                // new one Microsoft added or a wrong mapping here, and both are worth seeing.
                unresolved.Add(new UnresolvedLink(
                    StableKeys.ForComponent("classicWorkflowBackground", id, name),
                    "unknownWorkflowCategory",
                    $"Workflow '{name}' has category {category}, which this reader does not recognise."));
                continue;
            }

            // Category 0 covers both kinds of classic workflow and only Mode separates them.
            // They need different replacements, and recommending the same one for both is the
            // most common bad advice in this space.
            if (typeId == "classicWorkflow")
            {
                typeId = mode == 1 ? "classicWorkflowRealtime" : "classicWorkflowBackground";
            }

            var attributes = new Dictionary<string, object?>
            {
                ["category"] = category,
                ["mode"] = mode,
                ["scope"] = Value(workflow, "Scope"),
                ["statecode"] = Value(workflow, "StateCode"),
                ["primaryEntity"] = Value(workflow, "PrimaryEntity"),
                ["triggerOnCreate"] = Value(workflow, "TriggerOnCreate") == "1",
                ["triggerOnDelete"] = Value(workflow, "TriggerOnDelete") == "1",
                ["triggerOnUpdateAttributeList"] = Value(workflow, "TriggerOnUpdateAttributeList"),
                ["isSubprocess"] = Value(workflow, "Subprocess") == "1",
                ["description"] = Value(workflow, "Description")
            };

            var jsonFile = Value(workflow, "JsonFileName");
            if (!string.IsNullOrWhiteSpace(jsonFile))
            {
                ReadFlowDefinition(archive, jsonFile, attributes, unresolved, StableKeys.ForComponent(typeId, id, name));
            }

            var xamlFile = Value(workflow, "XamlFileName");
            if (!string.IsNullOrWhiteSpace(xamlFile))
            {
                attributes["stepCount"] = CountXamlSteps(archive, xamlFile);
            }

            var key = Add(components, typeId, id, name, name, solution, attributes);

            var primary = Value(workflow, "PrimaryEntity");
            if (!string.IsNullOrWhiteSpace(primary))
            {
                links.Add(new ComponentLink(key, StableKeys.ForComponent("table", null, primary), "runsOn"));
            }

            count++;
        }

        return count;
    }

    /// <summary>
    /// Reads a cloud flow's definition for the attributes three rules depend on.
    /// </summary>
    /// <remarks>
    /// Action counting walks the definition recursively rather than counting the top level,
    /// because a flow with one scope containing forty actions is exactly the flow the size
    /// rule is looking for and a top level count would report it as having one action.
    /// </remarks>
    private static void ReadFlowDefinition(
        ZipArchive archive,
        string path,
        Dictionary<string, object?> attributes,
        List<UnresolvedLink> unresolved,
        string flowKey)
    {
        var entry = archive.GetEntry(path.TrimStart('/'));
        if (entry is null)
        {
            unresolved.Add(new UnresolvedLink(flowKey, "missingDefinition",
                $"The manifest names {path} but the file is not in the archive."));
            return;
        }

        using var stream = entry.Open();
        using var document = JsonDocument.Parse(stream);

        if (!document.RootElement.TryGetProperty("properties", out var properties)) return;

        if (properties.TryGetProperty("definition", out var definition))
        {
            var depth = 0;
            var actions = 0;

            if (definition.TryGetProperty("actions", out var actionsElement))
            {
                (actions, depth) = CountActions(actionsElement, 1);
            }

            // The definition itself, because the rules that look for a hard coded endpoint or
            // a secret read the text. Counting the actions and throwing the text away meant
            // both rules searched web resources and reported every flow as clean.
            attributes["definitionText"] = definition.GetRawText();
            attributes["actionCount"] = actions;
            attributes["maxDepth"] = depth;
            attributes["triggerType"] = definition.TryGetProperty("triggers", out var triggers)
                ? triggers.EnumerateObject().FirstOrDefault().Value.TryGetProperty("type", out var type) ? type.GetString() : null
                : null;

            // A scope with a runAfter carrying anything other than Succeeded is the failure
            // path. Its absence is the evidence behind the error handling rule, so it is read
            // here rather than guessed at from the action count.
            attributes["hasErrorHandling"] = definition.GetRawText()
                .Contains("\"Failed\"", StringComparison.Ordinal);
        }

        if (properties.TryGetProperty("connectionReferences", out var references))
        {
            var names = references.EnumerateObject()
                .Select(reference => reference.Value.TryGetProperty("connection", out var connection)
                    && connection.TryGetProperty("connectionReferenceLogicalName", out var logical)
                        ? logical.GetString()
                        : null)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();

            attributes["connectionReferenceCount"] = names.Count;
            attributes["connectorIds"] = string.Join(",", references.EnumerateObject()
                .Select(reference => reference.Value.TryGetProperty("api", out var api)
                    && api.TryGetProperty("name", out var apiName) ? apiName.GetString() : null)
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        }
    }

    private static (int Count, int Depth) CountActions(JsonElement actions, int depth)
    {
        var count = 0;
        var deepest = depth;

        foreach (var action in actions.EnumerateObject())
        {
            count++;

            foreach (var property in action.Value.EnumerateObject())
            {
                // A scope, a condition and a foreach all nest their children under a property
                // called actions, else or default. Walking by name rather than by action type
                // means a new container type is counted rather than silently flattening.
                if (property.Name is "actions" or "else" or "default" && property.Value.ValueKind == JsonValueKind.Object)
                {
                    var (nested, nestedDepth) = CountActions(property.Value, depth + 1);
                    count += nested;
                    deepest = Math.Max(deepest, nestedDepth);
                }
                else if (property.Name == "cases" && property.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var branch in property.Value.EnumerateObject())
                    {
                        if (!branch.Value.TryGetProperty("actions", out var branchActions)) continue;
                        var (nested, nestedDepth) = CountActions(branchActions, depth + 1);
                        count += nested;
                        deepest = Math.Max(deepest, nestedDepth);
                    }
                }
            }
        }

        return (count, deepest);
    }

    private static int CountXamlSteps(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path.TrimStart('/'));
        if (entry is null) return 0;

        using var stream = entry.Open();
        var document = XDocument.Load(stream);

        // Activity elements under the workflow body. An approximation, and honestly so: the
        // step count feeds a complexity multiplier rather than an estimate on its own, and a
        // count that is out by a few does not move the multiplier band.
        return document.Descendants()
            .Count(element => element.Name.LocalName is "SetEntityProperty" or "CreateEntity" or "UpdateEntity"
                or "AssignEntity" or "SendEmail" or "ActivityReference" or "If" or "Switch");
    }

    private static int ReadWebResources(ZipArchive archive, XElement root, SolutionHeader solution, List<DiscoveredComponent> components)
    {
        var count = 0;

        foreach (var resource in root.Descendants(None + "WebResource"))
        {
            var name = Value(resource, "Name");
            if (string.IsNullOrWhiteSpace(name)) continue;

            var type = Value(resource, "WebResourceType");
            var typeId = type switch
            {
                "3" => "jsWebResource",
                "1" => "htmlWebResource",
                _ => null
            };

            if (typeId is null) continue;

            var file = Value(resource, "FileName");
            var size = 0L;
            var content = string.Empty;

            if (!string.IsNullOrWhiteSpace(file))
            {
                var entry = archive.GetEntry(file.TrimStart('/'));
                if (entry is not null)
                {
                    size = entry.Length;

                    // Only the small ones are read into memory. A minified framework of two
                    // megabytes tells the rules nothing they cannot get from its size, and
                    // reading every web resource in a large solution is how this becomes the
                    // slowest stage in the product.
                    if (typeId == "jsWebResource" && entry.Length < 256 * 1024)
                    {
                        using var stream = entry.Open();
                        using var reader = new StreamReader(stream);
                        content = reader.ReadToEnd();
                    }
                }
            }

            Add(components, typeId, Value(resource, "WebResourceId"), name, name, solution, new Dictionary<string, object?>
            {
                ["sizeBytes"] = size,
                ["isMinified"] = name.Contains(".min.", StringComparison.OrdinalIgnoreCase),
                ["contentAvailable"] = content.Length > 0,
                ["content"] = content,
                ["description"] = Value(resource, "Description")
            });

            count++;
        }

        return count;
    }

    private static int ReadConnectionReferences(XElement root, SolutionHeader solution, List<DiscoveredComponent> components)
    {
        var count = 0;

        foreach (var reference in root.Descendants(None + "connectionreference"))
        {
            var logicalName = reference.Attribute("connectionreferencelogicalname")?.Value
                ?? Value(reference, "connectionreferencelogicalname");
            if (string.IsNullOrWhiteSpace(logicalName)) continue;

            var connectorId = Value(reference, "connectorid");

            Add(components, "connectionReference", null,
                Value(reference, "connectionreferencedisplayname") ?? logicalName,
                logicalName, solution, new Dictionary<string, object?>
                {
                    ["connectorId"] = connectorId,
                    ["isCustom"] = connectorId?.Contains("/apis/", StringComparison.OrdinalIgnoreCase) == false,
                    ["description"] = Value(reference, "description")
                });

            count++;
        }

        return count;
    }

    /// <summary>
    /// Environment variable definitions, wherever the export put them.
    /// </summary>
    /// <remarks>
    /// A real export writes one file per variable under environmentvariabledefinitions, and
    /// customizations.xml carries nothing. Reading only customizations.xml found none of the
    /// fourteen in the first real solution this was run against, and reported an estate with
    /// no environment variables rather than a reader looking in the wrong place.
    ///
    /// Both places are read because they are both real: the folder is what Dataverse exports
    /// and the inline element is what a hand assembled file carries.
    /// </remarks>
    private static int ReadEnvironmentVariables(
        ZipArchive archive,
        XElement root,
        SolutionHeader solution,
        List<DiscoveredComponent> components)
    {
        var count = 0;

        var definitions = root.Descendants(None + "environmentvariabledefinition")
            .Concat(archive.Entries
                .Where(entry => entry.FullName.StartsWith("environmentvariabledefinitions/", StringComparison.OrdinalIgnoreCase)
                    && entry.Name.Equals("environmentvariabledefinition.xml", StringComparison.OrdinalIgnoreCase))
                .Select(Load));

        foreach (var definition in definitions)
        {
            var schemaName = definition.Attribute("schemaname")?.Value ?? Value(definition, "schemaname");
            if (string.IsNullOrWhiteSpace(schemaName)) continue;

            var displayName = definition.Element(None + "displayname")?.Attribute("default")?.Value
                ?? Value(definition, "displayname");

            Add(components, "environmentVariable", Value(definition, "environmentvariabledefinitionid"),
                string.IsNullOrWhiteSpace(displayName) ? schemaName : displayName, schemaName, solution, new Dictionary<string, object?>
                {
                    ["type"] = Value(definition, "type"),
                    ["hasDefaultValue"] = !string.IsNullOrWhiteSpace(Value(definition, "defaultvalue")),
                    ["defaultValue"] = Value(definition, "defaultvalue"),
                    ["isSecret"] = Value(definition, "type") == "100000005",
                    ["description"] = definition.Element(None + "description")?.Attribute("default")?.Value
                        ?? Value(definition, "description")
                });

            count++;
        }

        return count;
    }

    /// <summary>
    /// Custom APIs, one file per API under customapis.
    /// </summary>
    /// <remarks>
    /// A declared component type with no reader until a real export was run at it: nineteen of
    /// them were in the first solution this read and none appeared, in the findings or in the
    /// list of things that could not be read. They count toward the ratio and they are pro
    /// code, so an estate full of them reported as more low code than it is.
    ///
    /// The request parameters and response properties beside each one are deliberately not
    /// components. They are the API's signature rather than things anybody maintains
    /// separately, and counting two hundred of them would bury the nineteen that matter.
    /// </remarks>
    private static int ReadCustomApis(ZipArchive archive, SolutionHeader solution, List<DiscoveredComponent> components)
    {
        var count = 0;

        var files = archive.Entries
            .Where(entry => entry.FullName.StartsWith("customapis/", StringComparison.OrdinalIgnoreCase)
                && entry.Name.Equals("customapi.xml", StringComparison.OrdinalIgnoreCase));

        foreach (var file in files)
        {
            var api = Load(file);

            var uniqueName = api.Attribute("uniquename")?.Value ?? Value(api, "name");
            if (string.IsNullOrWhiteSpace(uniqueName)) continue;

            var displayName = api.Element(None + "displayname")?.Attribute("default")?.Value ?? uniqueName;

            // A custom API with no plugin behind it is a contract nothing implements. It is
            // worth telling apart from one that does, so the rules can say which.
            var pluginType = api.Element(None + "plugintypeid")?
                .Element(None + "plugintypeexportkey")?.Value;

            Add(components, "customApi", null, displayName, uniqueName, solution, new Dictionary<string, object?>
            {
                ["bindingType"] = Value(api, "bindingtype"),
                ["isPrivate"] = Value(api, "isprivate") == "1",
                ["isFunction"] = Value(api, "isfunction") == "1",
                ["allowedCustomProcessingStepType"] = Value(api, "allowedcustomprocessingsteptype"),
                ["hasPluginImplementation"] = !string.IsNullOrWhiteSpace(pluginType),
                ["description"] = api.Element(None + "description")?.Attribute("default")?.Value
            });

            count++;
        }

        return count;
    }

    private static int ReadRoles(XElement root, SolutionHeader solution, List<DiscoveredComponent> components)
    {
        var count = 0;

        foreach (var role in root.Descendants(None + "Role"))
        {
            var name = role.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(name)) continue;

            var privileges = role.Descendants(None + "RolePrivilege").ToList();

            Add(components, "securityRole", role.Attribute("roleid")?.Value, name, name, solution, new Dictionary<string, object?>
            {
                ["privilegeCount"] = privileges.Count,
                // Level 4 is organisation. A role holding organisation level write is the
                // evidence behind the privilege rule, and it is visible without a connection.
                ["hasOrganisationLevelWrite"] = privileges.Any(privilege =>
                    privilege.Attribute("level")?.Value == "Global" &&
                    (privilege.Attribute("name")?.Value?.StartsWith("prvWrite", StringComparison.OrdinalIgnoreCase) == true ||
                     privilege.Attribute("name")?.Value?.StartsWith("prvDelete", StringComparison.OrdinalIgnoreCase) == true)),
                ["description"] = Value(role, "Description")
            });

            count++;
        }

        return count;
    }

    private static int ReadPluginAssemblies(XElement root, SolutionHeader solution, List<DiscoveredComponent> components)
    {
        var count = 0;

        foreach (var assembly in root.Descendants(None + "PluginAssembly"))
        {
            var name = assembly.Attribute("FullName")?.Value ?? Value(assembly, "Name");
            if (string.IsNullOrWhiteSpace(name)) continue;

            Add(components, "pluginAssembly", assembly.Attribute("PluginAssemblyId")?.Value, name, name, solution, new Dictionary<string, object?>
            {
                ["isolationMode"] = Value(assembly, "IsolationMode"),
                ["sourceType"] = Value(assembly, "SourceType"),
                ["version"] = Value(assembly, "Version"),
                ["typeCount"] = assembly.Descendants(None + "PluginType").Count(),
                ["description"] = Value(assembly, "Description")
            });

            count++;
        }

        return count;
    }

    private static int ReadCanvasApps(XElement root, SolutionHeader solution, List<DiscoveredComponent> components)
    {
        var count = 0;

        foreach (var app in root.Descendants(None + "CanvasApp"))
        {
            var name = Value(app, "Name") ?? Value(app, "DisplayName");
            if (string.IsNullOrWhiteSpace(name)) continue;

            // The app itself is an .msapp inside the archive, and this reader does not open
            // one. Screen and formula counts come from the checker or from a live connection,
            // and claiming them from the manifest would be inventing them.
            Add(components, "canvasApp", Value(app, "CanvasAppId"), Value(app, "DisplayName") ?? name, name, solution,
                new Dictionary<string, object?>
                {
                    ["description"] = Value(app, "Description"),
                    ["appVersion"] = Value(app, "AppVersion")
                });

            count++;
        }

        return count;
    }

    /// <summary>
    /// Code components, from the control manifest each one ships with.
    /// </summary>
    /// <remarks>
    /// Nothing read these, which is how a solution containing exactly one PCF control came
    /// back as an estate with nothing in it. The manifest is the only place the version and
    /// the API version live, and both matter: a control built against an old API version is
    /// the kind of thing that stops working on a platform update nobody scheduled.
    ///
    /// Third party is inferred from the namespace rather than declared. A control whose
    /// namespace is not the solution's publisher prefix came from somewhere else, and
    /// somebody else's control is somebody else's to fix.
    /// </remarks>
    /// <param name="archive">The solution file.</param>
    /// <param name="root">customizations.xml.</param>
    /// <param name="solution">Which solution.</param>
    /// <param name="components">Where to put them.</param>
    private static int ReadCustomControls(
        ZipArchive archive,
        XElement root,
        SolutionHeader solution,
        List<DiscoveredComponent> components)
    {
        var count = 0;

        foreach (var control in root.Descendants(None + "CustomControl"))
        {
            var name = Value(control, "Name");
            if (string.IsNullOrWhiteSpace(name)) continue;

            var attributes = new Dictionary<string, object?>
            {
                ["isThirdParty"] = solution.PublisherPrefix is { Length: > 0 } prefix
                    && !name.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase)
            };

            // The manifest carries the version and what the control is allowed to reach. It
            // sits beside the bundle rather than in customizations.xml, and a control whose
            // manifest is missing is still a control.
            var manifestPath = Value(control, "FileName")?.TrimStart('/');

            if (manifestPath is not null && archive.GetEntry(manifestPath) is { } entry)
            {
                var manifest = Load(entry).Descendants(None + "control").FirstOrDefault();

                if (manifest is not null)
                {
                    attributes["version"] = manifest.Attribute("version")?.Value;
                    attributes["manifestVersion"] = manifest.Attribute("api-version")?.Value;
                    attributes["description"] = manifest.Attribute("description-key")?.Value;

                    // Declared in the manifest, which is the only place it is declared. A
                    // control reaching the Web API is doing more than rendering a field.
                    attributes["usesWebApi"] = manifest.Descendants(None + "uses-feature")
                        .Any(feature => feature.Attribute("name")?.Value == "WebAPI");

                    attributes["usesReactPlatformLibrary"] = manifest.Descendants(None + "platform-library")
                        .Any(library => library.Attribute("name")?.Value == "React");

                    attributes["controlType"] = manifest.Attribute("control-type")?.Value;

                    // Which pac built it, if it says. Not judged against a current version,
                    // because "current" moves and a rule that bakes today's number in starts
                    // lying the month after it ships. Carried into the inventory so somebody
                    // reading the report can see a control last built two years ago sitting
                    // next to one built last week, and draw their own conclusion.
                    attributes["builtBy"] = manifest.Descendants(None + "built-by")
                        .FirstOrDefault() is { } builtBy
                            ? $"{builtBy.Attribute("name")?.Value} {builtBy.Attribute("version")?.Value}".Trim()
                            : null;

                    attributes["declaresExternalService"] = manifest.Descendants(None + "external-service-usage")
                        .FirstOrDefault()?.Attribute("enabled")?.Value == "true";

                    attributes["resourceCount"] = manifest.Descendants(None + "resources")
                        .FirstOrDefault()?.Elements().Count() ?? 0;

                    // The bundle, which is the only place the rest of the answer lives. A
                    // manifest says what a control is allowed to do; the bundle says what it
                    // costs to load and how it was built.
                    ReadBundle(archive, manifestPath, manifest, attributes);
                }
            }

            Add(components, "pcfControl", null, name, name, solution, attributes);

            count++;
        }

        return count;
    }

    /// <summary>
    /// What a code component's bundle says about how it was built and what it costs.
    /// </summary>
    /// <remarks>
    /// Everything here is read off the file rather than judged. Size is size. Minified is a
    /// question about line lengths. React bundled inside a standard control is a string
    /// search for markers no minifier renames, because they are in string literals that
    /// React itself reads at run time.
    ///
    /// Nothing tries to measure complexity from minified code. A cyclomatic count over a
    /// webpack bundle measures the bundler, and a number that looks like an opinion about
    /// somebody's code had better not be one.
    ///
    /// The content is kept so the rules that look for hard coded endpoints and secrets can
    /// see it, which is the same treatment a web resource gets. Only the first stretch of
    /// it: the patterns are near the top in practice, and holding a two megabyte bundle in
    /// memory for every control in an estate is how a run of a large solution runs out of it.
    /// </remarks>
    /// <param name="archive">The solution file.</param>
    /// <param name="manifestPath">Where the manifest was, which is where the bundle sits beside it.</param>
    /// <param name="manifest">The parsed manifest, for the resource paths it declares.</param>
    /// <param name="attributes">What to fill in.</param>
    private static void ReadBundle(
        ZipArchive archive,
        string manifestPath,
        XElement manifest,
        Dictionary<string, object?> attributes)
    {
        var folder = manifestPath.Contains('/', StringComparison.Ordinal)
            ? manifestPath[..manifestPath.LastIndexOf('/')]
            : string.Empty;

        var total = 0L;
        string? code = null;

        foreach (var resource in manifest.Descendants(None + "code"))
        {
            var path = resource.Attribute("path")?.Value;
            if (string.IsNullOrWhiteSpace(path)) continue;

            var entry = archive.GetEntry($"{folder}/{path}".TrimStart('/'));
            if (entry is null) continue;

            total += entry.Length;

            // The first bundle only. A control with several is unusual and the first is the
            // one that loads.
            code ??= ReadText(entry, 200_000);
        }

        attributes["sizeBytes"] = total;

        if (code is null) return;

        attributes["content"] = code;

        // Minified output is one enormous line. Source is not. Averaged rather than taking
        // the longest line, because a single long string literal in otherwise readable code
        // would make source look minified.
        var lines = code.Split('\n');
        var average = lines.Length == 0 ? 0 : code.Length / lines.Length;

        attributes["isMinified"] = average > 200;

        // Markers React reads at run time, so a minifier leaves them alone. Checked only
        // where the manifest has not declared the platform library, because a virtual
        // control legitimately references React without shipping it.
        var declared = attributes.TryGetValue("usesReactPlatformLibrary", out var platform) && platform is true;

        attributes["bundlesOwnReact"] = !declared
            && (code.Contains("react.development", StringComparison.Ordinal)
                || code.Contains("react.production.min", StringComparison.Ordinal)
                || code.Contains("__SECRET_INTERNALS_DO_NOT_USE_OR_YOU_WILL_BE_FIRED", StringComparison.Ordinal));

        attributes["hasDebugCode"] =
            Regex.IsMatch(code, @"(^|[;{}\s])debugger\s*[;}]")
            || code.Contains("console.log(", StringComparison.Ordinal)
            || code.Contains("sourceMappingURL", StringComparison.Ordinal);

        // What the control does once it is running, which is the half of the question the
        // manifest cannot answer.
        //
        // These three survive minification and the rest of the bundle does not. A minifier
        // renames everything it owns and nothing it does not: setInterval is a browser
        // global, innerHTML is a DOM property, and webAPI is a property of an object the
        // platform hands in. All three are looked up by name at run time, so the name in the
        // bundle is the name in the source, and counting them is counting rather than
        // guessing. Anything the developer named themselves is now a single letter, which is
        // exactly why nothing here tries to say whether the code is well written.
        attributes["timerCount"] = Regex.Count(code, @"\bsetInterval\s*\(");

        attributes["innerHtmlCount"] = Regex.Count(code, @"\.innerHTML\s*=[^=]");

        attributes["callsWebApi"] = Regex.IsMatch(code, @"\.webAPI\b");
    }

    /// <summary>The first stretch of a file, as text.</summary>
    /// <param name="entry">The file.</param>
    /// <param name="limit">How much of it to take.</param>
    private static string ReadText(ZipArchiveEntry entry, int limit)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);

        var buffer = new char[limit];
        var read = reader.ReadBlock(buffer, 0, limit);

        return new string(buffer, 0, read);
    }

    /// <summary>
    /// The model driven apps in the solution, and their sitemaps.
    /// </summary>
    /// <remarks>
    /// An app is what a user actually opens, so a report that lists four hundred components
    /// and no apps has described a database rather than a system. Read from the manifest
    /// because the offline mode has nothing else; the component count and the publish date
    /// need a live connection and are not guessed here.
    /// </remarks>
    /// <param name="root">customizations.xml.</param>
    /// <param name="solution">Which solution.</param>
    /// <param name="components">Where to put them.</param>
    private static int ReadApps(XElement root, SolutionHeader solution, List<DiscoveredComponent> components)
    {
        var count = 0;

        foreach (var app in root.Descendants(None + "AppModule"))
        {
            var name = Value(app, "UniqueName") ?? Value(app, "Name");
            if (string.IsNullOrWhiteSpace(name)) continue;

            Add(components, "modelDrivenApp", Value(app, "AppModuleId"),
                Value(app, "Name") ?? name, name, solution,
                new Dictionary<string, object?>
                {
                    ["description"] = Value(app, "Description"),

                    // Lower case, as the export writes it. Reading "Statecode" found
                    // nothing and every app looked stateless.
                    ["statecode"] = Value(app, "statecode")
                });

            count++;
        }

        // AppModuleSiteMap rather than the SiteMap element nested inside it. The unique name
        // is on the wrapper, so reading the inner element found a node with no name on it and
        // skipped every sitemap in the file.
        foreach (var map in root.Descendants(None + "AppModuleSiteMap"))
        {
            var name = Value(map, "SiteMapUniqueName");
            if (string.IsNullOrWhiteSpace(name)) continue;

            Add(components, "siteMap", null, name, name, solution, new Dictionary<string, object?>());
            count++;
        }

        return count;
    }

    private static int ReadGlobalChoices(XElement root, SolutionHeader solution, List<DiscoveredComponent> components)
    {
        var count = 0;

        foreach (var optionSet in root.Descendants(None + "optionset"))
        {
            var name = optionSet.Attribute("Name")?.Value;
            if (string.IsNullOrWhiteSpace(name)) continue;

            Add(components, "choice", optionSet.Attribute("OptionSetId")?.Value, name, name, solution, new Dictionary<string, object?>
            {
                ["isGlobal"] = true,
                ["optionCount"] = optionSet.Descendants(None + "option").Count()
            });

            count++;
        }

        return count;
    }

    private static string Add(
        List<DiscoveredComponent> components,
        string typeId,
        string? platformId,
        string displayName,
        string schemaName,
        SolutionHeader solution,
        Dictionary<string, object?> attributes)
    {
        var key = StableKeys.ForComponent(typeId, platformId, schemaName);

        components.Add(new DiscoveredComponent(
            Guid.NewGuid(),
            key,
            typeId,
            displayName,
            schemaName,
            platformId,
            solution.UniqueName,
            solution.IsManaged,
            null,
            attributes));

        return key;
    }
}
