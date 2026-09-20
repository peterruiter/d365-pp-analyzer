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
/// <param name="OpenSolutionFiles">Every solution file this run can read: the uploaded export, or the chosen solutions exported from the environment. Reports each export as it happens, because it is a minute a solution.</param>
/// <param name="CheckConnections">Authenticates every configured source and says what each reaches.</param>
/// <param name="ListSolutions">Every solution the environment has, whether or not it is in scope.</param>
/// <param name="ReadEnvironment">Reads a live environment, scoped to the chosen solutions.</param>
/// <param name="RecordSolutions">Records what the environment holds, so somebody can choose from it.</param>
/// <param name="ReadSelection">What somebody chose, or null when nobody has been asked yet.</param>
/// <param name="RunChecker">Calls the Power Apps checker.</param>
/// <param name="Estimator">The three layer estimator.</param>
/// <param name="BacklogBuilder">Turns findings into work items.</param>
/// <param name="Publish">Writes to Azure DevOps or Jira, for one run. The only thing here that writes anywhere.</param>
/// <param name="Persist">Where everything is recorded.</param>
/// <param name="FixedCosts">Per engagement costs, from the contract.</param>
/// <param name="Bands">The estimate bands, from the contract.</param>
/// <param name="ComplexityRules">The complexity rules, from the contract.</param>
/// <param name="RoadmapPositions">Where each rule sits on the grid, from the contract.</param>
public sealed record StageServices(
    Func<IReadOnlyList<string>, IProgress<StageNote>, CancellationToken, Task<IReadOnlyList<SolutionFile>>> OpenSolutionFiles,
    Func<CancellationToken, Task<IReadOnlyList<ConnectionCheck>>> CheckConnections,
    Func<CancellationToken, Task<IReadOnlyList<SolutionSummary>>> ListSolutions,
    Func<IReadOnlyList<string>, bool, IProgress<StageNote>, CancellationToken, Task<EnvironmentRead?>> ReadEnvironment,
    Func<Guid, IReadOnlyList<SolutionSummary>, CancellationToken, Task> RecordSolutions,
    Func<Guid, CancellationToken, Task<ChosenScope?>> ReadSelection,
    Func<Stream, CancellationToken, Task<CheckerOutcome>> RunChecker,
    Estimator Estimator,
    BacklogBuilder BacklogBuilder,
    Func<Guid, IReadOnlyList<BacklogItem>, CancellationToken, Task<int>> Publish,
    IRunPersistence Persist,
    IReadOnlyList<FixedCost> FixedCosts,
    IReadOnlyDictionary<string, EstimateBand> Bands,
    IReadOnlyList<ComplexityRule> ComplexityRules,
    IReadOnlyDictionary<string, RoadmapPosition> RoadmapPositions);

/// <summary>
/// One solution's zip, in memory.
/// </summary>
/// <remarks>
/// A way to open it rather than the file itself. Two stages read the same solution — extract
/// unpacks it and the checker submits it — and each opens it when it needs it.
///
/// This was bytes for one afternoon, and bytes meant the whole of a client's estate sat in
/// the worker's memory for the length of a run: every chosen solution at once, held from the
/// extract stage until the checker had finished with it. A dozen small solutions is nothing
/// and a dozen large ones is the worker restarting, which reads as a lost run.
///
/// So the file lives in the storage account, which is where this product already puts an
/// uploaded solution, and both stages read it from there. Opening it twice costs two reads
/// of a blob rather than two exports of a solution.
/// </remarks>
/// <param name="Name">The solution's unique name, or the uploaded file's name.</param>
/// <param name="Open">Opens the zip for reading. Seekable, because a zip is read from its end.</param>
public sealed record SolutionFile(string Name, Func<CancellationToken, Task<Stream>> Open);

/// <summary>
/// What somebody told a paused run to do.
/// </summary>
/// <remarks>
/// The pipeline's own shape rather than the data layer's, because `PowerPete.Analyzer.Pipeline`
/// does not reference the data layer and should not. The worker translates.
/// </remarks>
/// <param name="Solutions">The solutions that were ticked. Empty means none of them.</param>
/// <param name="SolutionChecker">Whether to run Microsoft's checker, null meaning the mode decides.</param>
/// <param name="ModelEstimates">Whether to estimate with a model, null meaning the mode decides.</param>
/// <param name="EnvironmentHealth">Whether to report what the identity reaches, null meaning the mode decides.</param>
/// <param name="ExportSolutions">Whether to export the chosen solutions, null meaning the mode decides.</param>
public sealed record ChosenScope(
    IReadOnlyList<string> Solutions,
    bool? SolutionChecker,
    bool? ModelEstimates,
    bool? EnvironmentHealth,
    bool? ExportSolutions);

