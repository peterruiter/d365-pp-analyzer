namespace PowerPete.Analyzer.Analysis.Handlers;

using PowerPete.Analyzer.Domain;

// ------------------------------------------------------------- reference graph --

/// <summary>
/// A custom column nothing appears to use.
/// </summary>
/// <remarks>
/// The highest volume rule in the product and the one to be most careful with. A column with
/// no interface reference is very often written by an integration this tool cannot see, so
/// the finding is phrased as a question and grouped per table rather than raised four hundred
/// times.
/// </remarks>
public sealed class OrphanedColumnHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "governance.orphanedColumn";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var orphans = context.OfType("column")
            .Where(column => !column.IsManaged)
            .Where(column => column.Attribute<bool?>("isCustom") is not false)
            .Where(column => context.Inbound(column.StableKey).Count(link => link.Kind != "belongsTo") == 0)
            .GroupBy(column => column.Attribute<string>("table") ?? "unknown", StringComparer.OrdinalIgnoreCase);

        foreach (var table in orphans)
        {
            var columns = table.ToList();
            if (columns.Count == 0) continue;

            yield return Fire.At(RuleId, null,
                ("table", table.Key),
                ("count", columns.Count),
                ("columns", string.Join(", ", columns.Take(25).Select(column => column.SchemaName ?? column.DisplayName))),
                ("checkedAgainst", "Forms, views, flow filters, plugin filtering attributes and calculated columns."),
                ("whatThisCannotSee", "Integrations. A column written only by an API has no reference anywhere in this list, and deleting one is how a nightly load starts failing."));
        }
    }
}

/// <summary>A form script doing only what a business rule does.</summary>
public sealed class ProCodeWhereConfigWouldDoHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "architecture.proCodeWhereConfigWouldDo";

    private static readonly string[] SimpleApis =
        ["setVisible", "setDisabled", "setRequiredLevel", "getValue", "getAttribute", "getControl", "setValue"];

    private static readonly string[] ComplexSignals =
        ["Xrm.WebApi", "fetch(", "XMLHttpRequest", "getGrid", "addOnLoad", "Xrm.Navigation", "openForm", "getControlType"];

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var script in context.OfType("jsWebResource").Where(script => !script.IsManaged))
        {
            var content = script.Attribute<string>("content");

            // Never fires on a script nobody read. A recommendation to replace code the tool
            // could not open is a recommendation a developer will disprove in thirty seconds.
            if (string.IsNullOrWhiteSpace(content)) continue;
            if (ComplexSignals.Any(signal => content.Contains(signal, StringComparison.Ordinal))) continue;

            var used = SimpleApis.Where(api => content.Contains(api, StringComparison.Ordinal)).ToList();
            if (used.Count == 0) continue;

            yield return Fire.At(RuleId, script,
                ("apisUsed", string.Join(", ", used)),
                ("sizeBytes", script.Attribute<long?>("sizeBytes")),
                ("loadedByForms", context.Inbound(script.StableKey, "loadsScript").Count),
                ("limit", "Business rules cannot touch a subgrid, a tab or a quick view form. Read the script before agreeing."));
        }
    }
}

/// <summary>Inconsistent naming within one solution.</summary>
public sealed class NamingInconsistentHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "quality.namingInconsistent";

    private static readonly string[] Types = ["table", "column", "cloudFlow", "securityRole"];

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var typeId in Types)
        {
            var names = context.OfType(typeId)
                .Where(component => !component.IsManaged)
                .Select(component => component.DisplayName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();

            if (names.Count < 5) continue;

            var conventions = names.Select(Convention).Distinct(StringComparer.Ordinal).ToList();
            if (conventions.Count <= 2) continue;

            yield return Fire.At(RuleId, null,
                ("componentType", typeId),
                ("conventions", string.Join(", ", conventions)),
                ("count", conventions.Count),
                ("sampled", names.Count),
                ("note", "A proxy for how many people have built here without a standard."));
        }
    }

    private static string Convention(string name)
    {
        if (name.Contains('_', StringComparison.Ordinal)) return "snake_case";
        if (name.Contains('-', StringComparison.Ordinal)) return "kebab-case";
        if (name.Contains(' ', StringComparison.Ordinal)) return "Title Case";
        if (name.Length > 1 && char.IsLower(name[0]) && name.Any(char.IsUpper)) return "camelCase";
        return "PascalCase";
    }
}

