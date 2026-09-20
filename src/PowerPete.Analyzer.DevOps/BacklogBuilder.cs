namespace PowerPete.Analyzer.DevOps;

using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using PowerPete.Analyzer.Domain;
using PowerPete.Analyzer.Domain.Localization;

/// <summary>One work item as it would be created.</summary>
/// <param name="Key">The deterministic identifier. What makes a second publish update rather than duplicate.</param>
/// <param name="Type">epic, feature, story, task or bug.</param>
/// <param name="Title">Truncated at 255, component name preserved over rule name.</param>
/// <param name="DescriptionHtml">The six sections, in a fixed order.</param>
/// <param name="AcceptanceCriteria">From the criteria contract, with the component substituted in.</param>
/// <param name="TestRequirement">Separate from the criteria because a state is not a test.</param>
/// <param name="Priority">1 to 4, from severity.</param>
/// <param name="StoryPoints">Approved points, or null.</param>
/// <param name="LowHours">The approved range, which goes in the description.</param>
/// <param name="HighHours">The approved range, which goes in the description.</param>
/// <param name="Tags">So a client can filter to this tool's output and delete it wholesale.</param>
/// <param name="ParentKey">The feature or epic above it.</param>
/// <param name="FindingIds">Which findings it covers.</param>
public sealed record BacklogItem(
    string Key,
    string Type,
    string Title,
    string DescriptionHtml,
    string AcceptanceCriteria,
    string TestRequirement,
    int Priority,
    int? StoryPoints,
    decimal? LowHours,
    decimal? HighHours,
    IReadOnlyList<string> Tags,
    string? ParentKey,
    IReadOnlyList<Guid> FindingIds);

/// <summary>One acceptance criterion from the contract, before substitution.</summary>
/// <param name="Given">The component, in context.</param>
/// <param name="When">What has been done.</param>
/// <param name="Then">What must then be true.</param>
/// <param name="TestRequirement">How to prove it, which is usually the larger half of the work.</param>
public sealed record Criterion(string Given, string When, string Then, string TestRequirement);

/// <summary>
/// Turns findings into a backlog somebody would actually groom.
/// </summary>
/// <remarks>
/// One work item per finding would produce four hundred tasks nobody grooms. One per rule
/// loses the evidence, which is the part a client is buying. The grouping here is what a
/// consultant would have done by hand: an epic per category, a feature per rule that has
/// enough findings to need one, a story per finding that deserves naming, and trivial
/// findings batched.
/// </remarks>
public sealed class BacklogBuilder
{
    private readonly IReadOnlyDictionary<string, Criterion> criteria;
    private readonly Guid engagementId;
    private readonly string engagementName;

    /// <summary>Builds a backlog builder.</summary>
    /// <param name="engagementId">Which engagement, for the deterministic keys.</param>
    /// <param name="engagementName">Which engagement, for the tags.</param>
    /// <param name="criteria">The acceptance criteria contract, keyed by rule id.</param>
    /// <param name="language">
    /// The language the work items are written in, which is the language of the team who will
    /// pick them up and is not always the language of the report.
    /// </param>
    public BacklogBuilder(
        Guid engagementId,
        string engagementName,
        IReadOnlyDictionary<string, Criterion> criteria,
        string? language = null)
    {
        this.engagementId = engagementId;
        this.engagementName = engagementName;
        this.criteria = criteria;

        text = new Localiser("backlog", language);
        rules = new Localiser("finding", language);
    }

    /// <summary>The work item's own words: its headings, its criteria and its caveats.</summary>
    private readonly Localiser text;

    /// <summary>
    /// The rule catalogue, in the same language.
    /// </summary>
    /// <remarks>
    /// The same namespace the report reads, so a finding and the work item raised from it say
    /// the same thing. A team reading "Dialoog nog aanwezig" in Azure DevOps and a client
    /// reading something else in the report is a conversation nobody wants to have.
    /// </remarks>
    private readonly Localiser rules;

