namespace PowerPete.Analyzer.Analysis.Handlers;

using System.Text.RegularExpressions;
using PowerPete.Analyzer.Domain;

/// <summary>Shorthand for the handlers below.</summary>
internal static class Fire
{
    /// <summary>Builds a finding for a rule that is known to exist.</summary>
    /// <param name="ruleId">The rule.</param>
    /// <param name="component">What it fired against.</param>
    /// <param name="evidence">What triggered it.</param>
    public static Finding At(string ruleId, DiscoveredComponent? component, params (string Key, object? Value)[] evidence)
    {
        var rule = RuleCatalogue.Find(ruleId)
            ?? throw new InvalidOperationException($"Rule '{ruleId}' is not in the catalogue.");

        return Finding.From(rule, component, evidence.ToDictionary(pair => pair.Key, pair => pair.Value));
    }

    /// <summary>
    /// Builds a finding about a scope rather than a component.
    /// </summary>
    /// <remarks>
    /// For the rules that fire once per solution, per table or per component type. Without a
    /// scope every one of those findings would carry the same stable key and the second one
    /// written would violate the unique constraint on the run, taking the whole run with it.
    /// </remarks>
    /// <param name="ruleId">The rule.</param>
    /// <param name="scope">What this one is about: a solution's unique name, a table, a component type.</param>
    /// <param name="evidence">What triggered it.</param>
    public static Finding About(string ruleId, string scope, params (string Key, object? Value)[] evidence)
    {
        var rule = RuleCatalogue.Find(ruleId)
            ?? throw new InvalidOperationException($"Rule '{ruleId}' is not in the catalogue.");

        return Finding.From(rule, null, evidence.ToDictionary(pair => pair.Key, pair => pair.Value), scope);
    }

    /// <summary>
    /// Builds a finding against a component where one was found, and against a scope where
    /// the component could not be resolved.
    /// </summary>
    /// <remarks>
    /// For a rule that names a component it looked up rather than iterated. The lookup can
    /// miss: a rule can count logic sitting on a table that is itself outside the solutions
    /// in scope. Falling back to the component's name as the scope keeps such findings apart
    /// from each other, where passing null would give every one of them the same key.
    /// </remarks>
    /// <param name="ruleId">The rule.</param>
    /// <param name="component">What it fired against, where that could be resolved.</param>
    /// <param name="scope">What it is about, used when the component could not be.</param>
    /// <param name="evidence">What triggered it.</param>
    public static Finding AtOrAbout(string ruleId, DiscoveredComponent? component, string scope, params (string Key, object? Value)[] evidence)
    {
        var rule = RuleCatalogue.Find(ruleId)
            ?? throw new InvalidOperationException($"Rule '{ruleId}' is not in the catalogue.");

        return Finding.From(rule, component, evidence.ToDictionary(pair => pair.Key, pair => pair.Value),
            component is null ? scope : null);
    }
}

/// <summary>A dialog is present. Removed in 2020, so anything found is a leftover record.</summary>
public sealed class DialogPresentHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "lifecycle.dialogPresent";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.OfType("dialog").Select(dialog => Fire.At(RuleId, dialog,
            ("state", dialog.Attribute<string>("statecode")),
            ("table", dialog.Attribute<string>("primaryEntity")),
            ("note", "Dialogs were removed from the platform in 2020. This record cannot run.")));
    }
}

/// <summary>
/// A classic workflow carrying live logic.
/// </summary>
/// <remarks>
/// Fires on an activated workflow. Where runtime evidence exists the dormant ones are split
/// out by a separate rule, and where it does not, activation is the best available signal
/// and the evidence says so rather than implying an execution count nobody measured.
/// </remarks>
public sealed class ClassicWorkflowInUseHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "lifecycle.classicWorkflowInUse";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var workflows = context.OfType("classicWorkflowBackground")
            .Concat(context.OfType("classicWorkflowRealtime"));

        foreach (var workflow in workflows)
        {
            // StateCode 1 is activated. A draft workflow is not carrying live logic and
            // reporting it as though it were inflates every count in the report.
            if (workflow.Attribute<string>("statecode") is not "1" and not "Activated") continue;

            var isRealtime = workflow.TypeId == "classicWorkflowRealtime";
            var executions = workflow.Attribute<int?>("executionCount30d");

            yield return Fire.At(RuleId, workflow,
                ("mode", isRealtime ? "real time" : "background"),
                ("table", workflow.Attribute<string>("primaryEntity")),
                ("steps", workflow.Attribute<int?>("stepCount")),
                ("executions30d", executions),
                ("evidenceQuality", executions is null
                    ? "Activation only. No runtime evidence was available, so whether it actually runs is unknown."
                    : "Runtime evidence available."),
                ("replacementWarning", isRealtime
                    ? "Real time. It runs inside the transaction and can block a save, which a cloud flow cannot do. Do not recommend Power Automate without reading the definition."
                    : null));
        }
    }
}