/// <summary>Logic outside the platform with no documented owner.</summary>
public sealed class ExternalLogicHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "architecture.externalLogicInvisible";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var external = context.OfType("serviceEndpoint").Concat(context.OfType("customConnector"));

        foreach (var component in external.Where(component => !component.IsManaged))
        {
            var url = component.Attribute<string>("url") ?? component.Attribute<string>("host");

            yield return Fire.At(RuleId, component,
                ("url", url),
                ("contract", component.Attribute<string>("contract")),
                ("authType", component.Attribute<string>("authType") ?? component.Attribute<string>("authenticationType")),
                ("limit", "This report cannot tell you what is behind the URL. It can tell you that something is."));
        }
    }
}

/// <summary>A dataflow, which travels in nothing and is visible to almost no governance tooling.</summary>
public sealed class DataflowPresentHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "lifecycle.dataflowPresent";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.OfType("dataflow").Select(dataflow => Fire.At(RuleId, dataflow,
            ("owner", dataflow.OwnerUpn),
            ("refreshSchedule", dataflow.Attribute<string>("refreshSchedule")),
            ("note", "No CLI support and no catalogue API, so it does not travel through an ALM pipeline and does not appear in most governance tooling."),
            ("capacity", "Capacity consumption surfaces as a bill rather than as an alert.")));
    }
}

// ----------------------------------------------------------------- metadata --

/// <summary>A custom security role assigned to nobody.</summary>
public sealed class SecurityRoleSprawlHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "governance.securityRoleSprawl";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var role in context.OfType("securityRole").Where(role => !role.IsManaged))
        {
            var users = role.Attribute<int?>("userCount");
            var teams = role.Attribute<int?>("teamCount");

            // Both unknown means nobody read the assignments, and a role reported as unassigned
            // on that basis is a deletion recommendation built on nothing.
            if (users is null && teams is null) continue;
            if ((users ?? 0) + (teams ?? 0) > 0) continue;

            yield return Fire.At(RuleId, role,
                ("users", users),
                ("teams", teams),
                ("beforeDeleting", "Check whether a provisioning process assigns it on a schedule. A role assigned monthly looks unassigned for twenty-nine days out of thirty."));
        }
    }
}

/// <summary>A flow or app owned by somebody who has left.</summary>
public sealed class OrphanedByOwnerHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "governance.orphanedByOwner";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var types = new[] { "cloudFlow", "canvasApp", "desktopFlow" };

        foreach (var component in context.Components.Where(component => types.Contains(component.TypeId, StringComparer.Ordinal)))
        {
            var state = component.Attribute<string>("ownerState");
            if (state is not ("disabled" or "absent")) continue;

            yield return Fire.At(RuleId, component,
                ("owner", component.OwnerUpn),
                ("ownerState", state),
                ("consequence", "It runs on their connections until those expire, and then it stops, and nobody knows why."),
                ("note", "Reassigning to another person recreates this with a later date on it."));
        }
    }
}

/// <summary>Custom components whose only home is the default solution.</summary>
public sealed class DefaultSolutionComponentsHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "alm.defaultSolutionComponents";

    private static readonly string[] DefaultNames = ["Default", "Common Data Services Default Solution", "Active"];

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var stranded = context.Components
            .Where(component => !component.IsManaged)
            .Where(component => component.SolutionUniqueName is { } name
                && DefaultNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            .GroupBy(component => component.TypeId, StringComparer.Ordinal)
            .ToList();

        if (stranded.Count == 0) yield break;

        yield return Fire.At(RuleId, null,
            ("count", stranded.Sum(group => group.Count())),
            ("byType", string.Join(", ", stranded.Select(group => $"{group.Key}: {group.Count()}"))),
            ("consequence", "They do not travel. Somebody finds out when the next deployment is missing a column."));
    }
}