    /// <summary>
    /// Builds the backlog.
    /// </summary>
    /// <param name="findings">Every finding, with its estimate.</param>
    /// <param name="runContext">Run, date, extraction mode and identity, for the provenance section.</param>
    public IReadOnlyList<BacklogItem> Build(
        IReadOnlyList<(Finding Finding, Estimate Estimate, DiscoveredComponent? Component)> findings,
        string runContext)
    {
        ArgumentNullException.ThrowIfNull(findings);

        var items = new List<BacklogItem>();
        var byCategory = findings
            .Where(entry => entry.Finding.Rule is { } rule && rule.WorkItemType != "none")
            .GroupBy(entry => entry.Finding.Rule!.Category, StringComparer.OrdinalIgnoreCase);

        foreach (var category in byCategory)
        {
            var epicKey = StableKeys.ForWorkItem(engagementId, $"epic:{category.Key}");
            var epicLow = category.Sum(entry => entry.Estimate.LowHours);
            var epicHigh = category.Sum(entry => entry.Estimate.HighHours);

            items.Add(new BacklogItem(
                epicKey,
                "epic",
                Truncate($"{Title(category.Key)} ({category.Count()} findings)"),
                $"<p>Everything found in the {Title(category.Key)} category by the Power Platform Solution Analyzer.</p>" +
                $"<p>Estimated at {epicLow:0.#} to {epicHigh:0.#} hours across {category.Count()} findings. " +
                "The range is the sum of the individual estimates. Each child item carries its own rationale.</p>" +

                // Which solutions this category actually touches. An epic that says only how
                // many findings it holds gives a delivery lead nothing to plan around.
                Touches(category) +
                $"<p>{runContext}</p>",
                "Every child item is closed or explicitly rejected with a reason recorded.",
                "None at this level. The tests are on the children, where the changes are.",
                category.Min(entry => Priority(entry.Finding.Severity)),
                null,
                epicLow,
                epicHigh,
                [.. Tags(category.Key, null, null)],
                null,
                []));

            foreach (var rule in category.GroupBy(entry => entry.Finding.RuleId, StringComparer.Ordinal))
            {
                var definition = rule.First().Finding.Rule!;
                var needsFeature = rule.Count() > 3 || definition.WorkItemType == "epic";
                var parentKey = epicKey;

                if (needsFeature)
                {
                    var featureKey = StableKeys.ForWorkItem(engagementId, $"feature:{definition.Id}");

                    items.Add(new BacklogItem(
                        featureKey,
                        "feature",
                        Truncate($"{definition.Name} ({rule.Count()})"),
                        $"<h3>Why it matters</h3><p>{Escape(definition.Why)}</p>" +
                        $"<h3>Recommended approach</h3><p>{Escape(definition.Recommendation)}</p>" +
                        (definition.FalsePositive is null
                            ? string.Empty
                            : $"<h3>Where this rule is known to be wrong</h3><p>{Escape(definition.FalsePositive)}</p>") +

                        // Every component under this feature, with its solution. It said how
                        // many and named none of them.
                        Covers(rule) +
                        $"<p>{runContext}</p>",
                        "Every child item is closed or explicitly rejected with a reason recorded.",
                        "On the children.",
                        Priority(definition.Severity),
                        null,
                        rule.Sum(entry => entry.Estimate.LowHours),
                        rule.Sum(entry => entry.Estimate.HighHours),
                        [.. Tags(category.Key, definition.Id, definition.Severity.ToString())],
                        epicKey,
                        []));

                    parentKey = featureKey;
                }

                // Trivial findings are batched. Twenty missing descriptions is one task, and
                // the components are named in the description so nothing is lost.
                if (definition.WorkItemType == "task" && rule.Count() > 1)
                {
                    foreach (var batch in rule.Chunk(20))
                    {
                        items.Add(BuildBatch(definition, batch, parentKey, category.Key, runContext));
                    }

                    continue;
                }

                foreach (var entry in rule)
                {
                    items.Add(BuildOne(definition, entry, parentKey, category.Key, runContext));
                }
            }
        }

        return items;
    }

    private BacklogItem BuildOne(
        AnalysisRule rule,
        (Finding Finding, Estimate Estimate, DiscoveredComponent? Component) entry,
        string parentKey,
        string category,
        string runContext)
    {
        var criterion = Criterion(rule, entry.Component, entry.Finding);

        // Anything the model put at 21 needs splitting before it goes in a sprint, so it is
        // created as a feature rather than a story and says why.
        var type = entry.Estimate.StoryPoints == 21 ? "feature" : rule.WorkItemType;

        return new BacklogItem(
            StableKeys.ForWorkItem(engagementId, entry.Finding.StableKey),
            type,
            Truncate($"{RuleName(rule)}: {entry.Finding.ComponentName ?? text["backlog.solution", "solution"]}"),
            Describe(rule, entry, runContext, type == "feature" && rule.WorkItemType != "feature"),
            criterion.Rendered,
            criterion.Test,
            Priority(entry.Finding.Severity),
            entry.Estimate.StoryPoints,
            entry.Estimate.LowHours,
            entry.Estimate.HighHours,
            [.. Tags(category, rule.Id, entry.Finding.Severity.ToString()), $"estimate-{entry.Estimate.Layer}"],
            parentKey,
            [entry.Finding.FindingId]);
    }

