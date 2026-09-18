namespace PowerPete.Analyzer.Jobs;

using System.Text.Json;
using PowerPete.Analyzer.Analysis;
using PowerPete.Analyzer.Data;
using PowerPete.Analyzer.DevOps;
using PowerPete.Analyzer.Domain;
using PowerPete.Analyzer.Estimation;
using PowerPete.Analyzer.Pipeline;
using PowerPete.Analyzer.Pipeline.Stages;
using PowerPete.Analyzer.Analysis.Handlers;

/// <summary>
/// What the worker needs, and where it reads it from.
/// </summary>
/// <remarks>
/// Environment variables rather than a settings file. A container reads its configuration from
/// the platform that started it, and a file on disk is one more thing that can be missing at
/// three in the morning.
///
/// Everything here is checked before the loop starts. A worker that begins polling and then
/// discovers it has no connection string is a worker that looks healthy on a dashboard while
/// doing nothing, which is the one behaviour this product should never have.
/// </remarks>
public sealed record WorkerSettings(
    string ConnectionString,
    string? KeyVaultUri,
    string? UploadContainerUri,
    string? OpenAiEndpoint,
    string? OpenAiDeployment,
    string WorkerId,
    string Version,
    TimeSpan PollInterval)
{
    /// <summary>Reads the settings, or says which ones are missing.</summary>
    /// <param name="problems">What is not set.</param>
    public static WorkerSettings? Read(out IReadOnlyList<string> problems)
    {
        var missing = new List<string>();

        var connectionString = Environment.GetEnvironmentVariable("ANALYZER_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            missing.Add("ANALYZER_SQL_CONNECTION is not set. There is nowhere to record a run.");
        }

        var vault = Environment.GetEnvironmentVariable("ANALYZER_KEYVAULT_URI");
        if (string.IsNullOrWhiteSpace(vault))
        {
            // Not fatal. An engagement running entirely on uploaded solution files needs no
            // credential at all, and refusing to start would make the safest mode the hardest
            // one to use.
            Console.Error.WriteLine(
                "ANALYZER_KEYVAULT_URI is not set. Offline engagements will work; anything needing a " +
                "credential will fail when it reaches the connection.");
        }

        problems = missing;
        if (missing.Count > 0) return null;

        return new WorkerSettings(
            connectionString!,
            vault,
            Environment.GetEnvironmentVariable("ANALYZER_UPLOAD_CONTAINER"),
            Environment.GetEnvironmentVariable("ANALYZER_OPENAI_ENDPOINT"),
            Environment.GetEnvironmentVariable("ANALYZER_OPENAI_DEPLOYMENT"),
            Environment.GetEnvironmentVariable("ANALYZER_WORKER_ID") ?? Environment.MachineName,
            typeof(WorkerSettings).Assembly.GetName().Version?.ToString() ?? "unknown",
            TimeSpan.FromSeconds(
                int.TryParse(Environment.GetEnvironmentVariable("ANALYZER_POLL_SECONDS"), out var seconds) ? seconds : 10));
    }
}

/// <summary>
/// Turns the stores into the shapes the pipeline expects.
/// </summary>
/// <remarks>
/// Here rather than in the pipeline, deliberately. `PowerPete.Analyzer.Pipeline` does not
/// reference the data layer and should not: a stage that can be tested without a database is a
/// stage somebody will actually test.
/// </remarks>
internal sealed class StoreJournal(WorkspaceStore store) : IRunJournal
{
    public Task SetRunStatusAsync(Guid runId, string status, string? error, CancellationToken cancellationToken) =>
        store.SetRunStatusAsync(runId, status, error, cancellationToken);

    public Task SetStageAsync(Guid runId, string stageId, string status, string? error, string? checkpoint, CancellationToken cancellationToken) =>
        store.SetStageAsync(runId, stageId, status, error, checkpoint, cancellationToken);