/// <summary>
/// One configured source, authenticated.
/// </summary>
/// <param name="Name">What the connection is called on the engagement.</param>
/// <param name="Mode">servicePrincipal, delegated or offlineZip.</param>
/// <param name="Succeeded">Whether it authenticated at all.</param>
/// <param name="Identity">Who it authenticated as. The report carries this.</param>
/// <param name="Message">What to tell somebody, on either outcome.</param>
/// <param name="Reaches">The evidence sources this connection can actually read.</param>
public sealed record ConnectionCheck(
    string Name,
    string Mode,
    bool Succeeded,
    string? Identity,
    string Message,
    IReadOnlyList<EvidenceSource> Reaches);

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

    /// <summary>The solutions the components came out of.</summary>
    /// <remarks>
    /// Saved before the components, because a component resolves its solution by unique name
    /// and one that is not stored yet leaves every component in it unattributed.
    /// </remarks>
    /// <param name="runId">Which run.</param>
    /// <param name="solutions">What was read.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task SaveSolutionsAsync(
        Guid runId,
        IReadOnlyList<SolutionZipReader.SolutionHeader> solutions,
        CancellationToken cancellationToken);

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
/// <summary>
/// Proves every configured source before anything reads one.
/// </summary>
/// <remarks>
/// The contract puts this first and it was not implemented: the connection test happened
/// inside the extract stage, which works and reports the failure one stage later than it
/// happened. That matters because the two failures look different to somebody watching. A
/// run that dies in extract reads as "the estate could not be read"; a run that dies in
/// connect reads as "the credential is wrong", which is what it was.
///
/// It also records the identity. A connection that authenticates with too few privileges
/// fails later in a way that looks exactly like an estate with nothing in it, and the only
/// thing that tells those apart is knowing who the run was made as.
///
/// Writes nothing, per the contract. It is safe to run against a client who has bought
/// nothing yet, which is the point of having it separate.
/// </remarks>
public sealed class ConnectStage(StageServices services) : StageBase(services)
{
    /// <inheritdoc />
    public override string Id => "connect";

    /// <inheritdoc />
    public override async Task<StageOutcome> RunAsync(RunState state, string? checkpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        var checks = await Services.CheckConnections(cancellationToken).ConfigureAwait(false);

        if (checks.Count == 0)
        {
            return StageOutcome.Failed(
                "No source is configured on this engagement. There is nothing to authenticate and nothing to read.");
        }

        foreach (var check in checks)
        {
            state.Identities[check.Name] = check.Identity ?? "unknown";
        }

        var failed = checks.Where(check => !check.Succeeded).ToList();

        if (failed.Count == checks.Count)
        {
            // Every one of them. Naming each rather than saying "the connection failed",
            // because an engagement can have several and the message has to say which.
            return StageOutcome.Failed(
                "No configured source could be reached. " +
                string.Join(" ", failed.Select(check => $"{check.Name}: {check.Message}")));
        }

        // What the reachable connections between them can see. The analysis reads this to
        // decide which rules can run at all, and a rule whose evidence nobody reached is
        // reported as not assessed rather than as passing.
        foreach (var source in checks.Where(check => check.Succeeded).SelectMany(check => check.Reaches))
        {
            state.Reachable.Add(source);
        }

        return failed.Count > 0
            ? StageOutcome.Partial(
                $"{failed.Count} of {checks.Count} sources could not be reached. " +
                string.Join(" ", failed.Select(check => $"{check.Name}: {check.Message}")))
            : StageOutcome.Succeeded();
    }
}

/// <summary>
/// Says what exists before saying what was looked at.
/// </summary>
/// <remarks>
/// A report covering four of nineteen solutions and a report covering all nineteen look
/// identical on the cover page. This is the stage that makes them different.
///
/// Until this existed the total was set to the number of solutions the extraction happened
/// to read, so the two numbers were always equal and the caveat about partial coverage could
/// never fire. The environment is asked what it has, separately from what was read.
///
/// Nothing here narrows the scope yet: every unmanaged solution is analysed, as before. What
/// it adds is the denominator, which is the half that was missing.
/// </remarks>
public sealed class SelectSolutionsStage(StageServices services) : StageBase(services)
{
    /// <inheritdoc />
    public override string Id => "selectSolutions";