    /// <summary>Which solutions a category's findings are spread across.</summary>
    /// <remarks>
    /// A count on its own does not help anybody plan. "Forty findings" is a number; "forty
    /// findings across three solutions, thirty-one of them in one" is a shape somebody can
    /// allocate people to.
    /// </remarks>
    /// <param name="entries">Everything in the category.</param>
    private string Touches(
        IEnumerable<(Finding Finding, Estimate Estimate, DiscoveredComponent? Component)> entries)
    {
        var bySolution = entries
            .GroupBy(entry => entry.Component?.SolutionUniqueName ?? text["backlog.unattributed", "Not attributed"],
                StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ToList();

        if (bySolution.Count == 0) return string.Empty;

        var builder = new StringBuilder();

        builder.Append(CultureInfo.InvariantCulture,
            $"<h3>{Escape(text["backlog.whereItIs", "Where it is"])}</h3><ul>");

        foreach (var group in bySolution)
        {
            builder.Append("<li><b>").Append(Escape(group.Key)).Append("</b>: ")
                .Append(CultureInfo.InvariantCulture, $"{group.Count()} ")
                .Append(Escape(text["backlog.findings", "findings"]))
                .Append("</li>");
        }

        builder.Append("</ul>");

        return builder.ToString();
    }

    /// <summary>
    /// The components a rolled-up item covers, with the solution each one is in.
    /// </summary>
    /// <remarks>
    /// A story names one component and is self explanatory. An epic, a feature and a batched
    /// task each stand for several, and they said how many and nothing else: a delivery team
    /// picked up "Missing description (14 components)" with no way to find the fourteen.
    ///
    /// A table rather than a list, because the solution is the column somebody sorts by when
    /// they are deciding who owns the work, and a managed component is somebody else's to
    /// fix and has to be visible before anybody estimates around it.
    /// </remarks>
    /// <param name="entries">What the item rolls up.</param>
    private string Covers(
        IEnumerable<(Finding Finding, Estimate Estimate, DiscoveredComponent? Component)> entries)
    {
        var rows = entries
            .OrderBy(entry => entry.Component?.SolutionUniqueName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Finding.ComponentName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var builder = new StringBuilder();

        builder.Append(CultureInfo.InvariantCulture,
            $"<h3>{Escape(text["backlog.covers", "What this covers"])}</h3>");

        builder.Append("<table><thead><tr>")
            .Append(CultureInfo.InvariantCulture, $"<th>{Escape(text["backlog.component", "Component"])}</th>")
            .Append(CultureInfo.InvariantCulture, $"<th>{Escape(text["backlog.type", "Type"])}</th>")
            .Append(CultureInfo.InvariantCulture, $"<th>{Escape(text["backlog.solution", "Solution"])}</th>")
            .Append(CultureInfo.InvariantCulture, $"<th>{Escape(text["backlog.detail", "Detail"])}</th>")
            .Append("</tr></thead><tbody>");

        foreach (var entry in rows)
        {
            builder.Append("<tr><td>")
                .Append(Escape(entry.Finding.ComponentName ?? text["backlog.solutionWideFinding", "A solution wide finding"]))

                // Said in the row it applies to. A team that discovers halfway through a
                // sprint that four of fourteen are managed has lost the sprint.
                .Append(entry.Component?.IsManaged == true
                    ? $" <i>({Escape(text["backlog.managed", "managed"])})</i>"
                    : string.Empty)
                .Append("</td><td>")
                .Append(Escape(entry.Component?.Type?.Name ?? entry.Component?.TypeId ?? string.Empty))
                .Append("</td><td>")
                .Append(Escape(entry.Component?.SolutionUniqueName ?? text["backlog.unattributed", "Not attributed"]))
                .Append("</td><td>")

                // The evidence that fired, not the rule restated. It is different per row
                // and it is the reason this row is in the list at all.
                .Append(Escape(Detail(entry.Finding)))
                .Append("</td></tr>");
        }

        builder.Append("</tbody></table>");

        return builder.ToString();
    }

    /// <summary>The evidence for one finding, in one line.</summary>
    /// <param name="finding">The finding.</param>
    private static string Detail(Finding finding)
    {
        var values = finding.Evidence
            .Where(pair => pair.Value is not null)
            .Select(pair => $"{pair.Key}: {pair.Value}")
            .ToList();

        return values.Count == 0 ? string.Empty : string.Join("; ", values);
    }

    private BacklogItem BuildBatch(
        AnalysisRule rule,
        (Finding Finding, Estimate Estimate, DiscoveredComponent? Component)[] batch,
        string parentKey,
        string category,
        string runContext)
    {
        var criterion = Criterion(rule, batch[0].Component, batch[0].Finding);

        return new BacklogItem(
            StableKeys.ForWorkItem(engagementId, $"batch:{rule.Id}:{batch[0].Finding.StableKey}"),
            "task",
            Truncate($"{RuleName(rule)} ({batch.Length} {text["backlog.components", "components"]})"),
            $"<h3>What was found</h3><p>{Escape(rules[$"finding.{rule.Id}.why", rule.Why])}</p>" +

            // A list of bare names was all this had, so a team picking up "Missing
            // description (14 components)" could not find the fourteen, could not tell which
            // solution any of them was in, and could not see that some were managed.
            Covers(batch) +
            $"<h3>Recommended approach</h3><p>{Escape(rules[$"finding.{rule.Id}.recommendation", rule.Recommendation])}</p>" +
            $"<h3>Estimate</h3><p>{batch.Sum(entry => entry.Estimate.LowHours):0.#} to " +
            $"{batch.Sum(entry => entry.Estimate.HighHours):0.#} hours for all {batch.Length}.</p>" +
            $"<p>{runContext}</p>",
            criterion.Rendered,
            criterion.Test,
            Priority(batch[0].Finding.Severity),
            FitPoints(batch.Sum(entry => entry.Estimate.StoryPoints ?? 0)),
            batch.Sum(entry => entry.Estimate.LowHours),
            batch.Sum(entry => entry.Estimate.HighHours),
            [.. Tags(category, rule.Id, batch[0].Finding.Severity.ToString())],
            parentKey,
            [.. batch.Select(entry => entry.Finding.FindingId)]);
    }

    /// <summary>
    /// The six description sections, in the order the contract fixes.
    /// </summary>
    /// <remarks>
    /// Fixed so a reader who has seen one of these has seen them all. The provenance section
    /// at the bottom is the one people skip and the one that matters in six months, when
    /// somebody has to decide how much to trust a work item a tool created.
    /// </remarks>
    private string Describe(
        AnalysisRule rule,
        (Finding Finding, Estimate Estimate, DiscoveredComponent? Component) entry,
        string runContext,
        bool needsSplitting)
    {
        var builder = new StringBuilder();

        builder.Append(CultureInfo.InvariantCulture, $"<h3>{Escape(text["backlog.whatWasFound", "What was found"])}</h3><p>")
            .Append(Escape(entry.Finding.ComponentName ?? text["backlog.solutionWideFinding", "A solution wide finding"]))
            .Append(entry.Component?.SolutionUniqueName is { } solution ? $", {text["backlog.inSolution", "in solution"]} {Escape(solution)}" : string.Empty)
            .Append(".</p>");

        builder.Append(CultureInfo.InvariantCulture, $"<h3>{Escape(text["backlog.evidence", "Evidence"])}</h3><ul>");
        foreach (var (key, value) in entry.Finding.Evidence.Where(pair => pair.Value is not null))
        {
            builder.Append("<li><b>").Append(Escape(key)).Append("</b>: ").Append(Escape(value!.ToString() ?? string.Empty)).Append("</li>");
        }

        builder.Append("</ul>");

        builder.Append(CultureInfo.InvariantCulture, $"<h3>{Escape(text["backlog.whyItMatters", "Why it matters"])}</h3><p>")
            .Append(Escape(rules[$"finding.{rule.Id}.why", rule.Why])).Append("</p>");
        builder.Append(CultureInfo.InvariantCulture, $"<h3>{Escape(text["backlog.recommendedApproach", "Recommended approach"])}</h3><p>")
            .Append(Escape(rules[$"finding.{rule.Id}.recommendation", rule.Recommendation])).Append("</p>");

        if (rule.FalsePositive is not null)
        {
            builder.Append(CultureInfo.InvariantCulture, $"<h3>{Escape(text["backlog.beforeYouAct", "Before you act on this"])}</h3><p>")
                .Append(Escape(rules[$"finding.{rule.Id}.falsePositive", rule.FalsePositive])).Append("</p>");
        }

        builder.Append(CultureInfo.InvariantCulture, $"<h3>{Escape(text["backlog.estimate", "Estimate"])}</h3><p>")
            .Append(CultureInfo.InvariantCulture, $"{entry.Estimate.LowHours:0.#} {text["backlog.to", "to"]} {entry.Estimate.HighHours:0.#} {text["backlog.hours", "hours"]}. ")
            .Append(CultureInfo.InvariantCulture, $"{text["backlog.confidence", "Confidence"]}: {text["backlog.confidence." + entry.Estimate.Confidence.ToString().ToLowerInvariant(), entry.Estimate.Confidence.ToString().ToLowerInvariant()]}. ")
            .Append(entry.Estimate.Layer switch
            {
                EstimateLayer.EngagementOverride => text["backlog.layer.override", "Set by a consultant on this engagement."],
                EstimateLayer.Model => text["backlog.layer.model", "Produced by a model, one finding at a time."],
                _ => text["backlog.layer.band", "The band default for this rule. This finding was not estimated individually."]
            })
            .Append("</p><p>").Append(Escape(entry.Estimate.Rationale)).Append("</p>");

        if (entry.Estimate.Assumptions.Count > 0)
        {
            builder.Append(CultureInfo.InvariantCulture, $"<p><b>{Escape(text["backlog.assumes", "Assumes"])}:</b></p><ul>");
            foreach (var assumption in entry.Estimate.Assumptions)
            {
                builder.Append("<li>").Append(Escape(assumption)).Append("</li>");
            }

            builder.Append("</ul>");
        }

        if (entry.Estimate.FlaggedReason is not null)
        {
            builder.Append(CultureInfo.InvariantCulture, $"<p><b>{Escape(text["backlog.flagged", "Flagged"])}:</b> ").Append(Escape(entry.Estimate.FlaggedReason)).Append("</p>");
        }

        if (entry.Estimate.Confidence == Confidence.Low)
        {
            builder.Append(
                CultureInfo.InvariantCulture,
                $"<p><b>{Escape(text["backlog.lowConfidence", "Low confidence."])}</b> {Escape(text["backlog.lowConfidenceDetail", "Look at the component before planning with this number."])}</p>");
        }

        if (needsSplitting)
        {
            builder.Append(CultureInfo.InvariantCulture, $"<p><b>{Escape(text["backlog.featureNotStory", "Created as a feature rather than a story."])}</b> ")
                .Append(Escape(text["backlog.featureNotStoryDetail", "It was sized at 21 points, which means it needs splitting before it goes in a sprint."]))
                .Append("</p>");
        }

        builder.Append(CultureInfo.InvariantCulture, $"<h3>{Escape(text["backlog.provenance", "Provenance"])}</h3><p>").Append(Escape(runContext)).Append("</p>");

        return builder.ToString();
    }

    private (string Rendered, string Test) Criterion(AnalysisRule rule, DiscoveredComponent? component, Finding finding)
    {
        if (!criteria.TryGetValue(rule.Id, out var criterion))
        {
            // No fallback, by design. A generated work item whose acceptance criterion is
            // boilerplate teaches a team to close the rest without reading them, so the build
            // refuses rather than shipping one.
            throw new InvalidOperationException(
                $"Rule '{rule.Id}' produces a {rule.WorkItemType} and has no acceptance criterion. " +
                "Add one to acceptance-criteria.json. There is deliberately no generic fallback.");
        }

        // Translated before substitution, because the placeholders are part of the sentence
        // and a translation that drops one takes a component name with it. The contract's
        // English is the fallback, so an untranslated rule still produces a usable criterion.
        var given = Substitute(text[$"backlog.{rule.Id}.given", criterion.Given], component, finding);
        var when = Substitute(text[$"backlog.{rule.Id}.when", criterion.When], component, finding);
        var then = Substitute(text[$"backlog.{rule.Id}.then", criterion.Then], component, finding);
        var test = Substitute(
            text[$"backlog.{rule.Id}.testRequirement", criterion.TestRequirement], component, finding);

        var rendered = $"<p><b>{Escape(text["backlog.given", "Given"])}</b> {Escape(given)}<br/>" +
            $"<b>{Escape(text["backlog.when", "When"])}</b> {Escape(when)}<br/>" +
            $"<b>{Escape(text["backlog.then", "Then"])}</b> {Escape(then)}</p>" +
            $"<p><b>{Escape(text["backlog.test", "Test"])}:</b> {Escape(test)}</p>";

        return (rendered, test);
    }

    /// <summary>
    /// Substitutes the placeholders.
    /// </summary>
    /// <remarks>
    /// A placeholder with nothing behind it takes its sentence with it rather than being
    /// published as a brace. A work item reading "Given the flow {componentName}" is worse
    /// than one with a shorter criterion.
    /// </remarks>
    private static string Substitute(string text, DiscoveredComponent? component, Finding finding)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["componentName"] = component?.DisplayName ?? finding.ComponentName,
            ["componentType"] = component?.Type?.Name ?? component?.TypeId,
            ["solutionName"] = component?.SolutionUniqueName,
            ["tableName"] = component?.Attribute<string>("table")
                ?? component?.Attribute<string>("primaryEntity")
                ?? Evidence(finding, "table"),
            ["count"] = Evidence(finding, "count"),
            ["evidenceValue"] = finding.Evidence.Values.FirstOrDefault()?.ToString()
        };

        var result = text;

        foreach (var (key, value) in values)
        {
            var token = "{" + key + "}";
            if (!result.Contains(token, StringComparison.OrdinalIgnoreCase)) continue;

            result = string.IsNullOrWhiteSpace(value)
                ? DropSentenceContaining(result, token)
                : result.Replace(token, value, StringComparison.OrdinalIgnoreCase);
        }

        return result.Trim();
    }

