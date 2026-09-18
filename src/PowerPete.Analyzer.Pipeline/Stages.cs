namespace PowerPete.Analyzer.Pipeline.Stages;

using System.Text.Json;
using PowerPete.Analyzer.Analysis;
using PowerPete.Analyzer.DevOps;
using PowerPete.Analyzer.Domain;
using PowerPete.Analyzer.Estimation;
using PowerPete.Analyzer.Extraction;

/// <summary>Everything a stage needs that is not the run itself.</summary>
/// <remarks>
/// Passed in rather than resolved, so a stage can be constructed in a test with a fake for
/// one thing and nothing at all for the rest. A stage that reaches into a container is a
/// stage that can only be exercised by starting the product.
/// </remarks>
/// <param name="OpenSolutionFile">Opens the uploaded export, for the offline mode.</param>
/// <param name="ReadEnvironment">Reads a live environment, for the other two.</param>
/// <param name="RunChecker">Calls the Power Apps checker.</param>
/// <param name="Estimator">The three layer estimator.</param>
/// <param name="BacklogBuilder">Turns findings into work items.</param>
/// <param name="Publish">Writes to Azure DevOps. The only thing here that writes anywhere.</param>
/// <param name="Persist">Where everything is recorded.</param>
/// <param name="FixedCosts">Per engagement costs, from the contract.</param>
/// <param name="Bands">The estimate bands, from the contract.</param>
/// <param name="ComplexityRules">The complexity rules, from the contract.</param>
/// <param name="RoadmapPositions">Where each rule sits on the grid, from the contract.</param>
public sealed record StageServices(
    Func<CancellationToken, Task<Stream?>> OpenSolutionFile,
    Func<bool, CancellationToken, Task<EnvironmentRead?>> ReadEnvironment,
    Func<Stream, CancellationToken, Task<CheckerOutcome>> RunChecker,
    Estimator Estimator,
    BacklogBuilder BacklogBuilder,
    Func<IReadOnlyList<BacklogItem>, string, CancellationToken, Task<int>> Publish,
    IRunPersistence Persist,
    IReadOnlyList<FixedCost> FixedCosts,
    IReadOnlyDictionary<string, EstimateBand> Bands,
    IReadOnlyList<ComplexityRule> ComplexityRules,
    IReadOnlyDictionary<string, RoadmapPosition> RoadmapPositions);

/// <summary>What a live read returned.</summary>
/// <param name="Components">Everything found.</param>
/// <param name="Links">Edges it could resolve.</param>
/// <param name="Reads">One record per entity attempted.</param>
/// <param name="Identity">Who it authenticated as.</param>
/// <param name="ReachedRuntime">Whether runtime evidence actually came back, as opposed to having been asked for.</param>
public sealed record EnvironmentRead(
    IReadOnlyList<DiscoveredComponent> Components,
    IReadOnlyList<ComponentLink> Links,
    IReadOnlyList<(string ComponentTypeId, bool Succeeded, int? Count, string? Reason)> Reads,
    string? Identity,
    bool ReachedRuntime);

/// <summary>What the checker returned.</summary>
/// <param name="Succeeded">Whether it ran.</param>
/// <param name="Issues">What it found.</param>
/// <param name="FailureReason">Why not, which goes into a not assessed record for every checker backed rule.</param>
public sealed record CheckerOutcome(bool Succeeded, IReadOnlyList<CheckerIssue> Issues, string? FailureReason);

/// <summary>Where a stage records what it did.</summary>
public interface IRunPersistence
{
    /// <summary>One read attempt, successful or not.</summary>
    /// <param name="runId">Which run.</param>
    /// <param name="componentTypeId">What was read.</param>
    /// <param name="evidenceSource">Where from.</param>
    /// <param name="succeeded">Whether it worked.</param>
    /// <param name="count">How many.</param>
    /// <param name="reason">Why not.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task RecordReadAsync(Guid runId, string componentTypeId, string evidenceSource, bool succeeded, int? count, string? reason, CancellationToken cancellationToken);

