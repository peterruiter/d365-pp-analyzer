namespace PowerPete.Analyzer.Pipeline;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PowerPete.Analyzer.Domain;
using PowerPete.Analyzer.Extraction;
using PowerPete.Analyzer.Pipeline.Stages;

/// <summary>What a stage is allowed to do and what it leaves behind.</summary>
/// <remarks>
/// Mutable and passed down the pipeline, because each stage builds on the last and copying a
/// whole estate between stages to stay immutable would cost more than it buys.
/// </remarks>
public sealed class RunState
{
    /// <summary>Which run.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Which engagement.</summary>
    public required Guid EngagementId { get; init; }

    /// <summary>quickScan, assessment, publish or compare.</summary>
    public required string Mode { get; init; }

    /// <summary>What the environment is for. Several rules only fire against production.</summary>
    public string EnvironmentRole { get; set; } = "unknown";

    /// <summary>Everything found.</summary>
    public List<DiscoveredComponent> Components { get; } = [];

    /// <summary>Edges between them.</summary>
    public List<ComponentLink> Links { get; } = [];

    /// <summary>Edges pointing outside the inventory.</summary>
    public List<UnresolvedLink> Unresolved { get; } = [];

    /// <summary>What the checker returned.</summary>
    public List<CheckerIssue> CheckerIssues { get; } = [];

    /// <summary>Which evidence sources were actually reached.</summary>
    public HashSet<EvidenceSource> Reached { get; } = [];

    /// <summary>
    /// Which evidence sources the connections say they can reach.
    /// </summary>
    /// <remarks>
    /// What the connect stage proved, as opposed to what the extract stage got back. The two
    /// differ when a source is reachable and returns nothing, and the difference is the
    /// distinction between an estate with no plug-ins and a privilege nobody granted.
    /// </remarks>
    public HashSet<EvidenceSource> Reachable { get; } = [];

    /// <summary>Who each connection authenticated as, by connection name.</summary>
    /// <remarks>
    /// Carried into the report. A run made as a system administrator is not evidence that a
    /// least privileged integration could have made it, and the only way a reader can tell
    /// is if the document says who it ran as.
    /// </remarks>
    public Dictionary<string, string> Identities { get; } = new(StringComparer.Ordinal);