    private static string? Evidence(Finding finding, string key) =>
        finding.Evidence.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static string DropSentenceContaining(string text, string token)
    {
        var sentences = text.Split(". ", StringSplitOptions.RemoveEmptyEntries);
        var kept = sentences.Where(sentence => !sentence.Contains(token, StringComparison.OrdinalIgnoreCase));
        var result = string.Join(". ", kept).Trim();

        // If dropping the sentence leaves nothing, keep the original minus the placeholder
        // rather than publishing an empty criterion, which the database refuses anyway.
        return result.Length == 0
            ? text.Replace(token, "the component", StringComparison.OrdinalIgnoreCase)
            : result;
    }

    private IEnumerable<string> Tags(string category, string? ruleId, string? severity)
    {
        yield return "power-platform-analyzer";
        yield return engagementName;
        yield return category;
        if (ruleId is not null) yield return ruleId;
        if (severity is not null) yield return severity.ToLowerInvariant();
    }

    /// <summary>
    /// The nearest point value at or below a summed total.
    /// </summary>
    /// <remarks>
    /// Summing points across a batch and then fitting to the scale is not arithmetic anybody
    /// should defend, and it is better than leaving a twenty item task unpointed. It is capped
    /// at the top of the scale rather than rounding up past it.
    /// </remarks>
    private static int? FitPoints(int summed)
    {
        if (summed <= 0) return null;

        return Estimate.PointScale.Where(scale => scale <= summed).DefaultIfEmpty(1).Max();
    }