/// <summary>A custom workflow activity holding a classic workflow in place.</summary>
public sealed class CustomWorkflowActivityHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "lifecycle.customWorkflowActivity";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var activity in context.OfType("customWorkflowActivity"))
        {
            var callers = context.Inbound(activity.StableKey, "callsActivity");
            if (callers.Count == 0) continue;

            yield return Fire.At(RuleId, activity,
                ("callingWorkflows", callers.Count),
                ("order", "The workflows calling this cannot be replaced until the code has somewhere to go. This sets the order of the modernisation backlog."));
        }
    }
}

/// <summary>An RDL report, which is a Power BI candidate with a real hours cost.</summary>
public sealed class RdlReportHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "lifecycle.rdlReport";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.OfType("report").Select(report => Fire.At(RuleId, report,
            ("table", report.Attribute<string>("primaryEntity")),
            ("lastRunUtc", report.Attribute<string>("lastRunUtc")),
            ("note", "Decide per report. One nobody has run in a year is a deletion, not a migration.")));
    }
}

/// <summary>A cloud flow with no failure path.</summary>
public sealed class FlowNoErrorHandlingHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "quality.flowNoErrorHandling";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var flow in context.OfType("cloudFlow"))
        {
            var actions = flow.Attribute<int?>("actionCount");

            // Unknown action count means the definition was not read. The rule does not fire
            // on a guess: a flow reported as having no error handling when nobody opened it
            // is a finding a client will disprove in front of you.
            if (actions is null or <= 5) continue;
            if (flow.Attribute<bool>("hasErrorHandling")) continue;

            yield return Fire.At(RuleId, flow,
                ("actions", actions),
                ("hasFailurePath", false),
                ("consequence", "It fails silently. The run history knows, and nobody reads the run history unprompted."));
        }
    }
}

/// <summary>A cloud flow too large to reason about.</summary>
public sealed class FlowSizeHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "quality.flowSize";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var flow in context.OfType("cloudFlow"))
        {
            var actions = flow.Attribute<int?>("actionCount");
            var depth = flow.Attribute<int?>("maxDepth");

            if (actions is null) continue;
            if (actions <= 40 && depth is null or <= 4) continue;

            yield return Fire.At(RuleId, flow,
                ("actions", actions),
                ("maxNestingDepth", depth),
                ("threshold", "Forty actions, or nesting deeper than four."));
        }
    }
}

/// <summary>A flow carrying a connection rather than a connection reference.</summary>
public sealed class MissingConnectionReferenceHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "alm.missingConnectionReference";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var references = context.OfType("connectionReference")
            .Select(reference => reference.SchemaName)
            .Where(name => name is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var flow in context.OfType("cloudFlow"))
        {
            var connectors = flow.Attribute<string>("connectorIds");
            if (string.IsNullOrWhiteSpace(connectors)) continue;

            var declared = flow.Attribute<int?>("connectionReferenceCount") ?? 0;
            var used = connectors.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;

            if (declared >= used) continue;

            yield return Fire.At(RuleId, flow,
                ("connectorsUsed", used),
                ("connectionReferencesDeclared", declared),
                ("referencesInSolution", references.Count),
                ("consequence", "It will import and it will not run, and the error is reported by whoever tested the wrong thing first."));
        }
    }
}

/// <summary>An environment variable shipping a default value.</summary>
public sealed class EnvironmentVariableDefaultHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "alm.environmentVariableWithDefault";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var variable in context.OfType("environmentVariable"))
        {
            if (!variable.Attribute<bool>("hasDefaultValue")) continue;

            var value = variable.Attribute<string>("defaultValue");

            yield return Fire.At(RuleId, variable,
                ("defaultValue", variable.Attribute<bool>("isSecret") ? "(secret, not shown)" : value),
                ("consequence", "The default travels into the target environment and looks like a deliberate setting. Nobody changes it because nothing says it needs changing."));
        }
    }
}