    public Task<IReadOnlyDictionary<string, string?>> GetCompletedStagesAsync(Guid runId, CancellationToken cancellationToken) =>
        store.GetCompletedStagesAsync(runId, cancellationToken);
}

/// <summary>Writes what a run found.</summary>
internal sealed class StorePersistence(AnalysisStore analysis, WorkspaceStore workspace) : IRunPersistence
{
    public Task RecordReadAsync(Guid runId, string componentTypeId, string evidenceSource, bool succeeded, int? count, string? reason, CancellationToken cancellationToken) =>
        analysis.RecordEntityReadAsync(runId, componentTypeId, evidenceSource, succeeded, count, reason, cancellationToken);

    public Task SaveComponentsAsync(Guid runId, IReadOnlyList<DiscoveredComponent> components, CancellationToken cancellationToken) =>
        analysis.WriteComponentsAsync(runId,
            [.. components.Select(component => (
                component.ComponentId,
                component.StableKey,
                component.TypeId,
                component.DisplayName,
                component.SchemaName,
                component.PlatformId,
                component.Type?.Craft.ToString().ToLowerInvariant() ?? "config",
                component.Type?.Lifecycle.ToString().ToLowerInvariant() ?? "current",
                component.Type?.Domain ?? "platform",
                component.IsManaged,
                component.Attribute<bool?>("isCustom") ?? true,
                component.OwnerUpn,
                JsonSerializer.Serialize(component.Attributes)))],
            cancellationToken);

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

    public Task SaveNotAssessedAsync(Guid runId, IReadOnlyList<NotAssessed> notAssessed, CancellationToken cancellationToken) =>
        analysis.WriteNotAssessedAsync(runId,
            [.. notAssessed.Select(entry => (entry.RuleId, entry.Reason, entry.MissingEvidence))],
            cancellationToken);

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
            new { score.ByDomain, score.ByLifecycle, score.ByCraft, score.DebtByDomain, score.RatioDefinition, score.Caveats, customisation, roadmap },
            cancellationToken);

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

    public Task<string?> GetApprovedHashAsync(Guid runId, CancellationToken cancellationToken) =>
        workspace.GetApprovedHashAsync(runId, cancellationToken);

    private static string Layer(EstimateLayer layer) => layer switch
    {
        EstimateLayer.EngagementOverride => "engagementOverride",
        EstimateLayer.Model => "model",
        _ => "bandDefault"
    };
}

/// <summary>
/// The poll loop.
/// </summary>
/// <remarks>
/// Claims one command, runs it, records how it ended, sleeps. Deliberately single threaded: a
/// run reads a client's whole estate and two of them at once on one worker turns a slow report
/// into two slow reports and a memory problem. Scale by running more workers, which the claim
/// query already handles.
/// </remarks>
public sealed class Worker(WorkerSettings settings)
{
    /// <summary>Runs until cancelled.</summary>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var workspace = new WorkspaceStore(settings.ConnectionString);
        var analysis = new AnalysisStore(settings.ConnectionString);

        Console.WriteLine($"Worker {settings.WorkerId} running version {settings.Version}.");
        Console.WriteLine($"Polling every {settings.PollInterval.TotalSeconds:0} seconds.");

        // Applied on start rather than by a deployment step. A container that carries its own
        // schema cannot be pointed at a database it does not know how to build.
        var migrations = await new DatabaseMigrator(settings.ConnectionString).ApplyAsync(cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"{migrations.Count(result => result.Applied)} migration(s) applied, {migrations.Count} in total.");