    private static int Priority(Severity severity) => severity switch
    {
        Severity.Critical => 1,
        Severity.High => 2,
        Severity.Medium => 3,
        _ => 4
    };


    /// <summary>A rule's name in the language the team reads.</summary>
    private string RuleName(AnalysisRule rule) => rules[$"finding.{rule.Id}.name", rule.Name];

    private static string Title(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private static string Truncate(string title) =>
        title.Length <= 255 ? title : title[..252] + "...";

    private static string Escape(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
}

/// <summary>
/// Writes work items into Azure DevOps.
/// </summary>
/// <remarks>
/// The only thing in this product that writes to a client system. It refuses without an
/// approval whose hash matches the backlog it was given, and it defaults to a dry run on the
/// first publish against a project.
/// </remarks>
public sealed class WorkItemPublisher
{
    private readonly HttpClient client;

    /// <summary>
    /// The organisation's address, with no trailing slash.
    /// </summary>
    /// <remarks>
    /// An address, not a name. Every caller stores and passes
    /// "https://dev.azure.com/contoso" - it is what the connection wizard asks for and what
    /// the project list reads - and this class pasted it into a template that already had
    /// the host in it, producing a URL with "https://dev.azure.com/" twice. Azure DevOps
    /// answered 400 and the publish reported "Bad Request".
    ///
    /// The project list worked throughout, because it treats the same value as an address,
    /// which is why the connection looked healthy right up to the publish.
    /// </remarks>
    private readonly string organisation;
    private readonly string project;
    private readonly ProjectShape shape;

    /// <summary>
    /// What a project will actually accept.
    /// </summary>
    /// <remarks>
    /// Read from the project rather than assumed, which is what the Jira publisher beside
    /// this one already did. This one hardcoded Epic, Feature, User Story, Bug and Task,
    /// which are the Agile process's names. A project on the Basic process has Epic, Issue
    /// and Task and nothing else, so publishing a feature returned 400 and the message said
    /// only "Bad Request".
    /// </remarks>
    /// <param name="Types">The work item type names the project offers.</param>
    /// <param name="Fields">The field reference names each of those types has.</param>
    public sealed record ProjectShape(
        IReadOnlyList<string> Types,
        IReadOnlyDictionary<string, IReadOnlySet<string>> Fields)
    {
        /// <summary>What to assume when the project could not be read.</summary>
        /// <remarks>
        /// Empty, which makes every lookup below fall through to Task and skip every
        /// optional field. A publish that lands as a flat list of tasks is recoverable; one
        /// that fails on the ninth of nine items is not.
        /// </remarks>
        public static ProjectShape Unknown { get; } =
            new([], new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Builds a publisher.</summary>
    /// <param name="client">Authenticated against Azure DevOps. The caller owns the credential.</param>
    /// <param name="organisation">The organisation.</param>
    /// <param name="project">The project.</param>
    /// <param name="shape">What the project accepts, from <see cref="ReadShapeAsync"/>.</param>
    public WorkItemPublisher(HttpClient client, string organisation, string project, ProjectShape? shape = null)
    {
        ArgumentNullException.ThrowIfNull(organisation);

        this.client = client;
        this.organisation = organisation.TrimEnd('/');
        this.project = project;
        this.shape = shape ?? ProjectShape.Unknown;
    }

    /// <summary>
    /// Asks a project which work item types it has and what fields each one carries.
    /// </summary>
    /// <remarks>
    /// Two calls rather than one, because the type list does not carry the fields and a
    /// field that does not exist on a type is a 400 on the whole item. A failure here is not
    /// fatal: the caller gets <see cref="ProjectShape.Unknown"/> and the publish degrades to
    /// tasks with no optional fields rather than refusing to run.
    /// </remarks>
    /// <param name="client">Authenticated against Azure DevOps.</param>
    /// <param name="organisation">The organisation.</param>
    /// <param name="project">The project.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public static async Task<ProjectShape> ReadShapeAsync(
        HttpClient client, string organisation, string project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        try
        {
            using var response = await client.GetAsync(
                new Uri($"{organisation.TrimEnd('/')}/{Uri.EscapeDataString(project)}/_apis/wit/workitemtypes?api-version=7.1"),
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode) return ProjectShape.Unknown;

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty("value", out var values)) return ProjectShape.Unknown;

            var types = new List<string>();
            var fields = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var type in values.EnumerateArray())
            {
                if (!type.TryGetProperty("name", out var name) || name.GetString() is not { Length: > 0 } typeName)
                {
                    continue;
                }

                types.Add(typeName);

                var carries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (type.TryGetProperty("fields", out var typeFields))
                {
                    foreach (var field in typeFields.EnumerateArray())
                    {
                        if (field.TryGetProperty("referenceName", out var reference)
                            && reference.GetString() is { Length: > 0 } referenceName)
                        {
                            carries.Add(referenceName);
                        }
                    }
                }

                fields[typeName] = carries;
            }

            return new ProjectShape(types, fields);
        }
        catch (Exception failure) when (failure is HttpRequestException or JsonException or TaskCanceledException)
        {
            return ProjectShape.Unknown;
        }
    }

    /// <summary>What a publish did.</summary>
    /// <param name="Key">The deterministic key.</param>
    /// <param name="WorkItemId">The identifier Azure DevOps assigned.</param>
    /// <param name="Url">Where it is.</param>
    /// <param name="Action">created, updated, commented or skipped.</param>
    public sealed record Published(string Key, int WorkItemId, string Url, string Action);

    /// <summary>
    /// Publishes a backlog.
    /// </summary>
    /// <remarks>
    /// The count confirmation is not a formality. Nobody means to put nine hundred items in
    /// somebody else's backlog, and the one time it happens is the time the tool never gets
    /// used at that client again.
    /// </remarks>
    /// <param name="items">The approved backlog.</param>
    /// <param name="approvedHash">The hash the approval was bound to.</param>
    /// <param name="backlogHash">The hash of the backlog being published.</param>
    /// <param name="dryRun">When true, returns what would be created and calls nothing.</param>
    /// <param name="confirmedCount">The count the person confirmed, when there are more than two hundred items.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<IReadOnlyList<Published>> PublishAsync(
        IReadOnlyList<BacklogItem> items,
        string approvedHash,
        string backlogHash,
        bool dryRun,
        int? confirmedCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (!string.Equals(approvedHash, backlogHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The backlog has changed since it was approved. Approving one thing and publishing another is " +
                "the failure the approval exists to prevent, so this publish is refused. Re-approve the current backlog.");
        }

        if (items.Count > 200 && confirmedCount != items.Count)
        {
            throw new InvalidOperationException(
                $"This would create {items.Count} work items in {project}. Confirm the count before it runs.");
        }

        if (dryRun)
        {
            return [.. items.Select(item => new Published(item.Key, 0, string.Empty, "skipped"))];
        }

        var published = new List<Published>();
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);