/// <summary>
/// An environment specific value hard coded into a definition.
/// </summary>
/// <remarks>
/// The Microsoft host exclusions matter more than the patterns do. Without them every flow
/// calling Dataverse or Graph is a finding, the rule produces hundreds of them, and a
/// consultant stops reading the category.
/// </remarks>
public sealed partial class HardCodedEnvironmentValueHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "alm.hardCodedEnvironmentValue";

    private static readonly string[] MicrosoftHosts =
    [
        "api.powerplatform.com", "dynamics.com", "microsoft.com", "microsoftonline.com",
        "azure.com", "windows.net", "office.com", "sharepoint.com", "office365.com",
        "azure-api.net", "schema.org", "schemas.microsoft.com"
    ];

    [GeneratedRegex(@"https?://[\w.-]+(?::\d+)?(?:/[\w./%-]*)?", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex UrlPattern();

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Code components too. A PCF bundle is somebody's JavaScript shipped into the
        // estate, and an endpoint hard coded into one travels between environments exactly
        // the way a hard coded endpoint in a web resource does.
        var searchable = context.OfType("cloudFlow")
            .Concat(context.OfType("jsWebResource"))
            .Concat(context.OfType("canvasApp"))
            .Concat(context.OfType("pcfControl"));

        foreach (var component in searchable)
        {
            var content = component.Attribute<string>("content")
                ?? component.Attribute<string>("definitionText");

            if (string.IsNullOrWhiteSpace(content)) continue;

            var external = UrlPattern().Matches(content)
                .Select(match => match.Value)
                .Where(url => !MicrosoftHosts.Any(host => url.Contains(host, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToList();

            if (external.Count == 0) continue;

            yield return Fire.At(RuleId, component,
                ("urls", string.Join(", ", external)),
                ("excluded", "Microsoft service hosts are excluded. These are not."),
                ("consequence", "The solution imports cleanly into test and points at production."));
        }
    }
}

/// <summary>
/// A credential sitting in a definition that is exported and versioned.
/// </summary>
/// <remarks>
/// Deliberately conservative. A false positive here sends somebody on a rotation exercise for
/// nothing, and a rule that cries wolf on this category is one people switch off.
/// </remarks>
public sealed partial class SecretInDefinitionHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "security.secretInDefinition";

    [GeneratedRegex(@"(?:api[_-]?key|client[_-]?secret|password|accountkey|sas[_-]?token|bearer)\s*[""':=]{1,2}\s*[""']?([A-Za-z0-9+/=_\-]{20,})", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex SecretPattern();

    [GeneratedRegex(@"(?:AccountKey|SharedAccessKey)=[A-Za-z0-9+/=]{40,}", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex ConnectionStringPattern();

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var component in context.Components)
        {
            var content = component.Attribute<string>("content")
                ?? component.Attribute<string>("definitionText");

            if (string.IsNullOrWhiteSpace(content)) continue;

            var hits = SecretPattern().Count(content) + ConnectionStringPattern().Count(content);
            if (hits == 0) continue;

            // The value itself is never carried into the evidence. This evidence ends up in a
            // work item, in an Excel export and in a PDF, and copying a live credential into
            // three more places is not an improvement on where it already is.
            yield return Fire.At(RuleId, component,
                ("matches", hits),
                ("valueShown", false),
                ("order", "Rotate first, then move it to Key Vault through an environment variable. In that order: the old value is already in every exported copy of this solution and in source control history."));
        }
    }
}

/// <summary>A plugin assembly that cannot be deployed online.</summary>
public sealed class PluginNotSandboxedHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "quality.pluginNotSandboxed";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var assembly in context.OfType("pluginAssembly"))
        {
            var isolation = assembly.Attribute<string>("isolationMode");
            var source = assembly.Attribute<string>("sourceType");

            // Isolation mode 2 is sandbox, source type 0 is database. Anything else cannot be
            // imported into an online environment.
            var sandboxed = isolation is "2" or "Sandbox";
            var inDatabase = source is "0" or "Database" or null;

            if (sandboxed && inDatabase) continue;

            yield return Fire.At(RuleId, assembly,
                ("isolationMode", isolation),
                ("sourceType", source),
                ("consequence", "It cannot be deployed to an online environment and it cannot be moved. Usually a finding about where the solution came from rather than about the code."));
        }
    }
}

/// <summary>A form loading more script than it needs.</summary>
public sealed class FormScriptWeightHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "quality.formScriptOnLoadWeight";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var form in context.OfType("form"))
        {
            var libraries = form.Attribute<int?>("scriptLibraryCount") ?? 0;
            if (libraries <= 4) continue;

            var loaded = context.Outbound(form.StableKey, "loadsScript")
                .Select(link => link.ToKey)
                .ToList();

            var largest = context.Components
                .Where(component => loaded.Contains(component.StableKey, StringComparer.Ordinal))
                .Select(component => component.Attribute<long?>("sizeBytes") ?? 0)
                .DefaultIfEmpty(0)
                .Max();

            yield return Fire.At(RuleId, form,
                ("libraries", libraries),
                ("largestLibraryBytes", largest),
                ("table", form.Attribute<string>("table")),
                ("consequence", "Form load time is what users complain about and what nobody measures."));
        }
    }
}

/// <summary>A custom security role holding organisation level write.</summary>
public sealed class OrganisationLevelWriteHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "security.organisationLevelWrite";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var role in context.OfType("securityRole"))
        {
            if (!role.Attribute<bool>("hasOrganisationLevelWrite")) continue;

            yield return Fire.At(RuleId, role,
                ("privileges", role.Attribute<int?>("privilegeCount")),
                ("userCount", role.Attribute<int?>("userCount")),
                ("note", "Usually granted to make something work during a go live and never narrowed afterwards."));
        }
    }
}

