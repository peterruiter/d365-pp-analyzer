namespace PowerPete.Analyzer.Api;

using System.Text.Json;
using PowerPete.Analyzer.Analysis;
using PowerPete.Analyzer.Data;
using PowerPete.Analyzer.Domain;
using PowerPete.Analyzer.DevOps;
using PowerPete.Analyzer.Export;

/// <summary>
/// Rebuilds a finished run into the documents that leave the product.
/// </summary>
/// <remarks>
/// The renderers take a model and know nothing about a database; the stores hold rows and know
/// nothing about a document. This is the one place that turns one into the other, and it lives
/// in the API rather than in Export because a renderer that needs a connection string is a
/// renderer nobody can test.
///
/// Everything here is read back rather than recomputed. The score a report quotes is the score
/// the run stored, not one worked out again from whatever happened to be kept, because a
/// document produced twice from the same run has to say the same thing both times. The one
/// exception is complexity, which is a pure function of a component and its type and is
/// derived here rather than stored per row.
/// </remarks>
public sealed class ReportComposer(AnalysisStore analysis, WorkspaceStore workspace)
{
    /// <summary>What a run can be turned into, and everything both documents need.</summary>
    /// <param name="RunId">Which run.</param>
    /// <param name="ProducedUtc">When the analysis ran, not when the document was asked for.</param>
    /// <param name="Workbook">The workbook model.</param>
    /// <param name="Report">The report model.</param>
    public sealed record Composed(
        Guid RunId,
        DateTime ProducedUtc,
        FindingsWorkbook.Model Workbook,
        AssessmentReportPdf.Model Report);

    /// <summary>
    /// Composes both documents for the latest run that reached scoring.
    /// </summary>
    /// <remarks>
    /// Null when nothing has been analysed. Not an empty report: a document saying an estate
    /// has no findings, produced from a run that never happened, is the single most damaging
    /// thing this product could hand to a client.
    /// </remarks>
    /// <param name="engagementId">Which engagement.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<Composed?> ComposeAsync(Guid engagementId, CancellationToken cancellationToken)
    {
        var runId = await analysis.GetLatestScoredRunAsync(engagementId, cancellationToken);
        if (runId is null) return null;

        var stored = await analysis.GetScoreAsync(runId.Value, cancellationToken);
        if (stored is null) return null;

        var run = await workspace.GetRunAsync(runId.Value, cancellationToken);
        var engagements = await workspace.ListEngagementsAsync(UserAccess.Everything, cancellationToken);
        var engagement = engagements.FirstOrDefault(candidate => candidate.EngagementId == engagementId);

        var componentRows = await analysis.GetComponentsAsync(runId.Value, cancellationToken);
        var solutions = await analysis.GetSolutionsAsync(runId.Value, cancellationToken);
        var findingRows = await analysis.GetFindingsAsync(runId.Value, cancellationToken);
        var backlogRows = await analysis.GetBacklogAsync(runId.Value, cancellationToken);

        var breakdown = JsonDocument.Parse(stored.Value.BreakdownJson).RootElement;
        var score = ReadScore(breakdown, stored.Value);
        var customisation = Read<List<CustomisationRow>>(breakdown, "customisation") ?? [];
        var roadmap = Read<List<RoadmapItem>>(breakdown, "roadmap") ?? [];

        var components = componentRows.Select(ToComponent).ToList();
        var byKey = components.ToDictionary(component => component.StableKey, StringComparer.Ordinal);

        // The finding's component, looked up rather than left null. The command line leaves it
        // null and the workbook then has a column of blanks beside every finding, which is the
        // column a consultant filters on first.
        var findings = findingRows
            .Select(row => (
                Finding: ToFinding(row),
                Estimate: ToEstimate(row),
                Component: row.ComponentName is not null && byKey.TryGetValue(row.StableKey, out var match)
                    ? match
                    : null))
            .ToList();

        var rater = new ComplexityRater(ComplexityRule.FromContract());
        var rated = components.Select(component => (component, rater.Rate(component))).ToList();

        var backlog = backlogRows
            .Select(item => new BacklogItem(
                item.DeterministicKey,
                item.WorkItemType,
                item.Title,

                // The description is rendered from the finding when a backlog is published and
                // is not kept, so a workbook rebuilt from the database shows the acceptance
                // criterion, which is the half a person reads before deciding anything.
                string.Empty,
                item.AcceptanceCriteria,
                item.TestRequirement ?? string.Empty,
                item.Priority,
                item.StoryPoints,
                item.LowHours,
                item.HighHours,
                [],
                null,
                []))
            .ToList();

        var maturity = await analysis.GetMaturityAsync(engagementId, cancellationToken).ConfigureAwait(false);

        var written = (await analysis.GetNarrativeAsync(engagementId, cancellationToken).ConfigureAwait(false))
            .ToDictionary(section => section.SectionId, section => section.Body, StringComparer.Ordinal);

        var name = engagement?.Name ?? "Engagement";
        var mode = run?.Mode ?? "assessment";
        var produced = run?.CompletedUtc ?? run?.CreatedUtc ?? DateTime.UtcNow;

        return new Composed(
            runId.Value,
            produced,
            new FindingsWorkbook.Model(
                name, runId.Value, produced, mode, run?.CreatedBy,
                score, findings, rated, customisation, backlog),
            new AssessmentReportPdf.Model(
                name, engagement?.ClientName, runId.Value, produced, mode, run?.CreatedBy,
                [.. solutions.Select(solution => solution.UniqueName)],
                score, findings, customisation, roadmap,

                // Read by engagement rather than by run. A workshop is about the client, so
                // the written half survives every re-extraction of the generated half. Where
                // a section has not been written the report still prints its prompt, which
                // is a great deal better than generating a readiness score from metadata and
                // letting it read like one produced from twenty interviews.
                written,

                // Every axis the contract declares, in its order, whether or not anybody has
                // scored it. The radar draws an unscored axis as a spoke with no point on
                // it: nought means the capability is absent, and not having looked is a
                // different statement that must not be drawn as the first.
                [.. MaturityAxes().Select(axis =>
                {
                    var scored = maturity.FirstOrDefault(entry => entry.AxisId == axis);

                    return (axis, scored is null ? (decimal?)null : scored.Score, scored?.Evidence);
                })],

                // Language stays default here; the caller picks it. The flag does not:
                // the cover has to say out loud that the sample estate is a sample, and
                // the only thing that knows is the identifier.
                IsDemonstration: engagementId == AccessStore.DemoEngagementId));
    }