        // Parents first, so a child can be linked as it is created. Epics, then features,
        // then everything else, which is what the ordering below gives.
        foreach (var item in items.OrderBy(item => item.ParentKey is null ? 0 : item.Type == "feature" ? 1 : 2))
        {
            var existing = await FindAsync(item.Key, cancellationToken).ConfigureAwait(false);
            var result = existing is null
                ? await CreateAsync(item, ids, cancellationToken).ConfigureAwait(false)
                : await UpdateAsync(item, existing.Value, cancellationToken).ConfigureAwait(false);

            ids[item.Key] = result.WorkItemId;
            published.Add(result);
        }

        return published;
    }

    private async Task<int?> FindAsync(string key, CancellationToken cancellationToken)
    {
        var query = new
        {
            query = $"SELECT [System.Id] FROM WorkItems WHERE [System.Tags] CONTAINS '{key}'"
        };

        using var response = await client.PostAsJsonAsync(
            $"{organisation}/{Uri.EscapeDataString(project)}/_apis/wit/wiql?api-version=7.1",
            query,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

        var first = document.RootElement.GetProperty("workItems").EnumerateArray().FirstOrDefault();
        return first.ValueKind == JsonValueKind.Object ? first.GetProperty("id").GetInt32() : null;
    }

    private async Task<Published> CreateAsync(BacklogItem item, Dictionary<string, int> ids, CancellationToken cancellationToken)
    {
        var operations = Patch(item);

        if (item.ParentKey is not null && ids.TryGetValue(item.ParentKey, out var parentId))
        {
            operations.Add(new
            {
                op = "add",
                path = "/relations/-",
                value = new
                {
                    rel = "System.LinkTypes.Hierarchy-Reverse",
                    url = $"{organisation}/_apis/wit/workItems/{parentId}"
                }
            });
        }

        using var content = new StringContent(JsonSerializer.Serialize(operations), Encoding.UTF8, "application/json-patch+json");
        using var response = await client.PostAsync(
            new Uri($"{organisation}/{Uri.EscapeDataString(project)}/_apis/wit/workitems/${Uri.EscapeDataString(ResolveType(item.Type))}?api-version=7.1"),
            content,
            cancellationToken).ConfigureAwait(false);

        await ThrowIfRefusedAsync(response, item, ResolveType(item.Type), cancellationToken).ConfigureAwait(false);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

        var id = document.RootElement.GetProperty("id").GetInt32();
        return new Published(item.Key, id, $"{organisation}/{Uri.EscapeDataString(project)}/_workitems/edit/{id}", "created");
    }

    /// <summary>
    /// Updates an item that already exists.
    /// </summary>
    /// <remarks>
    /// Never reopens a closed item. Somebody closed it for a reason and this product does not
    /// know what it was. The same applies in the other direction: a finding that has gone is
    /// commented on, not closed.
    /// </remarks>
    private async Task<Published> UpdateAsync(BacklogItem item, int workItemId, CancellationToken cancellationToken)
    {
        var operations = Patch(item)
            .Where(operation => operation is not null)
            .ToList();

        using var content = new StringContent(JsonSerializer.Serialize(operations), Encoding.UTF8, "application/json-patch+json");
        using var response = await client.PatchAsync(
            new Uri($"{organisation}/{Uri.EscapeDataString(project)}/_apis/wit/workitems/{workItemId}?api-version=7.1"),
            content,
            cancellationToken).ConfigureAwait(false);

        await ThrowIfRefusedAsync(response, item, ResolveType(item.Type), cancellationToken).ConfigureAwait(false);

        return new Published(item.Key, workItemId,
            $"{organisation}/{Uri.EscapeDataString(project)}/_workitems/edit/{workItemId}", "updated");
    }

    private List<object> Patch(BacklogItem item)
    {
        var resolved = ResolveType(item.Type);

        var operations = new List<object>
        {
            Add("/fields/System.Title", item.Title),
            Add("/fields/System.Description", item.DescriptionHtml),
            Add("/fields/System.Tags", string.Join("; ", item.Tags.Append(item.Key)))
        };

        // Every optional field is asked for rather than assumed. Priority is on most types
        // and not on all of them, acceptance criteria is an Agile field, story points do not
        // exist on the Basic process at all, and any one of them absent is a 400 that fails
        // the whole item and says only "Bad Request".
        if (Carries(resolved, "Microsoft.VSTS.Common.Priority"))
        {
            operations.Add(Add("/fields/Microsoft.VSTS.Common.Priority", item.Priority));
        }

        if (item.Type is "story" or "feature" or "bug"
            && Carries(resolved, "Microsoft.VSTS.Common.AcceptanceCriteria"))
        {
            operations.Add(Add("/fields/Microsoft.VSTS.Common.AcceptanceCriteria", item.AcceptanceCriteria));
        }

        if (item.StoryPoints is { } points
            && item.Type is "story" or "feature"
            && Carries(resolved, "Microsoft.VSTS.Scheduling.StoryPoints"))
        {
            operations.Add(Add("/fields/Microsoft.VSTS.Scheduling.StoryPoints", points));
        }

        if (item.LowHours is { } low && item.HighHours is { } high
            && Carries(resolved, "Microsoft.VSTS.Scheduling.OriginalEstimate"))
        {
            // The one place a range becomes a single number, because the field holds one.
            // The range and its rationale are in the description of the same item, so the
            // midpoint is never the only record of it.
            operations.Add(Add("/fields/Microsoft.VSTS.Scheduling.OriginalEstimate", Math.Round((low + high) / 2, 2)));
        }

        return operations;
    }

    /// <summary>
    /// Turns a refusal into a sentence naming the item and what the platform objected to.
    /// </summary>
    /// <remarks>
    /// EnsureSuccessStatusCode throws away the response body, and the body is the only part
    /// with any information in it. Azure DevOps answers a bad field or an unknown work item
    /// type with a precise message and this reported "Bad Request", which sent somebody
    /// reading source code to find out that their project was on the Basic process.
    /// </remarks>
    /// <param name="response">What came back.</param>
    /// <param name="item">The item being published.</param>
    /// <param name="resolved">The work item type it was published as.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    private static async Task ThrowIfRefusedAsync(
        HttpResponseMessage response, BacklogItem item, string resolved, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var detail = body;

        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.TryGetProperty("message", out var message)
                && message.GetString() is { Length: > 0 } text)
            {
                detail = text;
            }
        }
        catch (JsonException)
        {
            // Not JSON. The body as it stands is still better than the status code alone.
        }

        throw new InvalidOperationException(
            $"Azure DevOps refused '{item.Title}' as a {resolved} ({(int)response.StatusCode}): {detail}");
    }

    private static object Add(string path, object value) => new { op = "add", path, value };

    /// <summary>
    /// The nearest thing this project has to the kind of item being published.
    /// </summary>
    /// <remarks>
    /// In order of preference, ending at Task, which every process has. Agile calls a story
    /// a User Story, Scrum calls it a Product Backlog Item, Basic has neither and calls
    /// everything below an epic an Issue. Publishing the wrong one is a 400 on that item.
    /// </remarks>
    /// <param name="type">What the backlog builder produced.</param>
    private string ResolveType(string type)
    {
        string[] preferences = type switch
        {
            "epic" => ["Epic", "Feature", "Issue", "Task"],
            "feature" => ["Feature", "Epic", "User Story", "Product Backlog Item", "Issue", "Task"],
            "story" => ["User Story", "Product Backlog Item", "Issue", "Task"],
            "bug" => ["Bug", "Issue", "Task"],
            _ => ["Task", "Issue"],
        };

        foreach (var candidate in preferences)
        {
            if (shape.Types.Contains(candidate, StringComparer.OrdinalIgnoreCase)) return candidate;
        }

        // Nothing matched, which means the project could not be read. Task is the one type
        // every process in Azure DevOps has.
        return "Task";
    }

    /// <summary>Whether a resolved type carries a field.</summary>
    /// <remarks>
    /// True when the project could not be read, because the alternative is dropping every
    /// estimate and every acceptance criterion from a publish on the strength of a failed
    /// metadata call. A field that is genuinely absent then fails that item, which is the
    /// behaviour there was before and is visible rather than silent.
    /// </remarks>
    /// <param name="resolved">The work item type name.</param>
    /// <param name="field">The field reference name.</param>
    private bool Carries(string resolved, string field) =>
        shape.Fields.Count == 0
        || !shape.Fields.TryGetValue(resolved, out var carries)
        || carries.Contains(field);
}

/// <summary>
/// The acceptance criteria, from the generated catalogue.
/// </summary>
/// <remarks>
/// A conversion at one boundary rather than two types that drift. The contract owns the text,
/// the generator turns it into code, and this turns it into the shape the builder wants.
/// </remarks>
public static class AcceptanceCriteria
{
    /// <summary>Every criterion, keyed by rule id.</summary>
    public static IReadOnlyDictionary<string, Criterion> Load() =>
        AcceptanceCriteriaCatalogue.All.ToDictionary(
            entry => entry.Key,
            entry => new Criterion(entry.Value.Given, entry.Value.When, entry.Value.Then, entry.Value.TestRequirement),
            StringComparer.Ordinal);
}