/// <summary>Unmanaged customisation sitting in production.</summary>
public sealed class UnmanagedInProductionHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "alm.unmanagedInProduction";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Only against a connection somebody marked as production. Guessing from an
        // environment name would produce a critical finding on half the sandboxes in the
        // world, and a report whose top finding is wrong gets put down.
        if (!context.IsProduction) yield break;

        var unmanaged = context.Components.Count(component => !component.IsManaged);
        if (unmanaged == 0) yield break;

        yield return Fire.At(RuleId, null,
            ("unmanagedComponents", unmanaged),
            ("environmentRole", context.EnvironmentRole),
            ("meaning", "There is no pipeline. Whatever is here was made here, the environment is the source of truth, and there is no rollback and no review."),
            ("precedence", "This is a delivery finding, not a component one. It outranks everything else in the report."));
    }
}

/// <summary>The same solution unmanaged in several environments, with diverging versions.</summary>
public sealed class NoEnvironmentSeparationHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "operability.noEnvironmentSeparation";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Needs more than one environment to say anything at all. With one connection this
        // rule has no evidence, and reporting it as passing would be a lie of omission.
        if (context.EnvironmentCount < 2) yield break;

        var diverging = context.OfType("solution")
            .Where(solution => !solution.IsManaged)
            .GroupBy(solution => solution.SchemaName ?? solution.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1
                && group.Select(solution => solution.Attribute<string>("version")).Distinct(StringComparer.Ordinal).Count() > 1)
            .ToList();

        foreach (var solution in diverging)
        {
            yield return Fire.At(RuleId, null,
                ("solution", solution.Key),
                ("environments", solution.Count()),
                ("versions", string.Join(", ", solution.Select(entry => entry.Attribute<string>("version")).Distinct(StringComparer.Ordinal))),
                ("meaning", "There is no promotion path, there is copying. The versions have already diverged."),
                ("firstStep", "Produce a written comparison before picking a source. Picking one without knowing what the others have is how a fortnight of somebody's work disappears."));
        }
    }
}

/// <summary>Nothing anywhere in the solution reports a failure to a person.</summary>
public sealed class NoFailureAlertingHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "operability.noFailureAlerting";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var flowsWithHandling = context.OfType("cloudFlow").Count(flow => flow.Attribute<bool>("hasErrorHandling"));
        var endpoints = context.OfType("serviceEndpoint").Count;
        var insights = context.Components.Any(component => component.Attribute<bool?>("applicationInsightsConfigured") == true);

        if (flowsWithHandling > 0 || endpoints > 0 || insights) yield break;

        // A solution with no logic at all is not missing alerting. It is just a data model.
        var logic = context.OfType("cloudFlow").Count + context.OfType("pluginAssembly").Count
            + context.OfType("classicWorkflowBackground").Count;
        if (logic == 0) yield break;

        yield return Fire.At(RuleId, null,
            ("logicComponents", logic),
            ("flowsWithFailurePaths", 0),
            ("serviceEndpoints", 0),
            ("applicationInsights", false),
            ("consequence", "Every other operability finding in this report assumes somebody finds out when something breaks. Nothing here does that."));
    }
}

/// <summary>An update step registered with no filtering attributes.</summary>
public sealed class StepNoFilteringAttributesHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "performance.stepNoFilteringAttributes";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var step in context.OfType("pluginStep").Where(step => !step.IsManaged))
        {
            if (step.Attribute<string>("message") is not "Update") continue;
            if (!string.IsNullOrWhiteSpace(step.Attribute<string>("filteringAttributes"))) continue;

            yield return Fire.At(RuleId, step,
                ("table", step.Attribute<string>("primaryEntity")),
                ("stage", step.Attribute<string>("stage")),
                ("mode", step.Attribute<string>("mode")),
                ("consequence", "It runs on every change to every column on that table, including the ones written by other plugins. That is also how recursion starts."));
        }
    }
}