    /// <inheritdoc />
    public override async Task<StageOutcome> RunAsync(RunState state, string? checkpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        var solutions = await Services.ListSolutions(cancellationToken).ConfigureAwait(false);

        if (solutions.Count == 0)
        {
            // Not a failure, and nothing to ask. An offline run has no environment to
            // enumerate, and the extract stage will take the solutions out of the file it
            // was given.
            return StageOutcome.Succeeded();
        }

        state.SolutionsTotal = solutions.Count;

        foreach (var solution in solutions)
        {
            state.Available[solution.UniqueName] = solution;
        }

        // Recorded before anybody is asked, so the list survives the pause and so a run that
        // is never resumed still says what the environment held.
        await Services.RecordSolutions(state.RunId, solutions, cancellationToken).ConfigureAwait(false);

        var chosen = await Services.ReadSelection(state.RunId, cancellationToken).ConfigureAwait(false);

        if (chosen is null)
        {
            // The pause. Everything after this reads the environment, and reading it before
            // somebody has said which solutions to read costs the hour the question exists
            // to save, on a report mostly about Microsoft's own solutions.
            var theirs = solutions.Count(solution => !FirstPartySolutions.IsFirstParty(solution));

            return StageOutcome.AwaitingSelection(
                $"{solutions.Count} solutions found, {theirs} of them not Microsoft's. "
                + "Choose which to analyse before the run reads the environment.");
        }

        // Only what is not already there. The worker seeds this from the database before the
        // pipeline starts, because a resumed run skips this stage and extract still needs the
        // scope, so on a first pass both would otherwise put the same names in.
        foreach (var solution in chosen.Solutions)
        {
            if (!state.Chosen.Contains(solution, StringComparer.OrdinalIgnoreCase))
            {
                state.Chosen.Add(solution);
            }
        }

        state.Checks = RunChecks.ForMode(
            state.Mode, chosen.SolutionChecker, chosen.ModelEstimates, chosen.EnvironmentHealth,
            chosen.ExportSolutions);

        if (state.Chosen.Count == 0)
        {
            return StageOutcome.Partial(
                $"None of the {solutions.Count} solutions in this environment were chosen, so nothing was read "
                + "from it. This is an unread estate rather than a clean one.");
        }

        var unmanaged = state.Chosen
            .Where(state.Available.ContainsKey)
            .Count(name => !state.Available[name].IsManaged);

        return unmanaged == 0
            ? StageOutcome.Partial(
                $"All {state.Chosen.Count} chosen solutions are managed. There is no unmanaged "
                + "customisation to analyse, which is either a very disciplined estate or the wrong environment.")
            : StageOutcome.Succeeded();
    }
}

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

        // Every solution this run can read as a file. For an offline engagement that is the
        // one somebody uploaded; for a live one it is each chosen solution, exported.
        var files = await Services
            .OpenSolutionFiles(state.Checks.ExportSolutions ? state.Chosen : [], state.Progress, cancellationToken)
            .ConfigureAwait(false);

        var unpacked = 0;

        foreach (var solutionFile in files)
        {
            state.Progress.Report(new StageNote("unpacking", solutionFile.Name, ++unpacked, files.Count));

            await using (var file = await solutionFile.Open(cancellationToken).ConfigureAwait(false))
            {
                var result = SolutionZipReader.Read(file);

                Merge(state, result.Components);
                state.Links.AddRange(result.Links);
                state.Unresolved.AddRange(result.Unresolved);
                foreach (var header in result.Solutions)
                {
                    state.Solutions[header.UniqueName] = header;
                }

                // Counted across the files rather than the largest of them. A live run reads
                // one file per chosen solution, and taking the maximum would report twelve
                // solutions analysed as one.
                state.SolutionsAnalysed += result.Solutions.Count;

                // Never lower than what was analysed, and never overwriting what the select
                // stage found. An offline run has no environment to enumerate, so the file is
                // the only answer there is; a live run already knows the real denominator and
                // taking the file's count would quietly claim full coverage.
                state.SolutionsTotal = Math.Max(state.SolutionsTotal, state.SolutionsAnalysed);
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

        // Scoped to what somebody chose. This used to read the whole environment whatever the
        // select stage had found, including every solution Microsoft ships, which is both the
        // slowest part of a run and the part that produces findings about somebody else's
        // product.
        var live = await Services
            .ReadEnvironment(state.Chosen, state.Mode != "quickScan", state.Progress, cancellationToken)
            .ConfigureAwait(false);

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

        // Solutions first. The components reference them by unique name.
        await Services.Persist
            .SaveSolutionsAsync(state.RunId, [.. state.Solutions.Values], cancellationToken)
            .ConfigureAwait(false);

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

        // Turned off deliberately is not the same as failed, and it is not the same as
        // passed either. The checker is the slowest part of a run by a wide margin, so it is
        // the one somebody turns off to get an answer before a meeting, and the rules that
        // depend on it stay unassessed rather than quietly reading as clean.
        if (!state.Checks.SolutionChecker)
        {
            return StageOutcome.Partial(
                "The solution checker was turned off for this run. Every rule whose evidence is a checker "
                + "result is reported as not assessed rather than as passing.");
        }

        var files = await Services
            .OpenSolutionFiles(state.Checks.ExportSolutions ? state.Chosen : [], state.Progress, cancellationToken)
            .ConfigureAwait(false);

        if (files.Count == 0)
        {
            // No longer the common case. A live connection exports the solutions it was told
            // to read, so this is an engagement with no source at all rather than one that
            // simply had nothing uploaded.
            return StageOutcome.Partial(
                "There is no solution file to check. An offline engagement needs one uploaded, and a live "
                + "one needs the solution export turned on, or the three checker backed rules stay unassessed.");
        }

        // One submission per solution. The checker takes a file, and a run covering twelve
        // solutions is twelve files: submitting only the first would report the other eleven
        // as checked when nothing looked at them.
        var refusals = new List<string>();
        var submitted = 0;

        foreach (var solutionFile in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Reported before the wait rather than after it. This is the slowest stage there
            // is, and somebody watching it needs to be told which of twelve solutions
            // Microsoft is currently thinking about.
            state.Progress.Report(new StageNote("checking", solutionFile.Name, ++submitted, files.Count));

            await using var file = await solutionFile.Open(cancellationToken).ConfigureAwait(false);

            var each = await Services.RunChecker(file, cancellationToken).ConfigureAwait(false);

            if (each.Succeeded) state.CheckerIssues.AddRange(each.Issues);
            else refusals.Add($"{solutionFile.Name}: {each.FailureReason ?? "the checker did not answer"}");
        }

        if (refusals.Count == files.Count)
        {
            // Never fatal. A checker outage is not a reason to lose the rest of an analysis,
            // and it is absolutely a reason for the report to say so rather than look complete.
            return StageOutcome.Partial(string.Join("; ", refusals));
        }

        if (refusals.Count > 0)
        {
            return StageOutcome.Partial(
                $"{refusals.Count} of {files.Count} solutions were not checked: {string.Join("; ", refusals)}");
        }

        var outcome = new CheckerOutcome(true, state.CheckerIssues, null);

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

        // Two different reasons wear the same words otherwise.
        //
        // "This connection could not reach runtime" is true whether the mode has no runtime
        // access at all or the mode has it and the read failed today. The first is a fact
        // about how the client chose to connect and nothing can be done about it in this
        // run; the second is a fault somebody can go and fix. The connect stage proved which
        // sources were reachable, so where a source was reachable and no data arrived, the
        // reason says so.
        var notAssessed = outcome.NotAssessed
            .Select(entry => Enum.TryParse<EvidenceSource>(entry.MissingEvidence, ignoreCase: true, out var source)
                && state.Reachable.Contains(source)
                && !state.Reached.Contains(source)
                    ? entry with { Reason = NotAssessedReasons.ReadReturnedNothing }
                    : entry)
            .ToList();

        state.NotAssessed.AddRange(notAssessed);
        await Services.Persist.SaveNotAssessedAsync(state.RunId, notAssessed, cancellationToken).ConfigureAwait(false);

        // Findings are carried without estimates until the estimate stage fills them in. A
        // band default stands in so a quick scan, which skips estimation, still has numbers.
        foreach (var finding in outcome.Findings)
        {
            var band = Services.Bands.TryGetValue(finding.Rule?.EstimateBand ?? "none", out var found)
                ? found
                : Services.Bands["none"];

            state.Findings.Add((finding, band.AsEstimate()));
        }

        return notAssessed.Count > 0
            ? StageOutcome.Partial($"{notAssessed.Count} checks could not run and are named in the report.")
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

        // Skipped, not failed. Every finding still gets an estimate: the scorer falls back to
        // the band default for the component type, which is exactly what a quick scan does
        // and what the report then says it did.
        if (!state.Checks.ModelEstimates)
        {
            return StageOutcome.Skipped();
        }

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

        if (state.BacklogHash is null)
        {
            return StageOutcome.Failed("The backlog stage did not run, so there is nothing to publish.");
        }

        var byKey = state.Components.ToDictionary(component => component.StableKey, StringComparer.Ordinal);

        var items = Services.BacklogBuilder.Build(
            [.. state.Findings.Select(entry => (
                entry.Finding,
                entry.Estimate,
                entry.Finding.ComponentKey is null ? null : byKey.GetValueOrDefault(entry.Finding.ComponentKey)))],
            $"Published from run {state.RunId}.");

        var created = await Services.Publish(state.RunId, items, cancellationToken).ConfigureAwait(false);

        return StageOutcome.Succeeded(Checkpoint.Write(new { created }));
    }
}
