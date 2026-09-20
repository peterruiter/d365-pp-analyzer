namespace PowerPete.Analyzer.Jobs;

using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using PowerPete.Analyzer.Analysis;
using PowerPete.Analyzer.Data;
using PowerPete.Analyzer.Dataverse;
using PowerPete.Analyzer.DevOps;
using PowerPete.Analyzer.Domain;
using PowerPete.Analyzer.Estimation;
using PowerPete.Analyzer.Pipeline;
using PowerPete.Analyzer.Pipeline.Stages;


/// <summary>
/// Assembles everything a run needs from its connections.
/// </summary>
/// <remarks>
/// The composition root proper. Every argument it produces is a closure over one connection,
/// so a stage never sees a credential and never chooses one.
/// </remarks>
public sealed class StageServicesFactory(
    ConnectionFactory connections,
    AnalysisStore analysis,
    WorkspaceStore workspace,
    WorkerSettings settings)
{
    /// <summary>
    /// Where a run's export of one solution is kept.
    /// </summary>
    /// <remarks>
    /// Under a prefix the whole run shares, so deleting the run deletes its files with one
    /// call, and so the lifecycle rule on the account can find them. The unique name is
    /// escaped because a solution's name is a publisher's to choose and a blob path treats a
    /// slash as a folder.
    /// </remarks>
    /// <param name="runId">The run.</param>
    /// <param name="uniqueName">The solution.</param>
    public static string ExportBlobName(Guid runId, string uniqueName) =>
        $"exports/{runId}/{Uri.EscapeDataString(uniqueName)}.zip";

    /// <summary>Opens a blob that is expected to be there.</summary>
    /// <remarks>
    /// Every caller has already established that it exists, so absence here is the file
    /// having gone between one stage and the next. That is a failure worth a sentence
    /// somebody can act on rather than a null reference two frames further in.
    /// </remarks>
    /// <param name="container">The container.</param>
    /// <param name="blobName">The blob.</param>
    private static Func<CancellationToken, Task<Stream>> OpenOrFail(Uri? container, string blobName) =>
        async token => await ConnectionFactory.OpenUploadAsync(container, blobName, token).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"The solution file '{blobName}' is no longer in the storage account. It was there when this run "
                + "started reading, so it has been removed since. Run the analysis again.");

    /// <summary>Builds the services for one run.</summary>
    /// <param name="runId">Which run. Names the folder its exported solutions are written to.</param>
    /// <param name="engagementId">Which engagement.</param>
    /// <param name="engagementName">Its name, for the work item tags.</param>
    /// <param name="source">What to read. Null for an engagement with no live connection.</param>
    /// <param name="target">Where to publish. Null on everything but a publish run.</param>
    /// <param name="criteria">The acceptance criteria contract.</param>
    /// <param name="backlogLanguage">
    /// The language the work items are written in, which is the language of the team who will
    /// pick them up and is not always the language of the report.
    /// </param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<StageServices> BuildAsync(
        Guid runId,
        Guid engagementId,
        string engagementName,
        Connection? source,
        Connection? target,
        IReadOnlyDictionary<string, Criterion> criteria,
        string? backlogLanguage,
        CancellationToken cancellationToken)
    {
        var uploads = settings.UploadContainerUri is null ? null : new Uri(settings.UploadContainerUri);
        var blobName = source is null ? null : ConnectionFactory.Read(source).BlobName;

        var overrides = await analysis.GetOverridesAsync(engagementId, cancellationToken).ConfigureAwait(false);

        var estimator = BuildEstimator(
            [.. overrides.Select(entry => new Override(
                entry.Scope, entry.RuleId, entry.ComponentTypeId, entry.FindingKey,
                entry.Low, entry.High, entry.StoryPoints, entry.Rationale, entry.SetBy))]);

        return new StageServices(
            // Every solution this run can read as a file.
            //
            // For an offline engagement that is the one somebody uploaded. For a live one it
            // is each chosen solution, exported from the environment through the same call a
            // person makes when they click Export, which is what makes a live connection the
            // richest source rather than the poorest.
            //
            // Seventeen rules depend on this. Fourteen read the zip and three need
            // Microsoft's checker, which takes a file, and every one of them reported "not
            // assessed" against a live environment while reporting fine against an uploaded
            // copy of the same solutions. The extraction-sources contract has always
            // declared solutionZip and checker as fully reached by a live mode; nothing had
            // ever implemented it.
            OpenSolutionFiles: async (chosen, progress, token) =>
            {
                var files = new List<SolutionFile>();

                if (blobName is not null && await ConnectionFactory
                    .BlobExistsAsync(uploads, blobName, token).ConfigureAwait(false))
                {
                    var uploaded = blobName;
                    files.Add(new SolutionFile(uploaded, OpenOrFail(uploads, uploaded)));
                }

                if (source is null || source.Mode == "offlineZip" || chosen.Count == 0 || uploads is null)
                {
                    return files;
                }

                var client = await connections.ForDataverseAsync(source, token).ConfigureAwait(false);
                var reader = new DataverseReader(client);
                var done = 0;

                foreach (var name in chosen)
                {
                    token.ThrowIfCancellationRequested();

                    // Said before it starts, not after. A minute of silence per solution is
                    // what made a working export indistinguishable from a stuck one.
                    progress.Report(new StageNote("exporting", name, ++done, chosen.Count));

                    // One blob per solution per run. Per run rather than per solution,
                    // because a solution changes and a report is about the estate as it was
                    // on the day it was read.
                    var export = ExportBlobName(runId, name);

                    try
                    {
                        // Already there, from an earlier attempt at this same run. A stage
                        // that failed after the export is retried by hand, and paying a
                        // quarter of an hour again to fetch files that have not changed
                        // since is how retrying a run stops being something anybody does.
                        if (await ConnectionFactory.BlobExistsAsync(uploads, export, token).ConfigureAwait(false))
                        {
                            files.Add(new SolutionFile(name, OpenOrFail(uploads, export)));
                            continue;
                        }

                        bool exported;

                        // Out of the environment and into the storage account in one pass:
                        // the worker never holds the file. One at a time and slowly, because
                        // an export took seventy seconds against a real environment and
                        // asking for a dozen at once is how a client's environment starts
                        // throttling everything else somebody is doing in it.
                        var destination = await ConnectionFactory
                            .CreateBlobAsync(uploads, export, token).ConfigureAwait(false);

                        await using (destination.ConfigureAwait(false))
                        {
                            exported = await reader.ExportSolutionAsync(name, destination, token)
                                .ConfigureAwait(false);
                        }

                        if (exported) files.Add(new SolutionFile(name, OpenOrFail(uploads, export)));
                        else Console.Error.WriteLine($"The environment would not export '{name}'.");
                    }
                    catch (Exception failure) when (failure is HttpRequestException or JsonException
                        or IOException or FormatException or RequestFailedException
                        or InvalidOperationException or TaskCanceledException)
                    {
                        // One solution that will not export is not a reason to lose the other
                        // eleven. The rules that needed it report as not assessed, which is
                        // the whole point of that machinery.
                        Console.Error.WriteLine($"Could not export '{name}': {failure.Message}");
                    }
                }

                return files;
            },

            // Authenticates without reading anything. The contract puts this first and says
            // it writes nothing, which is what makes it safe to run against a client who has
            // bought nothing yet.
            CheckConnections: async token =>
            {
                var checks = new List<ConnectionCheck>();

                if (blobName is not null)
                {
                    await using var file = await ConnectionFactory
                        .OpenUploadAsync(uploads, blobName, token).ConfigureAwait(false);

                    checks.Add(file is null
                        ? new ConnectionCheck(source?.Name ?? "Solution file", "offlineZip", false, null,
                            "The uploaded solution file is not in the container any more. Upload it again.", [])
                        : new ConnectionCheck(source?.Name ?? "Solution file", "offlineZip", true, "the uploaded file",
                            "Read from the uploaded export. Nothing was reached over the network.",
                            [EvidenceSource.SolutionZip, EvidenceSource.Checker]));
                }

                if (source is not null && source.Mode != "offlineZip")
                {
                    try
                    {
                        var client = await connections.ForDataverseAsync(source, token).ConfigureAwait(false);
                        var probe = await new DataverseReader(client).TestAsync(token).ConfigureAwait(false);

                        // Only what the probe actually proved. WhoAmI establishes that the
                        // credential works and that the identity has a user in the
                        // environment; it says nothing about whether that user may read run
                        // history, so runtime is not claimed here for any mode. The extract
                        // stage attempts it and records what happened, which is the only
                        // honest source for that answer.
                        var reaches = probe.Succeeded
                            ? new List<EvidenceSource> { EvidenceSource.Metadata, EvidenceSource.Checker }
                            : new List<EvidenceSource>();

                        checks.Add(new ConnectionCheck(
                            source.Name, source.Mode, probe.Succeeded, probe.Identity, probe.Message, reaches));
                    }
                    catch (Exception failure) when (failure is HttpRequestException or InvalidOperationException
                        or Azure.RequestFailedException or Azure.Identity.AuthenticationFailedException)
                    {
                        // Reported rather than thrown. One unreachable connection out of two
                        // is a partial stage, and the stage decides that rather than this.
                        checks.Add(new ConnectionCheck(
                            source.Name, source.Mode, false, null, failure.Message, []));
                    }
                }

                return checks;
            },

            // What the environment has, as opposed to what the run read. Without this the
            // total and the analysed count are the same number and the report can never say
            // it covered four solutions of nineteen.
            ListSolutions: async token =>
            {
                if (source is null || source.Mode == "offlineZip") return [];

                try
                {
                    var client = await connections.ForDataverseAsync(source, token).ConfigureAwait(false);

                    return await new DataverseReader(client).ListSolutionsAsync(token).ConfigureAwait(false);
                }
                catch (Exception failure) when (failure is HttpRequestException or InvalidOperationException
                    or Azure.RequestFailedException or Azure.Identity.AuthenticationFailedException)
                {
                    // Not fatal on its own. The connect stage has already decided whether the
                    // source is usable; failing here as well would report one fault twice.
                    Console.WriteLine($"Could not list solutions: {failure.Message}");
                    return [];
                }
            },

            // Recorded through the store rather than handed back to the stage, so the list
            // outlives the pause. A run that nobody ever resumes still says what the
            // environment held on the day it looked.
            RecordSolutions: async (runId, solutions, token) =>
                await workspace.RecordRunSolutionsAsync(
                    runId,
                    [.. solutions.Select(solution => new RunSolution(
                        runId,
                        solution.UniqueName,
                        solution.FriendlyName,
                        solution.Version,
                        solution.IsManaged,
                        solution.PublisherPrefix,
                        solution.PublisherName,
                        solution.ComponentCount,

                        // Worked out here, once, and stored beside the answer. Deciding it in
                        // the query that reads the list would mean a change to the rule
                        // silently rewriting what every old run appeared to have been asked.
                        FirstPartySolutions.IsFirstParty(solution),

                        // Not selected and not rejected. Null is what makes the stage stop
                        // and ask rather than read everything.
                        IsSelected: null))],
                    token).ConfigureAwait(false),

            ReadSelection: async (runId, token) =>
            {
                var selection = await workspace.GetSelectionAsync(runId, token).ConfigureAwait(false);

                if (selection is null) return null;

                return new ChosenScope(
                    selection.Solutions,
                    selection.Checks.SolutionChecker,
                    selection.Checks.ModelEstimates,
                    selection.Checks.EnvironmentHealth,
                    selection.Checks.ExportSolutions);
            },

            ReadEnvironment: async (chosen, includeRuntime, progress, token) =>
            {
                if (source is null || source.Mode == "offlineZip") return null;

                var client = await connections.ForDataverseAsync(source, token).ConfigureAwait(false);
                var reader = new DataverseReader(client);

                var test = await reader.TestAsync(token).ConfigureAwait(false);

                // Recorded on the connection before anything is read. A connection that
                // authenticates and holds no role reads an environment with nothing in it, and
                // this row is how somebody spots that before a client does.
                await workspace.RecordConnectionTestAsync(
                    source.ConnectionId, test.Succeeded, test.Identity, test.Message, null, token).ConfigureAwait(false);

                if (!test.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"The connection '{source.Name}' did not authenticate: {test.Message}");
                }

                var result = await reader.ReadAsync(chosen, includeRuntime, progress, token).ConfigureAwait(false);

                return new EnvironmentRead(
                    result.Components,
                    result.Links,
                    [.. result.Reads.Select(read => (read.ComponentTypeId, read.Succeeded, read.RecordCount, read.FailureReason))],
                    result.Identity,
                    // Asked for is not the same as reached. Run history is not in the Dataverse
                    // Web API at all, so this stays false until that reader exists, and the
                    // rules needing it report as not assessed.
                    ReachedRuntime: false);
            },

            RunChecker: async (file, token) =>
            {
                if (source is null)
                {
                    return new CheckerOutcome(false, [], "The checker needs a connection to authenticate with.");
                }

                var geography = ConnectionFactory.Read(source).CheckerGeography;

                if (string.IsNullOrWhiteSpace(geography))
                {
                    // Never defaulted. Where the checker runs is a data residency decision and
                    // guessing it for a client is not this product's to make.
                    return new CheckerOutcome(false, [],
                        "No checker geography is set on this connection. Where the solution is uploaded for analysis " +
                        "is a data residency decision and is not defaulted.");
                }

                var client = await connections.ForCheckerAsync(source, token).ConfigureAwait(false);
                var checker = new CheckerClient(client, geography);

                var ruleset = await checker.ResolveRulesetAsync("Solution Checker", token).ConfigureAwait(false);

                if (ruleset is null)
                {
                    return new CheckerOutcome(false, [], "The checker service did not return its ruleset list.");
                }

                var run = await checker.AnalyseAsync(file, "solution.zip", ruleset.Value, TimeSpan.FromMinutes(20), token)
                    .ConfigureAwait(false);

                return new CheckerOutcome(run.Succeeded, run.Issues, run.FailureReason);
            },

            Estimator: estimator,
            BacklogBuilder: new BacklogBuilder(engagementId, engagementName, criteria, backlogLanguage),

            Publish: async (runId, items, approvedHash, token) =>
            {
                if (target is null)
                {
                    throw new InvalidOperationException("A publish run reached the publish stage with no target connection.");
                }

                var devOpsSettings = ConnectionFactory.Read(target);
                var client = await connections.ForDevOpsAsync(target, token).ConfigureAwait(false);

                var publisher = new WorkItemPublisher(client,
                    devOpsSettings.Organisation ?? throw new InvalidOperationException("No Azure DevOps organisation is set."),
                    devOpsSettings.Project ?? throw new InvalidOperationException("No Azure DevOps project is set."));

                var published = await publisher.PublishAsync(items, approvedHash, approvedHash,
                    dryRun: false, confirmedCount: items.Count, token).ConfigureAwait(false);

                // The run and the key, not Guid.Empty twice. This recorded a publish against
                // no run and no backlog item, which the foreign key would have refused had
                // this path ever run: the worker's publish has never been exercised.
                await analysis.WritePublishedAsync(runId,
                    [.. published.Select(entry => (entry.Key, devOpsSettings.Organisation!, devOpsSettings.Project!,
                        entry.WorkItemId, entry.Url, entry.Action))],
                    token).ConfigureAwait(false);

                return published.Count;
            },

            Persist: new StorePersistence(analysis, workspace),
            FixedCosts: [.. EstimateCatalogue.FixedCosts.Select(cost => new FixedCost(cost.Id, cost.Name, cost.Low, cost.High))],
            Bands: EstimateCatalogue.Bands,
            ComplexityRules: ComplexityRule.FromContract(),
            RoadmapPositions: RuleCatalogue.Roadmap.ToDictionary(
                entry => entry.Key,
                entry => new RoadmapPosition(entry.Value.Row, entry.Value.Column, entry.Value.Band),
                StringComparer.Ordinal));
    }

    /// <summary>
    /// The estimator, with a model when one is configured and without one when it is not.
    /// </summary>
    /// <remarks>
    /// Degrades visibly rather than refusing to start. An engagement with no model still
    /// produces a report; every estimate says it is a band default and the score carries a
    /// caveat counting them.
    /// </remarks>
    private Estimator BuildEstimator(IReadOnlyList<Override> overrides)
    {
        if (string.IsNullOrWhiteSpace(settings.OpenAiEndpoint) || string.IsNullOrWhiteSpace(settings.OpenAiDeployment))
        {
            return new Estimator(EstimateCatalogue.Bands, overrides);
        }

        return new Estimator(
            EstimateCatalogue.Bands,
            overrides,
            new AzureOpenAiModel(new Uri(settings.OpenAiEndpoint), settings.OpenAiDeployment));
    }
}