/// <summary>A default view joining or selecting too much.</summary>
public sealed class ViewComplexityHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "performance.viewComplexity";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var view in context.OfType("view"))
        {
            if (!view.Attribute<bool>("isDefault")) continue;

            var columns = view.Attribute<int?>("columnCount") ?? 0;
            var linked = view.Attribute<int?>("linkedEntityCount") ?? 0;

            if (columns <= 20 && linked <= 5) continue;

            yield return Fire.At(RuleId, view,
                ("table", view.Attribute<string>("table")),
                ("columns", columns),
                ("linkedEntities", linked),
                ("consequence", "It is the first thing every user of that table loads."));
        }
    }
}

// ------------------------------------------------------------------ runtime --

/// <summary>A classic workflow that is activated and never runs.</summary>
public sealed class ClassicWorkflowDormantHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "lifecycle.classicWorkflowDormant";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var workflows = context.OfType("classicWorkflowBackground").Concat(context.OfType("classicWorkflowRealtime"));

        foreach (var workflow in workflows)
        {
            if (workflow.Attribute<string>("statecode") is not "1" and not "Activated") continue;

            var executions = workflow.Attribute<int?>("executionCount90d");
            if (executions is not 0) continue;

            yield return Fire.At(RuleId, workflow,
                ("table", workflow.Attribute<string>("primaryEntity")),
                ("executions90d", 0),
                ("lastExecuted", workflow.Attribute<string>("lastExecutedUtc")),
                ("theQuestion", "Either it is dead and should go, or it should have run and did not. The second is worse and this rule cannot tell you which."));
        }
    }
}

/// <summary>A cloud flow failing regularly.</summary>
public sealed class FlowFailureRateHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "quality.flowFailureRate";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var flow in context.OfType("cloudFlow"))
        {
            var runs = flow.Attribute<int?>("runCount30d");
            var rate = flow.Attribute<decimal?>("failureRate30d");

            // Twenty runs minimum. Two failures out of three is a hundred percent alarming
            // and statistically nothing.
            if (runs is null or < 20 || rate is null or <= 0.10m) continue;

            yield return Fire.At(RuleId, flow,
                ("runs30d", runs),
                ("failureRate", $"{rate:P0}"),
                ("lastFailure", flow.Attribute<string>("lastFailureMessage")),
                ("beforeEstimating", "Read the last failure. A flow failing on a data condition is a different job from one failing on an expired connection."));
        }
    }
}

/// <summary>A synchronous plugin running long enough for a user to feel it.</summary>
public sealed class SyncPluginSlowHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "performance.plugSyncSlow";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var step in context.OfType("pluginStep"))
        {
            if (step.Attribute<string>("mode") is not "0" and not "Synchronous") continue;

            var average = step.Attribute<int?>("averageExecutionMs");
            if (average is null or < 2000) continue;

            yield return Fire.At(RuleId, step,
                ("table", step.Attribute<string>("primaryEntity")),
                ("message", step.Attribute<string>("message")),
                ("averageMs", average),
                ("executions30d", step.Attribute<int?>("executionCount30d")),
                ("consequence", "It is inside the user's save. Everything above two seconds is felt."));
        }
    }
}

// ------------------------------------------------------------------ checker --

/// <summary>
/// A base for the rules whose detection belongs to the Power Apps checker.
/// </summary>
/// <remarks>
/// These handlers do no analysis. They group what the checker returned under a rule in this
/// catalogue so the finding gets an estimate, an acceptance criterion and a place in the
/// backlog, while the detection itself stays with Microsoft, who keep it current.
/// </remarks>
public abstract class CheckerBackedHandler : IRuleHandler
{
    /// <inheritdoc />
    public abstract string RuleId { get; }

    /// <summary>The checker rule identifiers, or prefixes of them, this rule covers.</summary>
    protected abstract IReadOnlyList<string> CheckerRules { get; }

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var rule = RuleCatalogue.Find(RuleId)
            ?? throw new InvalidOperationException($"Rule '{RuleId}' is not in the catalogue.");

        var issues = CheckerRules
            .SelectMany(context.Checker)
            .GroupBy(issue => issue.ComponentKey ?? issue.FilePath ?? "solution", StringComparer.Ordinal);