    /// <summary>The inventory.</summary>
    /// <param name="runId">Which run.</param>
    /// <param name="components">Everything found.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task SaveComponentsAsync(Guid runId, IReadOnlyList<DiscoveredComponent> components, CancellationToken cancellationToken);

    /// <summary>Findings with their estimates.</summary>
    /// <param name="runId">Which run.</param>
    /// <param name="engagementId">Which engagement.</param>
    /// <param name="findings">The findings.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task SaveFindingsAsync(Guid runId, Guid engagementId, IReadOnlyList<(Finding Finding, Estimate Estimate)> findings, CancellationToken cancellationToken);

    /// <summary>Every rule that could not run.</summary>
    /// <param name="runId">Which run.</param>
    /// <param name="notAssessed">The rules and the reasons.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task SaveNotAssessedAsync(Guid runId, IReadOnlyList<NotAssessed> notAssessed, CancellationToken cancellationToken);

    /// <summary>The score.</summary>
    /// <param name="runId">Which run.</param>
    /// <param name="engagementId">Which engagement.</param>
    /// <param name="score">The numbers.</param>
    /// <param name="customisation">The components by customisation chart.</param>
    /// <param name="roadmap">The roadmap items.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task SaveScoreAsync(Guid runId, Guid engagementId, RunScore score, IReadOnlyList<CustomisationRow> customisation, IReadOnlyList<RoadmapItem> roadmap, CancellationToken cancellationToken);

    /// <summary>The backlog.</summary>
    /// <param name="runId">Which run.</param>
    /// <param name="engagementId">Which engagement.</param>
    /// <param name="items">The items.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task SaveBacklogAsync(Guid runId, Guid engagementId, IReadOnlyList<BacklogItem> items, CancellationToken cancellationToken);

    /// <summary>The hash somebody approved, or null.</summary>
    /// <param name="runId">Which run.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<string?> GetApprovedHashAsync(Guid runId, CancellationToken cancellationToken);
}

/// <summary>Shared plumbing for the stages below.</summary>
/// <param name="services">What the stage needs.</param>
public abstract class StageBase(StageServices services) : IStage
{
    /// <summary>What the stage needs.</summary>
    protected StageServices Services { get; } = services;

    /// <inheritdoc />
    public abstract string Id { get; }

    /// <inheritdoc />
    public virtual bool RunsIn(string mode) => mode is "quickScan" or "assessment" or "compare";

    /// <inheritdoc />
    public abstract Task<StageOutcome> RunAsync(RunState state, string? checkpoint, CancellationToken cancellationToken);
}

/// <summary>
/// Reads the estate, from whichever sources the engagement has.
/// </summary>
/// <remarks>
/// Both modes can be configured at once and both run. What a zip cannot carry comes from the
/// live read, what the live read cannot see in a definition comes from the zip, and a
/// component seen by both is merged on its stable key rather than duplicated.
/// </remarks>
public sealed class ExtractStage(StageServices services) : StageBase(services)
{
    /// <inheritdoc />
    public override string Id => "extract";