        while (!cancellationToken.IsCancellationRequested)
        {
            RunCommand? command = null;

            try
            {
                command = await workspace.ClaimCommandAsync(settings.WorkerId, settings.Version, cancellationToken)
                    .ConfigureAwait(false);

                if (command is null)
                {
                    await Task.Delay(settings.PollInterval, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                Console.WriteLine($"Claimed {command.Command} for run {command.RunId}.");

                await ExecuteAsync(command, workspace, analysis, cancellationToken).ConfigureAwait(false);
                await workspace.CompleteCommandAsync(command.CommandId, true, null, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
#pragma warning disable CA1031 // A worker that dies on one bad command stops processing every other one.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                Console.Error.WriteLine($"Command failed: {exception.Message}");

                if (command is not null)
                {
                    await workspace.CompleteCommandAsync(command.CommandId, false, exception.Message, CancellationToken.None)
                        .ConfigureAwait(false);
                    await workspace.SetRunStatusAsync(command.RunId, "failed", exception.Message, CancellationToken.None)
                        .ConfigureAwait(false);
                }

                await Task.Delay(settings.PollInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        Console.WriteLine("Worker stopping.");
        return 0;
    }

    /// <summary>Runs one command.</summary>
    /// <param name="command">What was asked for.</param>
    /// <param name="workspace">Engagements, connections and runs.</param>
    /// <param name="analysis">Where findings are written.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    private async Task ExecuteAsync(
        RunCommand command,
        WorkspaceStore workspace,
        AnalysisStore analysis,
        CancellationToken cancellationToken)
    {
        if (command.Command is "cancel" or "approve")
        {
            // Both are recorded by the API against a person. The worker has nothing to do with
            // either, and a worker that could approve on somebody's behalf would make the gate
            // meaningless.
            return;
        }

        var run = await workspace.GetRunAsync(command.RunId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Run {command.RunId} does not exist.");

        var engagements = await workspace.ListConnectionsAsync(run.EngagementId, cancellationToken).ConfigureAwait(false);
        var source = engagements.FirstOrDefault(connection => connection.ConnectionId == run.SourceConnectionId);
        var target = engagements.FirstOrDefault(connection => connection.ConnectionId == run.TargetConnectionId);

        var secrets = new Secrets(settings.KeyVaultUri);
        var factory = new StageServicesFactory(new ConnectionFactory(secrets), analysis, workspace, settings);

        var services = await factory.BuildAsync(
            run.EngagementId,
            run.EngagementId.ToString(),
            source,
            target,
            AcceptanceCriteria.Load(),
            cancellationToken).ConfigureAwait(false);

        var state = new RunState
        {
            RunId = run.RunId,
            EngagementId = run.EngagementId,
            Mode = run.Mode,
            EnvironmentRole = source?.EnvironmentRole ?? "unknown"
        };

        var stages = new IStage[]
        {
            new ExtractStage(services),
            new CheckerStage(services),
            new ResolveStage(services),
            new AnalyseStage(services, new RuleEngine(Handlers())),
            new EstimateStage(services),
            new ScoreStage(services),
            new BacklogStage(services),
            new PublishStage(services)
        };

        // Which failures are fatal comes from the contract, not from here. Two copies of that
        // opinion would eventually disagree, and the disagreement would be invisible.
        var fatal = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["extract"] = true,
            ["resolve"] = true,
            ["analyse"] = true,
            ["score"] = true,
            ["backlog"] = true,
            ["publish"] = true,
            ["checker"] = false,
            ["estimate"] = false
        };

        var outcome = await new PipelineRunner(stages, new StoreJournal(workspace), fatal)
            .RunAsync(state, cancellationToken).ConfigureAwait(false);

        Console.WriteLine($"Run {run.RunId} ended {outcome.Status} after {outcome.StagesRun} stage(s).");
    }

    /// <summary>
    /// Every rule handler in the analysis assembly.
    /// </summary>
    /// <remarks>
    /// By reflection rather than a list. A hand written list is one somebody forgets to add
    /// to, and a missing handler does not fail: its rule reports as not implemented and the
    /// category quietly looks smaller than it is.
    /// </remarks>
    private static IReadOnlyList<IRuleHandler> Handlers() =>
        [.. typeof(RuleEngine).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && type.IsAssignableTo(typeof(IRuleHandler)))
            .Select(type => (IRuleHandler)Activator.CreateInstance(type)!)];
}
