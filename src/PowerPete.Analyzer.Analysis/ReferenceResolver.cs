namespace PowerPete.Analyzer.Analysis;

using PowerPete.Analyzer.Domain;

/// <summary>
/// Turns a list of components into a graph.
/// </summary>
/// <remarks>
/// Ten of the rules in the catalogue are unanswerable without this. "A column on no form and
/// no view" is a question about inbound edges, and so is "a script loaded by five forms" and
/// "a flow calling a child flow that is in no solution".
///
/// The extraction produces some links already, because a form's XML names the scripts it
/// loads and a workflow names its table. This stage produces the rest, and it is where a
/// reference that points at nothing becomes a recorded fact rather than a dropped row.
/// </remarks>
public sealed class ReferenceResolver
{
    /// <summary>What resolving produced.</summary>
    /// <param name="Links">Edges between components that exist.</param>
    /// <param name="Unresolved">Edges pointing outside the inventory.</param>
    public sealed record Result(IReadOnlyList<ComponentLink> Links, IReadOnlyList<UnresolvedLink> Unresolved);

    /// <summary>
    /// Resolves every reference this stage knows how to find.
    /// </summary>
    /// <param name="components">Everything in scope.</param>
    /// <param name="existing">Links the extraction already produced.</param>
    public static Result Resolve(IReadOnlyList<DiscoveredComponent> components, IReadOnlyList<ComponentLink> existing)
    {
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(existing);

        var byKey = components.ToDictionary(component => component.StableKey, StringComparer.Ordinal);
        var links = new List<ComponentLink>(existing);
        var unresolved = new List<UnresolvedLink>();

        // Every link the extraction produced was built by name, so some of them point at
        // components that were never extracted. Checking them here rather than trusting them
        // is what turns "this form loads a script" into "this form loads a script that is in
        // no solution", which is a finding.
        foreach (var link in existing.Where(link => !byKey.ContainsKey(link.ToKey)))
        {
            unresolved.Add(new UnresolvedLink(link.FromKey, link.Kind, link.ToKey));
        }

        links.RemoveAll(link => !byKey.ContainsKey(link.ToKey) || !byKey.ContainsKey(link.FromKey));

        ResolveViewColumns(components, byKey, links, unresolved);
        ResolveWorkflowTables(components, byKey, links, unresolved);
        ResolveFlowConnections(components, byKey, links, unresolved);
        ResolveActivityCalls(components, byKey, links);
        ResolveChoiceUsage(components, byKey, links);

        return new Result(
            [.. links.DistinctBy(link => (link.FromKey, link.ToKey, link.Kind))],
            [.. unresolved.DistinctBy(link => (link.FromKey, link.Kind, link.TargetDescription))]);
    }