    /// <inheritdoc />
    public override async Task<StageOutcome> RunAsync(RunState state, string? checkpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        var failures = 0;
        var attempts = 0;

        await using (var file = await Services.OpenSolutionFile(cancellationToken).ConfigureAwait(false))
        {
            if (file is not null)
            {
                var result = SolutionZipReader.Read(file);

                Merge(state, result.Components);
                state.Links.AddRange(result.Links);
                state.Unresolved.AddRange(result.Unresolved);
                state.SolutionsAnalysed = Math.Max(state.SolutionsAnalysed, result.Solutions.Count);
                state.SolutionsTotal = Math.Max(state.SolutionsTotal, result.Solutions.Count);
                state.Reached.Add(EvidenceSource.SolutionZip);

                foreach (var read in result.Reads)
                {
                    attempts++;
                    if (!read.Succeeded) failures++;

                    await Services.Persist.RecordReadAsync(state.RunId, read.ComponentTypeId, "solutionZip",
                        read.Succeeded, read.RecordCount, read.FailureReason, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        var live = await Services.ReadEnvironment(state.Mode != "quickScan", cancellationToken).ConfigureAwait(false);

        if (live is not null)
        {
            Merge(state, live.Components);
            state.Links.AddRange(live.Links);
            state.Reached.Add(EvidenceSource.Metadata);
            if (live.ReachedRuntime) state.Reached.Add(EvidenceSource.Runtime);

            foreach (var read in live.Reads)
            {
                attempts++;
                if (!read.Succeeded) failures++;

                await Services.Persist.RecordReadAsync(state.RunId, read.ComponentTypeId, "metadata",
                    read.Succeeded, read.Count, read.Reason, cancellationToken).ConfigureAwait(false);
            }
        }

        if (attempts == 0)
        {
            return StageOutcome.Failed(
                "No source was configured. There is no solution file and no environment connection on this engagement, " +
                "so there is nothing to read and nothing to report.");
        }

        await Services.Persist.SaveComponentsAsync(state.RunId, state.Components, cancellationToken).ConfigureAwait(false);

        // A quarter is the line. Below it the report carries on and names what it missed;
        // above it the report would be describing an environment nobody read, and a plausible
        // document is worse than no document.
        if (failures > attempts / 4)
        {
            return StageOutcome.Failed(
                $"{failures} of {attempts} reads failed. That is too much of the estate to be missing for a report " +
                "to describe it. Fix the connection rather than publishing this.");
        }

        return failures > 0
            ? StageOutcome.Partial($"{failures} of {attempts} reads failed. Every rule depending on them is reported as not assessed.")
            : StageOutcome.Succeeded();
    }

    /// <summary>
    /// Adds components, keeping the richer copy where two sources saw the same one.
    /// </summary>
    /// <remarks>
    /// Richer means more filled attributes. A zip carries a flow's definition and a live read
    /// carries its owner and its run history, and taking whichever arrived second would lose
    /// half of whichever it was.
    /// </remarks>
    private static void Merge(RunState state, IReadOnlyList<DiscoveredComponent> incoming)
    {
        var existing = state.Components.ToDictionary(component => component.StableKey, StringComparer.Ordinal);

        foreach (var component in incoming)
        {
            if (!existing.TryGetValue(component.StableKey, out var already))
            {
                state.Components.Add(component);
                existing[component.StableKey] = component;
                continue;
            }

            var merged = new Dictionary<string, object?>(already.Attributes);
            foreach (var (key, value) in component.Attributes)
            {
                if (value is not null) merged[key] = value;
            }

            var replacement = already with
            {
                Attributes = merged,
                OwnerUpn = already.OwnerUpn ?? component.OwnerUpn,
                PlatformId = already.PlatformId ?? component.PlatformId,
                SolutionUniqueName = already.SolutionUniqueName ?? component.SolutionUniqueName
            };

            state.Components[state.Components.IndexOf(already)] = replacement;
            existing[component.StableKey] = replacement;
        }
    }
}

/// <summary>Runs the Power Apps checker.</summary>
public sealed class CheckerStage(StageServices services) : StageBase(services)
{
    /// <inheritdoc />
    public override string Id => "checker";

    /// <summary>Skipped on a quick scan, where the point is an answer in fifteen minutes.</summary>
    /// <param name="mode">The run mode.</param>
    public override bool RunsIn(string mode) => mode is "assessment" or "compare";

    /// <inheritdoc />
    public override async Task<StageOutcome> RunAsync(RunState state, string? checkpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        await using var file = await Services.OpenSolutionFile(cancellationToken).ConfigureAwait(false);

        if (file is null)
        {
            return StageOutcome.Partial(
                "The checker needs a solution file and this engagement has only a live connection. " +
                "Export one and re-run, or the three checker backed rules stay unassessed.");
        }

        var outcome = await Services.RunChecker(file, cancellationToken).ConfigureAwait(false);

        if (!outcome.Succeeded)
        {
            // Never fatal. A checker outage is not a reason to lose the rest of an analysis,
            // and it is absolutely a reason for the report to say so rather than look complete.
            return StageOutcome.Partial(outcome.FailureReason ?? "The checker did not answer.");
        }

        state.CheckerIssues.AddRange(outcome.Issues);
        state.Reached.Add(EvidenceSource.Checker);

        return StageOutcome.Succeeded();
    }
}

/// <summary>Builds the reference graph.</summary>
public sealed class ResolveStage(StageServices services) : StageBase(services)
{
    /// <inheritdoc />
    public override string Id => "resolve";

    /// <inheritdoc />
    public override Task<StageOutcome> RunAsync(RunState state, string? checkpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        var result = ReferenceResolver.Resolve(state.Components, state.Links);

        state.Links.Clear();
        state.Links.AddRange(result.Links);
        state.Unresolved.Clear();
        state.Unresolved.AddRange(result.Unresolved);

        return Task.FromResult(StageOutcome.Succeeded());
    }
}

/// <summary>Applies every rule.</summary>
public sealed class AnalyseStage(StageServices services, RuleEngine engine) : StageBase(services)
{
    /// <inheritdoc />
    public override string Id => "analyse";

    /// <inheritdoc />
    public override async Task<StageOutcome> RunAsync(RunState state, string? checkpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        var context = new AnalysisContext(
            state.Components,
            state.Links,
            state.Unresolved,
            new Reach(state.Reached),
            state.EnvironmentRole,
            state.CheckerIssues,
            state.SolutionsTotal > 0 ? 1 : 0);

        var outcome = engine.Run(context);

        state.NotAssessed.AddRange(outcome.NotAssessed);
        await Services.Persist.SaveNotAssessedAsync(state.RunId, outcome.NotAssessed, cancellationToken).ConfigureAwait(false);

        // Findings are carried without estimates until the estimate stage fills them in. A
        // band default stands in so a quick scan, which skips estimation, still has numbers.
        foreach (var finding in outcome.Findings)
        {
            var band = Services.Bands.TryGetValue(finding.Rule?.EstimateBand ?? "none", out var found)
                ? found
                : Services.Bands["none"];

            state.Findings.Add((finding, band.AsEstimate()));
        }

        return outcome.NotAssessed.Count > 0
            ? StageOutcome.Partial($"{outcome.NotAssessed.Count} checks could not run and are named in the report.")
            : StageOutcome.Succeeded();
    }
}

/// <summary>Estimates every finding, one at a time.</summary>
public sealed class EstimateStage(StageServices services) : StageBase(services)
{
    /// <inheritdoc />
    public override string Id => "estimate";

    /// <summary>A quick scan runs on band defaults, which is what makes it quick.</summary>
    /// <param name="mode">The run mode.</param>
    public override bool RunsIn(string mode) => mode is "assessment" or "compare";

    /// <inheritdoc />
    public override async Task<StageOutcome> RunAsync(RunState state, string? checkpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        // Findings already estimated on a previous attempt are not estimated again. A resumed
        // run must not pay a second time for the same model calls.
        var done = Checkpoint.Read<HashSet<string>>(checkpoint) ?? [];
        var byKey = state.Components.ToDictionary(component => component.StableKey, StringComparer.Ordinal);
        var failures = 0;

        for (var index = 0; index < state.Findings.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (finding, current) = state.Findings[index];
            if (done.Contains(finding.StableKey)) continue;
            if (finding.Rule?.WorkItemType == "none") continue;

            try
            {
                var component = finding.ComponentKey is null ? null : byKey.GetValueOrDefault(finding.ComponentKey);
                var result = await Services.Estimator.EstimateAsync(finding, component, cancellationToken).ConfigureAwait(false);
                state.Findings[index] = (finding, result.Estimate);
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
            {
                // The band default already in place stays. A finding with no number at all
                // would drop out of the total silently.
                failures++;
                state.Findings[index] = (finding, current with
                {
                    FlaggedReason = $"The estimate call failed: {exception.Message} This is the band default."
                });
            }

            done.Add(finding.StableKey);
        }

        return failures > 0
            ? new StageOutcome("partial", Checkpoint.Write(done), $"{failures} estimates fell back to their band.")
            : StageOutcome.Succeeded(Checkpoint.Write(done));
    }
}

/// <summary>Computes the score, the customisation chart and the roadmap.</summary>
public sealed class ScoreStage(StageServices services) : StageBase(services)
{
    /// <inheritdoc />
    public override string Id => "score";

    /// <inheritdoc />
    public override async Task<StageOutcome> RunAsync(RunState state, string? checkpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        await Services.Persist.SaveFindingsAsync(state.RunId, state.EngagementId, state.Findings, cancellationToken)
            .ConfigureAwait(false);

        var score = Scorer.Score(
            state.Components,
            state.Findings,
            state.NotAssessed,
            Services.FixedCosts,
            state.SolutionsAnalysed,
            state.SolutionsTotal);

        var customisation = new ComplexityRater(Services.ComplexityRules).ByCustomisation(state.Components);
        var roadmap = new RoadmapBuilder(Services.RoadmapPositions).Build(state.Findings);

        await Services.Persist.SaveScoreAsync(state.RunId, state.EngagementId, score, customisation, roadmap.Items, cancellationToken)
            .ConfigureAwait(false);

        return roadmap.Unplaced.Count > 0
            ? StageOutcome.Partial($"{roadmap.Unplaced.Count} rules have findings and no roadmap position: {string.Join(", ", roadmap.Unplaced)}.")
            : StageOutcome.Succeeded();
    }
}

/// <summary>Builds the backlog and the hash an approval binds to.</summary>
public sealed class BacklogStage(StageServices services) : StageBase(services)
{
    /// <inheritdoc />
    public override string Id => "backlog";

    /// <inheritdoc />
    public override async Task<StageOutcome> RunAsync(RunState state, string? checkpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        var byKey = state.Components.ToDictionary(component => component.StableKey, StringComparer.Ordinal);

        var context = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"Produced by the Power Platform Solution Analyzer, run {state.RunId}, {DateTime.UtcNow:yyyy-MM-dd}.");

        var items = Services.BacklogBuilder.Build(
            [.. state.Findings.Select(entry => (
                entry.Finding,
                entry.Estimate,
                entry.Finding.ComponentKey is null ? null : byKey.GetValueOrDefault(entry.Finding.ComponentKey)))],
            context);

        await Services.Persist.SaveBacklogAsync(state.RunId, state.EngagementId, items, cancellationToken).ConfigureAwait(false);

        state.BacklogHash = BacklogHash.Of(items.Select(item =>
            (item.Key, item.Title, item.AcceptanceCriteria, item.LowHours ?? 0, item.HighHours ?? 0, item.StoryPoints)));

        return StageOutcome.Succeeded();
    }
}

/// <summary>
/// Writes the approved backlog into Azure DevOps.
/// </summary>
/// <remarks>
/// The only stage in the product that writes outside its own database, and the only one that
/// runs in the publish mode. It refuses without an approval whose hash matches the backlog in
/// front of it, which is the whole reason the hash exists.
/// </remarks>
public sealed class PublishStage(StageServices services) : StageBase(services)
{
    /// <inheritdoc />
    public override string Id => "publish";

    /// <inheritdoc />
    public override bool RunsIn(string mode) => mode == "publish";

    /// <inheritdoc />
    public override async Task<StageOutcome> RunAsync(RunState state, string? checkpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        var approved = await Services.Persist.GetApprovedHashAsync(state.RunId, cancellationToken).ConfigureAwait(false);

        if (approved is null)
        {
            return StageOutcome.Failed(
                "Nobody has approved this backlog. Approval is a row inserted by a named person against the exact " +
                "backlog they were looking at, and there is no setting that skips it.");
        }

        if (state.BacklogHash is null)
        {
            return StageOutcome.Failed("The backlog stage did not run, so there is nothing to compare the approval against.");
        }

        var byKey = state.Components.ToDictionary(component => component.StableKey, StringComparer.Ordinal);

        var items = Services.BacklogBuilder.Build(
            [.. state.Findings.Select(entry => (
                entry.Finding,
                entry.Estimate,
                entry.Finding.ComponentKey is null ? null : byKey.GetValueOrDefault(entry.Finding.ComponentKey)))],
            $"Published from run {state.RunId}.");

        var created = await Services.Publish(items, approved, cancellationToken).ConfigureAwait(false);

        return StageOutcome.Succeeded(Checkpoint.Write(new { created }));
    }
}