/// <summary>Components carrying more than one publisher prefix in one solution.</summary>
public sealed class PublisherPrefixSprawlHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "alm.publisherPrefixSprawl";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var bySolution = context.Components
            .Where(component => !component.IsManaged && Prefix(component.SchemaName) is not null)
            .GroupBy(component => component.SolutionUniqueName ?? "unknown", StringComparer.OrdinalIgnoreCase);

        foreach (var solution in bySolution)
        {
            var prefixes = solution
                .Select(component => Prefix(component.SchemaName)!)
                .Where(prefix => prefix.Length is > 1 and <= 8)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (prefixes.Count <= 1) continue;

            yield return Fire.About(RuleId, solution.Key,
                ("solution", solution.Key),
                ("prefixes", string.Join(", ", prefixes)),
                ("count", prefixes.Count),
                ("note", "Renaming an existing prefix is not something the platform permits. Agree one for new work."));
        }
    }

    /// <summary>
    /// The publisher prefix on a component's schema name, or null when it carries none.
    /// </summary>
    /// <remarks>
    /// A column arrives as table.column and a form as table.form.id, so the prefix belongs to
    /// the last segment. Taking it from the front of the whole string reports every column
    /// under its table's prefix, which is how a solution holding two of them reported one and
    /// this rule matched nothing without ever failing.
    /// </remarks>
    private static string? Prefix(string? schemaName)
    {
        if (schemaName is null) return null;

        var name = schemaName[(schemaName.LastIndexOf('.') + 1)..];
        var underscore = name.IndexOf('_');

        return underscore > 0 ? name[..underscore] : null;
    }
}

/// <summary>Custom components with no description.</summary>
public sealed class MissingDescriptionHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "quality.missingDescription";

    private static readonly string[] Types =
        ["table", "column", "cloudFlow", "customApi", "securityRole", "environmentVariable"];

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var typeId in Types)
        {
            var missing = context.OfType(typeId)
                .Where(component => !component.IsManaged)
                .Where(component => component.Attribute<bool?>("isCustom") is not false)
                .Where(component => string.IsNullOrWhiteSpace(component.Attribute<string>("description")))
                .ToList();

            if (missing.Count == 0) continue;

            // One finding per type rather than per component. Four hundred individually named
            // findings for a low severity hygiene rule buries every other category in the
            // report, and the work is done in one sitting anyway.
            yield return Fire.About(RuleId, typeId,
                ("componentType", typeId),
                ("count", missing.Count),
                ("examples", string.Join(", ", missing.Take(10).Select(component => component.DisplayName))),
                ("solution", missing[0].SolutionUniqueName));
        }
    }
}

/// <summary>
/// One table with logic in more than three places.
/// </summary>
/// <remarks>
/// The finding that explains why a change on that table takes three weeks and breaks
/// something unrelated. Counts mechanisms rather than components: forty business rules are
/// one mechanism, and a table with forty business rules has a different problem.
/// </remarks>
public sealed class LogicSpreadHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "architecture.logicSpread";

    private static readonly Dictionary<string, string> Mechanisms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["classicWorkflowBackground"] = "classic workflow",
        ["classicWorkflowRealtime"] = "real time workflow",
        ["cloudFlow"] = "cloud flow",
        ["businessRule"] = "business rule",
        ["lowCodePlugin"] = "low code plugin",
        ["pluginStep"] = "plugin",
        ["calculatedColumn"] = "calculated column",
        ["formulaColumn"] = "formula column",
        ["jsWebResource"] = "client script"
    };

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var perTable = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var component in context.Components)
        {
            if (!Mechanisms.TryGetValue(component.TypeId, out var mechanism)) continue;

            var table = component.Attribute<string>("primaryEntity") ?? component.Attribute<string>("table");
            if (string.IsNullOrWhiteSpace(table)) continue;

            if (!perTable.TryGetValue(table, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                perTable[table] = set;
            }

            set.Add(mechanism);
        }

        foreach (var (table, mechanisms) in perTable.Where(pair => pair.Value.Count > 3))
        {
            var component = context.OfType("table")
                .FirstOrDefault(candidate => string.Equals(candidate.SchemaName, table, StringComparison.OrdinalIgnoreCase));

            yield return Fire.AtOrAbout(RuleId, component, table,
                ("table", table),
                ("mechanisms", string.Join(", ", mechanisms.OrderBy(mechanism => mechanism, StringComparer.Ordinal))),
                ("count", mechanisms.Count),
                ("consequence", "Nobody can hold the order of execution in their head, and no document describes it. Mapping it is most of the value even if nothing is consolidated."));
        }
    }
}