    private static T? Read<T>(JsonElement breakdown, string name) =>
        breakdown.TryGetProperty(name, out var value)
            ? value.Deserialize<T>(Json)
            : default;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The capability axes, in the order the contract declares them.
    /// </summary>
    /// <remarks>
    /// Read from the contract rather than listed here, so the radar, the scoring form and
    /// the report cannot disagree about which axes exist or what order they go round in. A
    /// radar whose axes move between two engagements is not comparable with itself.
    /// </remarks>
    private static IReadOnlyList<string> MaturityAxes()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "build", "contracts", "report-model.json");

        if (!File.Exists(path)) return [];

        using var document = JsonDocument.Parse(File.ReadAllText(path));

        return
        [
            .. document.RootElement.GetProperty("maturityAxes").GetProperty("groups").EnumerateArray()
                .SelectMany(group => group.GetProperty("axes").EnumerateArray())
                .Select(axis => axis.GetString())
                .OfType<string>()
        ];
    }

    /// <summary>
    /// The score the run stored, or as much of it as an older run kept.
    /// </summary>
    /// <remarks>
    /// Runs written before the whole score was stored kept only a handful of its fields. Those
    /// reports come back with the totals from the score row and the breakdown that is there,
    /// rather than failing: a report missing a chart is worth more than no report, and the
    /// figures that do appear are still the ones the run produced.
    /// </remarks>
    private static RunScore ReadScore(
        JsonElement breakdown,
        (int ComponentsTotal, int FindingsTotal, int NotAssessedCount, decimal TotalLowHours,
         decimal TotalHighHours, decimal FixedCostLowHours, decimal FixedCostHighHours, string BreakdownJson) row)
    {
        if (breakdown.TryGetProperty("score", out var whole))
        {
            var deserialized = whole.Deserialize<RunScore>(Json);
            if (deserialized is not null) return deserialized;
        }

        return new RunScore(
            row.ComponentsTotal,
            Read<Dictionary<string, int>>(breakdown, "byCraft") ?? new Dictionary<string, int>(),

            // Empty rather than falling back to byCraft. A run stored before this existed
            // has no counted breakdown, and byCraft is the wrong denominator: putting it
            // here would redraw the donut this field was added to fix. This path already
            // passes null for the share, so the whole ratio section is degraded together
            // and says so, which is the honest shape for a run nobody can recompute.
            Read<Dictionary<string, int>>(breakdown, "countedByCraft") ?? new Dictionary<string, int>(),
            Read<Dictionary<string, int>>(breakdown, "byDomain") ?? new Dictionary<string, int>(),
            Read<Dictionary<string, int>>(breakdown, "byLifecycle") ?? new Dictionary<string, int>(),
            null,
            Read<string>(breakdown, "ratioDefinition") ?? string.Empty,
            new Dictionary<string, int>(),
            new Dictionary<string, int>(),
            Read<Dictionary<string, decimal>>(breakdown, "debtByDomain") ?? new Dictionary<string, decimal>(),
            row.TotalLowHours,
            row.TotalHighHours,
            row.FixedCostLowHours,
            row.FixedCostHighHours,
            [],
            Read<List<string>>(breakdown, "caveats") ?? []);
    }

    private static DiscoveredComponent ToComponent(AnalysisStore.ComponentRow row) =>
        new(row.ComponentId,
            row.StableKey,
            row.TypeId,
            row.DisplayName,
            row.SchemaName,
            row.PlatformId,
            row.SolutionUniqueName,
            row.IsManaged,
            row.OwnerUpn,
            JsonSerializer.Deserialize<Dictionary<string, object?>>(row.AttributesJson, Json)
                ?? new Dictionary<string, object?>(StringComparer.Ordinal));

    private static Finding ToFinding(AnalysisStore.FindingRow row) =>
        new(row.FindingId,
            row.StableKey,
            row.RuleId,
            row.ComponentTypeId,
            row.ComponentName,
            Enum.TryParse<Severity>(row.Severity, true, out var severity) ? severity : Severity.Info,
            Enum.TryParse<FindingOrigin>(row.Origin, true, out var origin) ? origin : FindingOrigin.Catalogue,
            row.CheckerRuleId,
            JsonSerializer.Deserialize<Dictionary<string, object?>>(row.EvidenceJson, Json)
                ?? new Dictionary<string, object?>(StringComparer.Ordinal));

    private static Estimate ToEstimate(AnalysisStore.FindingRow row) =>
        new(row.LowHours,
            row.HighHours,
            row.StoryPoints,
            Enum.TryParse<Confidence>(row.Confidence, true, out var confidence) ? confidence : Confidence.Low,
            Enum.TryParse<EstimateLayer>(row.Layer, true, out var layer) ? layer : EstimateLayer.BandDefault,
            row.Rationale,
            [],
            row.FlaggedReason);
}