        foreach (var group in issues)
        {
            var component = context.ByKey(group.First().ComponentKey);

            // A finding against a managed component is somebody else's to fix. Reported so it
            // is not a surprise, and never estimated, because estimating work on a solution
            // you do not ship is inventing a number.
            var managed = component?.IsManaged == true;

            yield return Finding.From(rule, component, new Dictionary<string, object?>
            {
                ["file"] = group.First().FilePath,
                ["issues"] = group.Count(),
                ["checkerRules"] = string.Join(", ", group.Select(issue => issue.CheckerRuleId).Distinct(StringComparer.Ordinal)),
                ["firstMessage"] = group.First().Message,
                ["line"] = group.First().Line,
                ["managed"] = managed,
                ["ownership"] = managed
                    ? "This component came in a managed solution. Raise it with whoever ships it rather than estimating a fix."
                    : null
            });
        }
    }
}

/// <summary>Deprecated client API use, as the checker reports it.</summary>
public sealed class DeprecatedClientApiHandler : CheckerBackedHandler
{
    /// <inheritdoc />
    public override string RuleId => "lifecycle.deprecatedClientApi";

    /// <inheritdoc />
    protected override IReadOnlyList<string> CheckerRules =>
        ["web-avoid-crm2011-service-odata", "web-avoid-crm2011-service-soap", "web-use-client-api",
         "web-avoid-2011-api", "web-remove-debug-script", "web-unsupported-syntax"];
}

/// <summary>Canvas app delegation warnings, as the checker reports them.</summary>
public sealed class CanvasDelegationHandler : CheckerBackedHandler
{
    /// <inheritdoc />
    public override string RuleId => "quality.canvasDelegationWarnings";

    /// <inheritdoc />
    protected override IReadOnlyList<string> CheckerRules => ["app-formula-issues-high", "app-delegation"];
}

/// <summary>Plugins with no tracing, as the checker reports them.</summary>
public sealed class PluginNoTracingHandler : CheckerBackedHandler
{
    /// <inheritdoc />
    public override string RuleId => "quality.pluginNoTracing";

    /// <inheritdoc />
    protected override IReadOnlyList<string> CheckerRules => ["il-avoid-crm2011-depreciated-methods", "il-use-tracing"];
}

// ------------------------------------------------------------ informational --

/// <summary>The low code ratio, carried as a finding so it travels with everything else.</summary>
public sealed class LowCodeRatioHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "architecture.lowCodeRatio";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var counted = context.Components
            .Where(component => component.Type?.CountsTowardRatio == true)
            .ToList();

        if (counted.Count == 0) yield break;

        var lowCode = counted.Count(component => component.Type!.Craft == Craft.LowCode);
        var proCode = counted.Count(component => component.Type!.Craft == Craft.ProCode);
        var external = counted.Count(component => component.Type!.Craft == Craft.External);

        yield return Fire.At(RuleId, null,
            ("lowCode", lowCode),
            ("proCode", proCode),
            ("external", external),
            ("counted", counted.Count),
            ("share", $"{(decimal)lowCode / counted.Count:P0}"),
            ("definition", "Configuration and content are counted separately and are not in this ratio."),
            ("externalIsAFloor", "External logic is only visible through connection references, service endpoints and custom connectors. The real figure is higher."));
    }
}

/// <summary>Premium connector use, reported as a licensing question rather than a technical one.</summary>
public sealed class PremiumConnectorHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "governance.premiumConnectorExposure";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var premium = context.OfType("connectionReference")
            .Where(reference => reference.Attribute<bool?>("isPremium") == true)
            .ToList();

        if (premium.Count == 0) yield break;

        yield return Fire.At(RuleId, null,
            ("count", premium.Count),
            ("connectors", string.Join(", ", premium.Select(reference => reference.Attribute<string>("connectorId")).Distinct(StringComparer.Ordinal))),
            ("usedBy", premium.Sum(reference => context.Inbound(reference.StableKey, "usesConnection").Count)),
            ("limit", "This product cannot see what licences the tenant holds and does not guess. Check the finding against the licences in place before drawing a conclusion."));
    }
}