    /// <summary>
    /// Which columns a view shows, so the orphaned column rule has something to count against.
    /// </summary>
    /// <remarks>
    /// Read from the fetch XML rather than from the layout, because the layout carries the
    /// visible grid and the fetch carries what the query actually selects. A column filtered
    /// on but not displayed is still in use, and reporting it as orphaned is how this rule
    /// loses a client's trust.
    /// </remarks>
    private static void ResolveViewColumns(
        IReadOnlyList<DiscoveredComponent> components,
        Dictionary<string, DiscoveredComponent> byKey,
        List<ComponentLink> links,
        List<UnresolvedLink> unresolved)
    {
        foreach (var view in components.Where(component => component.TypeId == "view"))
        {
            var table = view.Attribute<string>("table");
            var columns = view.Attribute<string>("columnNames");

            if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(columns)) continue;

            foreach (var column in columns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                Link(byKey, links, unresolved, view.StableKey, "column", $"{table}.{column}", "showsColumn");
            }
        }
    }

    private static void ResolveWorkflowTables(
        IReadOnlyList<DiscoveredComponent> components,
        Dictionary<string, DiscoveredComponent> byKey,
        List<ComponentLink> links,
        List<UnresolvedLink> unresolved)
    {
        var logicTypes = new[]
        {
            "classicWorkflowBackground", "classicWorkflowRealtime", "businessRule",
            "businessProcessFlow", "cloudFlow", "customProcessAction", "lowCodePlugin", "pluginStep"
        };

        foreach (var component in components.Where(component => logicTypes.Contains(component.TypeId, StringComparer.Ordinal)))
        {
            var table = component.Attribute<string>("primaryEntity");
            if (!string.IsNullOrWhiteSpace(table))
            {
                Link(byKey, links, unresolved, component.StableKey, "table", table, "runsOn");
            }

            // The filtering attribute list is where a plugin or a workflow says which columns
            // it reads. It is also the only evidence some columns have that anything uses them.
            var filtered = component.Attribute<string>("triggerOnUpdateAttributeList")
                ?? component.Attribute<string>("filteringAttributes");

            if (string.IsNullOrWhiteSpace(filtered) || string.IsNullOrWhiteSpace(table)) continue;

            foreach (var column in filtered.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                Link(byKey, links, unresolved, component.StableKey, "column", $"{table}.{column}", "filtersOn");
            }
        }
    }

    private static void ResolveFlowConnections(
        IReadOnlyList<DiscoveredComponent> components,
        Dictionary<string, DiscoveredComponent> byKey,
        List<ComponentLink> links,
        List<UnresolvedLink> unresolved)
    {
        foreach (var flow in components.Where(component => component.TypeId is "cloudFlow" or "canvasApp"))
        {
            var references = flow.Attribute<string>("connectionReferenceNames");
            if (string.IsNullOrWhiteSpace(references)) continue;

            foreach (var reference in references.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                Link(byKey, links, unresolved, flow.StableKey, "connectionReference", reference, "usesConnection");
            }
        }
    }

    private static void ResolveActivityCalls(
        IReadOnlyList<DiscoveredComponent> components,
        Dictionary<string, DiscoveredComponent> byKey,
        List<ComponentLink> links)
    {
        var activities = components
            .Where(component => component.TypeId == "customWorkflowActivity")
            .ToList();

        if (activities.Count == 0) return;

        // A workflow's XAML names the activity type it calls. The extraction records the raw
        // text rather than parsing it into a reference, because matching a .NET type name to
        // an activity component is a name comparison and belongs here rather than in a reader.
        foreach (var workflow in components.Where(component =>
            component.TypeId is "classicWorkflowBackground" or "classicWorkflowRealtime"))
        {
            var called = workflow.Attribute<string>("calledActivityTypes");
            if (string.IsNullOrWhiteSpace(called)) continue;

            foreach (var typeName in called.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var match = activities.FirstOrDefault(activity =>
                    activity.SchemaName?.EndsWith(typeName, StringComparison.OrdinalIgnoreCase) == true
                    || activity.DisplayName.Equals(typeName, StringComparison.OrdinalIgnoreCase));

                if (match is null) continue;

                links.Add(new ComponentLink(workflow.StableKey, match.StableKey, "callsActivity"));
                _ = byKey;
            }
        }
    }

    private static void ResolveChoiceUsage(
        IReadOnlyList<DiscoveredComponent> components,
        Dictionary<string, DiscoveredComponent> byKey,
        List<ComponentLink> links)
    {
        var globals = components
            .Where(component => component.TypeId == "choice" && component.Attribute<bool>("isGlobal"))
            .ToDictionary(component => component.SchemaName ?? component.DisplayName, StringComparer.OrdinalIgnoreCase);

        if (globals.Count == 0) return;

        foreach (var column in components.Where(component => component.TypeId == "column"))
        {
            var optionSet = column.Attribute<string>("optionSetName");
            if (string.IsNullOrWhiteSpace(optionSet)) continue;
            if (!globals.TryGetValue(optionSet, out var choice)) continue;

            links.Add(new ComponentLink(column.StableKey, choice.StableKey, "usesChoice"));
            _ = byKey;
        }
    }

    /// <summary>
    /// Records a link, or records that it could not be made.
    /// </summary>
    /// <remarks>
    /// The whole reason both lists exist. Dropping a reference to something outside the
    /// analysed solutions makes a report look tidier and makes it wrong: the difference
    /// between "this column is unused" and "this column is used by something we did not
    /// look at" is the difference between a deletion and an incident.
    /// </remarks>
    private static void Link(
        Dictionary<string, DiscoveredComponent> byKey,
        List<ComponentLink> links,
        List<UnresolvedLink> unresolved,
        string fromKey,
        string targetTypeId,
        string targetName,
        string kind)
    {
        var targetKey = StableKeys.ForComponent(targetTypeId, null, targetName);

        if (byKey.ContainsKey(targetKey))
        {
            links.Add(new ComponentLink(fromKey, targetKey, kind));
            return;
        }

        unresolved.Add(new UnresolvedLink(fromKey, kind, $"{targetTypeId} '{targetName}'"));
    }
}
