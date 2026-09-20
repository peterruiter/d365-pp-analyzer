namespace PowerPete.Analyzer.Jobs;

using System.Globalization;
using System.Text.Json;
using PowerPete.Analyzer.Analysis;
using PowerPete.Analyzer.Data;
using PowerPete.Analyzer.DevOps;
using PowerPete.Analyzer.Dataverse;
using PowerPete.Analyzer.Domain;
using PowerPete.Analyzer.Extraction;
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

        // Through DeploymentSettings rather than by name. The container is given
        // ConnectionStrings__Analyzer and KeyVault__Uri; this used to ask for
        // ANALYZER_SQL_CONNECTION and ANALYZER_KEYVAULT_URI and find neither.
        var connectionString = DeploymentSettings.FromEnvironment(DeploymentSettings.SqlConnection);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            missing.Add(Unset(DeploymentSettings.SqlConnection) + " There is nowhere to record a run.");
        }

        var vault = DeploymentSettings.FromEnvironment(DeploymentSettings.KeyVaultUri);
        if (string.IsNullOrWhiteSpace(vault))
        {
            // Not fatal. An engagement running entirely on uploaded solution files needs no
            // credential at all, and refusing to start would make the safest mode the hardest
            // one to use.
            Console.Error.WriteLine(
                Unset(DeploymentSettings.KeyVaultUri)
                + " Offline engagements will work; anything needing a credential will fail when it "
                + "reaches the connection.");
        }

        problems = missing;
        if (missing.Count > 0) return null;

        return new WorkerSettings(
            connectionString!,
            vault,
            DeploymentSettings.FromEnvironment(DeploymentSettings.UploadContainer),
            DeploymentSettings.FromEnvironment(DeploymentSettings.OpenAiEndpoint),
            DeploymentSettings.FromEnvironment(DeploymentSettings.OpenAiDeployment),
            DeploymentSettings.FromEnvironment(DeploymentSettings.WorkerId) ?? Environment.MachineName,
            typeof(WorkerSettings).Assembly.GetName().Version?.ToString() ?? "unknown",
            TimeSpan.FromSeconds(
                int.TryParse(
                    DeploymentSettings.FromEnvironment(DeploymentSettings.PollSeconds),
                    CultureInfo.InvariantCulture,
                    out var seconds)
                    ? seconds
                    : 10));
    }

    /// <summary>Says a setting is unset, naming every spelling that would have counted.</summary>
    /// <param name="setting">The setting.</param>
    /// <returns>A sentence an operator can act on.</returns>
    private static string Unset(DeploymentSetting setting) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{string.Join(" or ", DeploymentSettings.Names(setting).Select(DeploymentSettings.EnvironmentName))} is not set.");
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
    public Task SetRunStatusAsync(Guid runId, string status, string? failure, CancellationToken cancellationToken) =>
        store.SetRunStatusAsync(runId, status, failure, cancellationToken);

    public Task SetStageAsync(Guid runId, string stageId, string status, string? failure, string? checkpoint, CancellationToken cancellationToken) =>
        store.SetStageAsync(runId, stageId, status, failure, checkpoint, cancellationToken);

    public Task<IReadOnlyDictionary<string, string?>> GetCompletedStagesAsync(Guid runId, CancellationToken cancellationToken) =>
        store.GetCompletedStagesAsync(runId, cancellationToken);
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

        // Checked on start, not applied. This called ApplyAsync, which was fine for as long
        // as every migration had already been applied out of band by a person, and threw an
        // unhandled exception the first time one had not: the managed identity holds
        // db_datareader and db_datawriter and nothing else, deliberately, and cannot alter a
        // table. The container then restarted every five minutes with a stack trace nobody
        // was reading.
        //
        // Grant-DatabaseAccess.ps1 is explicit that this is the intended arrangement, and it
        // is the right one. A container that reads a client's estate should not be able to
        // reshape the database it writes to.
        var migrator = new DatabaseMigrator(settings.ConnectionString);
        var pending = await migrator.PendingAsync(cancellationToken).ConfigureAwait(false);

        if (pending.Count > 0)
        {
            // Loud, and repeated on every poll below rather than said once at startup and
            // scrolled away. A worker sitting idle against a database it does not match is
            // the exact failure this repository keeps finding: something that looks healthy
            // and does nothing.
            Console.Error.WriteLine(
                $"The database is behind this build by {pending.Count} migration(s): {string.Join(", ", pending)}.");
            Console.Error.WriteLine(
                "No work will be claimed until they are applied. Run ./build/Initialize-Database.ps1 as "
                + "somebody who can change the schema; this worker cannot and is not meant to be able to.");
        }
        else
        {
            Console.WriteLine($"Schema is current, {DatabaseMigrator.LoadMigrations().Count} migration(s).");
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            RunCommand? command = null;

            try
            {
                if (pending.Count > 0)
                {
                    // Re-checked rather than latched, so the worker picks itself up the
                    // moment somebody applies the migrations, without a restart.
                    pending = await migrator.PendingAsync(cancellationToken).ConfigureAwait(false);

                    if (pending.Count > 0)
                    {
                        Console.Error.WriteLine(
                            $"Still waiting: {pending.Count} migration(s) unapplied. Claiming nothing.");

                        await Task.Delay(settings.PollInterval, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    Console.WriteLine("Schema is current now. Claiming work.");
                }

                command = await workspace.ClaimCommandAsync(settings.WorkerId, settings.Version, cancellationToken)
                    .ConfigureAwait(false);

                if (command is null)
                {
                    await Task.Delay(settings.PollInterval, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                Console.WriteLine($"Claimed {command.Command} for run {command.RunId}.");

                // Says this worker is still here, every half minute, for as long as the
                // command takes. Without it a command claimed by a container that was then
                // replaced stayed claimed for ever and nothing ever retried it.
                //
                // Out here rather than inside a stage, because a stage can legitimately run
                // for an hour and the question this answers is whether the worker is alive,
                // not whether the stage is making progress.
                using var beating = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var heartbeat = HeartbeatAsync(workspace, command.CommandId, beating.Token);

                try
                {
                    await ExecuteAsync(command, workspace, analysis, cancellationToken).ConfigureAwait(false);
                    await workspace.CompleteCommandAsync(command.CommandId, true, null, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    await beating.CancelAsync().ConfigureAwait(false);
                    await heartbeat.ConfigureAwait(false);
                }
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

        if (command.Command == "retryStage" && command.StageId is { Length: > 0 } stageId)
        {
            // Everything from that stage onwards is cleared, not just the stage named. A
            // stage that ran again against the state a later stage had already consumed
            // would leave a run whose findings came from one extraction and whose score came
            // from another, and nothing on the screen would say so.
            await workspace
                .ClearStagesFromAsync(command.RunId, PipelineOrder.Stages, stageId, cancellationToken)
                .ConfigureAwait(false);
        }

        var run = await workspace.GetRunAsync(command.RunId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Run {command.RunId} does not exist.");

        var engagements = await workspace.ListConnectionsAsync(run.EngagementId, cancellationToken).ConfigureAwait(false);
        var source = engagements.FirstOrDefault(connection => connection.ConnectionId == run.SourceConnectionId);
        var target = engagements.FirstOrDefault(connection => connection.ConnectionId == run.TargetConnectionId);

        var secrets = SecretStore.For(settings.KeyVaultUri);
        var factory = new StageServicesFactory(new ConnectionFactory(secrets), analysis, workspace, settings);

        // The engagement, for its name and the language its backlog is written in. The name
        // was the identifier before this, so every work item this product had ever raised was
        // tagged with a GUID.
        var engagement = await workspace.GetEngagementAsync(run.EngagementId, cancellationToken)
            .ConfigureAwait(false);

        var services = await factory.BuildAsync(
            run.EngagementId,
            engagement?.Name ?? run.EngagementId.ToString(),
            source,
            target,
            AcceptanceCriteria.Load(),
            engagement?.BacklogLanguage,
            cancellationToken).ConfigureAwait(false);

        // Resolved once, here, rather than worked out by each stage from the mode and a
        // nullable override. Two stages deriving the same answer is two places for them to
        // disagree, and the way they would disagree is a report that says it ran the checker
        // and did not.
        var chosen = await workspace.GetSelectionAsync(run.RunId, cancellationToken).ConfigureAwait(false);

        var state = new RunState
        {
            RunId = run.RunId,
            EngagementId = run.EngagementId,
            Mode = run.Mode,
            EnvironmentRole = source?.EnvironmentRole ?? "unknown",
            Checks = RunChecks.ForMode(
                run.Mode,
                chosen?.Checks.SolutionChecker,
                chosen?.Checks.ModelEstimates,
                chosen?.Checks.EnvironmentHealth)
        };

        var stages = new IStage[]
        {
            // In the order analysis-stages.json declares. Connect writes nothing and proves
            // the credential before anything reads; selectSolutions says what exists before
            // extract says what it looked at.
            new ConnectStage(services),
            new SelectSolutionsStage(services),
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

        Console.WriteLine(outcome.Status == "awaitingSelection"
            ? $"Run {run.RunId} is waiting for somebody to choose solutions, after {outcome.StagesRun} stage(s)."
            : $"Run {run.RunId} ended {outcome.Status} after {outcome.StagesRun} stage(s).");
    }

    /// <summary>
    /// Writes a heartbeat until the command finishes.
    /// </summary>
    /// <remarks>
    /// Failures are swallowed on purpose. A missed heartbeat because the database blinked is
    /// not a reason to abandon a run that is going perfectly well, and the claim query
    /// tolerates ten missed in a row before anybody else may take the work.
    /// </remarks>
    /// <param name="workspace">Where to write it.</param>
    /// <param name="commandId">Which command.</param>
    /// <param name="cancellationToken">Cancelled when the command ends.</param>
    private static async Task HeartbeatAsync(
        WorkspaceStore workspace, Guid commandId, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
                await workspace.HeartbeatAsync(commandId, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
#pragma warning disable CA1031 // A heartbeat that cannot be written must not end the run it is reporting on.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                Console.Error.WriteLine($"Could not write a heartbeat: {exception.Message}");
            }
        }
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
