namespace PowerPete.Analyzer.Pipeline;

using System.Text.Json;
using PowerPete.Analyzer.Analysis;
using PowerPete.Analyzer.Data;
using PowerPete.Analyzer.DevOps;
using PowerPete.Analyzer.Domain;
using PowerPete.Analyzer.Extraction;
using PowerPete.Analyzer.Pipeline.Stages;

/// <summary>Writes what a run found.</summary>
public sealed class StorePersistence(AnalysisStore analysis, WorkspaceStore workspace) : IRunPersistence
{
    /// <inheritdoc />
    public Task RecordReadAsync(Guid runId, string componentTypeId, string evidenceSource, bool succeeded, int? count, string? reason, CancellationToken cancellationToken) =>
        analysis.RecordEntityReadAsync(runId, componentTypeId, evidenceSource, succeeded, count, reason, cancellationToken);

    /// <inheritdoc />
    public Task SaveSolutionsAsync(
        Guid runId,
        IReadOnlyList<SolutionZipReader.SolutionHeader> solutions,
        CancellationToken cancellationToken) =>
        analysis.WriteSolutionsAsync(runId,
            [.. solutions.Select(solution => new AnalysisStore.SolutionRow(
                solution.UniqueName,
                solution.FriendlyName,
                solution.Version,
                solution.IsManaged,
                solution.PublisherPrefix,
                solution.PublisherName))],
            cancellationToken);

    /// <inheritdoc />
    public Task SaveComponentsAsync(Guid runId, IReadOnlyList<DiscoveredComponent> components, CancellationToken cancellationToken) =>
        analysis.WriteComponentsAsync(runId,
            [.. components.Select(component => (
                component.ComponentId,
                component.StableKey,
                component.TypeId,
                component.DisplayName,
                component.SchemaName,
                component.PlatformId,
                component.SolutionUniqueName,
                component.Type?.Craft.ToString().ToLowerInvariant() ?? "config",
                component.Type?.Lifecycle.ToString().ToLowerInvariant() ?? "current",
                component.Type?.Domain ?? "platform",
                component.IsManaged,
                component.Attribute<bool?>("isCustom") ?? true,
                component.OwnerUpn,
                JsonSerializer.Serialize(component.Attributes)))],
            cancellationToken);

    /// <inheritdoc />
    public Task SaveFindingsAsync(Guid runId, Guid engagementId, IReadOnlyList<(Finding Finding, Estimate Estimate)> findings, CancellationToken cancellationToken) =>
        analysis.WriteFindingsAsync(runId, engagementId,
            [.. findings.Select(entry => (
                entry.Finding.FindingId,
                entry.Finding.StableKey,
                entry.Finding.RuleId,
                entry.Finding.ComponentKey,
                entry.Finding.Severity.ToString().ToLowerInvariant(),
                entry.Finding.Rule?.Category ?? "quality",
                entry.Finding.Origin.ToString().ToLowerInvariant(),
                entry.Finding.CheckerRuleId,
                JsonSerializer.Serialize(entry.Finding.Evidence),
                entry.Estimate.LowHours,
                entry.Estimate.HighHours,
                entry.Estimate.StoryPoints,
                entry.Estimate.Confidence.ToString().ToLowerInvariant(),
                Layer(entry.Estimate.Layer),
                entry.Estimate.Rationale,
                entry.Estimate.Assumptions.Count == 0 ? null : JsonSerializer.Serialize(entry.Estimate.Assumptions),
                entry.Estimate.FlaggedReason))],
            cancellationToken);

    /// <inheritdoc />
    public Task SaveNotAssessedAsync(Guid runId, IReadOnlyList<NotAssessed> notAssessed, CancellationToken cancellationToken) =>
        analysis.WriteNotAssessedAsync(runId,
            [.. notAssessed.Select(entry => (entry.RuleId, entry.Reason, entry.MissingEvidence))],
            cancellationToken);

    /// <inheritdoc />
    public Task SaveScoreAsync(Guid runId, Guid engagementId, RunScore score, IReadOnlyList<CustomisationRow> customisation, IReadOnlyList<RoadmapItem> roadmap, CancellationToken cancellationToken) =>
        analysis.WriteScoreAsync(runId, engagementId,
            (0, 0, score.ComponentsTotal,
             score.ByCraft.GetValueOrDefault("lowCode"),
             score.ByCraft.GetValueOrDefault("proCode"),
             score.ByCraft.GetValueOrDefault("external"),
             score.ByCraft.GetValueOrDefault("config"),
             score.ByCraft.GetValueOrDefault("content"),
             score.ByLifecycle.Where(pair => pair.Key != "current").Sum(pair => pair.Value),
             score.FindingsBySeverity.Values.Sum(),
             score.FindingsBySeverity.GetValueOrDefault("critical"),
             score.FindingsBySeverity.GetValueOrDefault("high"),
             score.NotAssessed.Count,
             score.TotalLowHours, score.TotalHighHours, score.FixedCostLowHours, score.FixedCostHighHours),
            new { score, customisation, roadmap },
            cancellationToken);

    /// <inheritdoc />
    public Task SaveBacklogAsync(Guid runId, Guid engagementId, IReadOnlyList<BacklogItem> items, CancellationToken cancellationToken)
    {
        // Parents before children, and the key mapping built as we go, because the database
        // stores a parent as a foreign key and the builder produces one as a string key.
        var ids = items.ToDictionary(item => item.Key, _ => Guid.NewGuid(), StringComparer.Ordinal);

        return analysis.WriteBacklogAsync(runId, engagementId,
            [.. items.Select(item => (
                ids[item.Key],
                item.ParentKey is not null && ids.TryGetValue(item.ParentKey, out var parent) ? parent : (Guid?)null,
                item.Type,
                item.Title,
                item.DescriptionHtml,
                item.AcceptanceCriteria,
                item.TestRequirement,
                item.Priority,
                item.StoryPoints,
                item.LowHours,
                item.HighHours,
                JsonSerializer.Serialize(item.Tags),
                item.Key))],
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<string?> GetApprovedHashAsync(Guid runId, CancellationToken cancellationToken) =>
        workspace.GetApprovedHashAsync(runId, cancellationToken);

    private static string Layer(EstimateLayer layer) => layer switch
    {
        EstimateLayer.EngagementOverride => "engagementOverride",
        EstimateLayer.Model => "model",
        _ => "bandDefault"
    };
}