    /// <summary>Every solution the environment has, whether or not it was analysed.</summary>
    public Dictionary<string, SolutionSummary> Available { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The solutions somebody chose to read, once they have.
    /// </summary>
    /// <remarks>
    /// Empty until the selectSolutions stage has an answer, and an answer of "none" is
    /// possible and legitimate: an offline run has no environment to choose from and reads
    /// the file it was given.
    /// </remarks>
    public List<string> Chosen { get; } = [];

    /// <summary>Which optional checks this run was told to do, resolved against the mode.</summary>
    public RunChecks Checks { get; set; } = RunChecks.ForMode("assessment");

    /// <summary>Findings with their estimates.</summary>
    public List<(Finding Finding, Estimate Estimate)> Findings { get; } = [];

    /// <summary>Rules that could not run, with the reason.</summary>
    public List<NotAssessed> NotAssessed { get; } = [];

    /// <summary>How many solutions were in scope, and how many exist.</summary>
    /// <summary>
    /// The solutions the run read, by unique name.
    /// </summary>
    /// <remarks>
    /// Kept rather than counted. A component resolves its solution by unique name when it is
    /// stored, and a report that cannot say which solution a finding is in is one nobody can
    /// act on: somebody has to know whose thing it is before they can change it.
    /// </remarks>
    public Dictionary<string, SolutionZipReader.SolutionHeader> Solutions { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public int SolutionsAnalysed { get; set; }

    /// <summary>How many solutions exist. A different number from the above is a caveat, not a footnote.</summary>
    public int SolutionsTotal { get; set; }

    /// <summary>The hash an approval binds to, once the backlog exists.</summary>
    public string? BacklogHash { get; set; }
}

/// <summary>
/// Which optional checks a run does, after the mode's default and anybody's override.
/// </summary>
/// <remarks>
/// Resolved once, at the top of the run, rather than asked per stage. Two stages working out
/// the same answer from a mode string and a nullable override is two places to get it wrong,
/// and the way it would go wrong is a report that says it ran the checker and did not.
/// </remarks>
/// <param name="SolutionChecker">Microsoft's static analysis.</param>
/// <param name="ModelEstimates">Estimates with a rationale rather than band defaults.</param>
/// <param name="EnvironmentHealth">What the identity can actually read.</param>
public sealed record RunChecks(bool SolutionChecker, bool ModelEstimates, bool EnvironmentHealth)
{
    /// <summary>What a mode does when nobody has said otherwise.</summary>
    /// <remarks>
    /// Mirrors the modes in analysis-stages.json. A contract test holds the two together,
    /// because a default that drifts here silently changes what every run does.
    /// </remarks>
    /// <param name="mode">quickScan, assessment, publish or compare.</param>
    public static RunChecks ForMode(string mode) => mode switch
    {
        "quickScan" => new RunChecks(SolutionChecker: false, ModelEstimates: false, EnvironmentHealth: true),
        "publish" => new RunChecks(SolutionChecker: false, ModelEstimates: false, EnvironmentHealth: false),
        _ => new RunChecks(SolutionChecker: true, ModelEstimates: true, EnvironmentHealth: true),
    };

    /// <summary>The same, with anything somebody chose applied over the top.</summary>
    /// <param name="mode">The run mode.</param>
    /// <param name="solutionChecker">Their answer, or null to keep the mode's.</param>
    /// <param name="modelEstimates">Their answer, or null to keep the mode's.</param>
    /// <param name="environmentHealth">Their answer, or null to keep the mode's.</param>
    public static RunChecks ForMode(
        string mode, bool? solutionChecker, bool? modelEstimates, bool? environmentHealth)
    {
        var defaults = ForMode(mode);

        return new RunChecks(
            solutionChecker ?? defaults.SolutionChecker,
            modelEstimates ?? defaults.ModelEstimates,
            environmentHealth ?? defaults.EnvironmentHealth);
    }
}

/// <summary>How a stage ended.</summary>
/// <param name="Status">succeeded, partial, failed, skipped or awaitingSelection.</param>
/// <param name="Checkpoint">Whatever it needs to resume, as JSON.</param>
/// <param name="Error">Why it failed.</param>
public sealed record StageOutcome(string Status, string? Checkpoint = null, string? Error = null)
{
    /// <summary>It worked.</summary>
    public static StageOutcome Succeeded(string? checkpoint = null) => new("succeeded", checkpoint);

    /// <summary>
    /// It worked in part.
    /// </summary>
    /// <remarks>
    /// Its own status rather than a success with a note. A run that reached three quarters of
    /// an estate and a run that reached all of it must not look the same on a screen, because
    /// the report from the first one is missing things nobody will notice.
    /// </remarks>
    /// <param name="reason">What was missed.</param>
    public static StageOutcome Partial(string reason) => new("partial", null, reason);

    /// <summary>It did not work.</summary>
    /// <param name="reason">Why.</param>
    public static StageOutcome Failed(string reason) => new("failed", null, reason);

    /// <summary>It was not needed in this mode.</summary>
    public static StageOutcome Skipped() => new("skipped");

    /// <summary>
    /// It did its work and the run cannot go on until somebody answers.
    /// </summary>
    /// <remarks>
    /// Not a success and not a failure. A success would count as completed, and a run resumed
    /// afterwards would skip the stage and carry on with the question still unanswered, which
    /// is the whole thing this is here to prevent.
    /// </remarks>
    /// <param name="reason">What is being asked, in a sentence a person reads on the screen.</param>
    public static StageOutcome AwaitingSelection(string reason) => new("awaitingSelection", null, reason);
}

/// <summary>
/// The stages this pipeline runs, in order, without building any of them.
/// </summary>
/// <remarks>
/// Needed by two callers who cannot build a stage. The worker needs it to run a stage again,
/// which means forgetting everything recorded after it and therefore knowing what "after"
/// means. The API needs it to draw the timeline before a run has reached anything.
///
/// Here rather than in either of them, because a second copy of the pipeline's order is a
/// second copy to fall out of step, and the way it would fall out of step is a retry that
/// discards the wrong stages. A test holds this against the array the worker builds and
/// against the contract that declares them.
/// </remarks>
public static class PipelineOrder
{
    /// <summary>Every stage the worker runs, in the order it runs them.</summary>
    public static IReadOnlyList<string> Stages { get; } =
    [
        "connect", "selectSolutions", "extract", "checker", "resolve",
        "analyse", "estimate", "score", "backlog", "publish",
    ];
}

/// <summary>One stage of the pipeline.</summary>
public interface IStage
{
    /// <summary>Its identifier, matching analysis-stages.json.</summary>
    string Id { get; }

    /// <summary>Whether this stage runs in this mode.</summary>
    /// <param name="mode">The run mode.</param>
    bool RunsIn(string mode);

    /// <summary>
    /// Does the work.
    /// </summary>
    /// <param name="state">What the pipeline has so far. The stage adds to it.</param>
    /// <param name="checkpoint">What a previous attempt left behind, or null.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<StageOutcome> RunAsync(RunState state, string? checkpoint, CancellationToken cancellationToken);
}

/// <summary>Where a runner writes what it did, so it does not depend on the data layer.</summary>
public interface IRunJournal
{
    /// <summary>Records a run's status.</summary>
    /// <param name="runId">Which run.</param>
    /// <param name="status">Where it is.</param>
    /// <param name="failure">Why, on a failure.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task SetRunStatusAsync(Guid runId, string status, string? failure, CancellationToken cancellationToken);

    /// <summary>Records a stage's status and checkpoint.</summary>
    /// <param name="runId">Which run.</param>
    /// <param name="stageId">Which stage.</param>
    /// <param name="status">Where it is.</param>
    /// <param name="failure">Why, on a failure.</param>
    /// <param name="checkpoint">What it needs to resume.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task SetStageAsync(Guid runId, string stageId, string status, string? failure, string? checkpoint, CancellationToken cancellationToken);

    /// <summary>Which stages already succeeded, with their checkpoints.</summary>
    /// <param name="runId">Which run.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<IReadOnlyDictionary<string, string?>> GetCompletedStagesAsync(Guid runId, CancellationToken cancellationToken);
}

/// <summary>
/// Runs the stages in order.
/// </summary>
/// <remarks>
/// Three things make this more than a for loop.
///
/// A stage that already succeeded is skipped on a resume, so a run that died during a twenty
/// minute checker job does not start again from the extraction.
///
/// A stage that fails is fatal or it is not, and which one is declared in the contract rather
/// than decided here. An extraction that lost some entities carries on and the report says
/// what it missed; a connection that could not authenticate stops, because everything after it
/// would describe an estate nobody read.
///
/// And a run where anything came back partial ends as partial rather than succeeded. A screen
/// showing a green tick over a three quarter extraction is how a client is told their estate
/// is clean when nobody finished looking at it.
/// </remarks>
public sealed class PipelineRunner(IReadOnlyList<IStage> stages, IRunJournal journal, IReadOnlyDictionary<string, bool> fatalByStage)
{
    /// <summary>How a run ended.</summary>
    /// <param name="Status">succeeded, partial, failed, cancelled or awaitingSelection.</param>
    /// <param name="StagesRun">How many stages did work.</param>
    /// <param name="Error">Why it failed.</param>
    public sealed record Outcome(string Status, int StagesRun, string? Error);

    /// <summary>Runs one pass.</summary>
    /// <param name="state">The run.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<Outcome> RunAsync(RunState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        await journal.SetRunStatusAsync(state.RunId, "running", null, cancellationToken).ConfigureAwait(false);

        var completed = await journal.GetCompletedStagesAsync(state.RunId, cancellationToken).ConfigureAwait(false);
        var ran = 0;
        var anyPartial = false;

        foreach (var stage in stages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!stage.RunsIn(state.Mode))
            {
                await journal.SetStageAsync(state.RunId, stage.Id, "skipped", null, null, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (completed.ContainsKey(stage.Id)) continue;

            await journal.SetStageAsync(state.RunId, stage.Id, "running", null, null, cancellationToken).ConfigureAwait(false);

            StageOutcome outcome;

            try
            {
                completed.TryGetValue(stage.Id, out var checkpoint);
                outcome = await stage.RunAsync(state, checkpoint, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await journal.SetStageAsync(state.RunId, stage.Id, "cancelled", null, null, cancellationToken).ConfigureAwait(false);
                await journal.SetRunStatusAsync(state.RunId, "cancelled", null, cancellationToken).ConfigureAwait(false);
                throw;
            }
#pragma warning disable CA1031 // A stage throwing anything at all must not lose the run's record of where it got to.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                outcome = StageOutcome.Failed(exception.Message);
            }

            await journal.SetStageAsync(state.RunId, stage.Id, outcome.Status, outcome.Error, outcome.Checkpoint, cancellationToken)
                .ConfigureAwait(false);

            if (outcome.Status == "awaitingSelection")
            {
                // Returned rather than continued. Everything after this reads an environment,
                // and reading it before somebody has said which solutions to read would make
                // the question pointless and cost the hour the question exists to save.
                //
                // Not an error, and deliberately not "partial" either: nothing went wrong and
                // nothing was missed. The run is waiting, and a screen showing it as anything
                // else would have somebody hunting a fault that is not there.
                await journal.SetRunStatusAsync(state.RunId, "awaitingSelection", null, cancellationToken)
                    .ConfigureAwait(false);

                return new Outcome("awaitingSelection", ran, null);
            }

            if (outcome.Status == "failed")
            {
                // Fatal comes from the contract. The pipeline does not get an opinion about
                // which failures are survivable, because that opinion would then exist in two
                // places and they would disagree.
                if (fatalByStage.TryGetValue(stage.Id, out var fatal) && fatal)
                {
                    await journal.SetRunStatusAsync(state.RunId, "failed", outcome.Error, cancellationToken).ConfigureAwait(false);
                    return new Outcome("failed", ran, outcome.Error);
                }

                anyPartial = true;
                continue;
            }

            if (outcome.Status == "partial") anyPartial = true;
            ran++;
        }

        var status = anyPartial ? "partial" : "succeeded";
        await journal.SetRunStatusAsync(state.RunId, status, null, cancellationToken).ConfigureAwait(false);

        return new Outcome(status, ran, null);
    }
}

/// <summary>
/// The hash an approval binds to.
/// </summary>
/// <remarks>
/// Computed from what a person would have read on the screen: the items, their titles, their
/// estimates and their acceptance criteria, in a fixed order. Deliberately not from the
/// database rows, which carry identifiers that change between runs and would make every
/// re-approval necessary for no reason a person could see.
///
/// Reordering the backlog changes nothing, because the input is sorted. Changing a title, an
/// estimate or a criterion changes everything, because those are the things somebody was
/// approving.
/// </remarks>
public static class BacklogHash
{
    /// <summary>Hashes a backlog.</summary>
    /// <param name="items">Each item as (key, title, criteria, low, high, points).</param>
    public static string Of(IEnumerable<(string Key, string Title, string Criteria, decimal Low, decimal High, int? Points)> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var canonical = items
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{item.Key}|{item.Title}|{item.Criteria}|{item.Low}|{item.High}|{item.Points}"));

        var joined = string.Join("\n", canonical);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined))).ToLowerInvariant();
    }
}

/// <summary>
/// A checkpoint, as JSON.
/// </summary>
/// <remarks>
/// A stage decides its own shape. The extraction records which entities it finished, the
/// checker records the job it is waiting on, and the estimator records which findings already
/// have a number so a resumed run does not pay for the same model calls twice.
/// </remarks>
public static class Checkpoint
{
    /// <summary>Writes one.</summary>
    /// <typeparam name="T">Whatever the stage needs.</typeparam>
    /// <param name="value">The state.</param>
    public static string Write<T>(T value) => JsonSerializer.Serialize(value);

    /// <summary>Reads one, or the default when there is nothing to resume from.</summary>
    /// <typeparam name="T">Whatever the stage needs.</typeparam>
    /// <param name="json">What was recorded.</param>
    public static T? Read<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;

        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException)
        {
            // A checkpoint written by an older version of a stage is not a reason to fail a
            // run. Starting the stage again is slower and correct.
            return default;
        }
    }
}
