namespace PowerPete.Analyzer.Data;

using Dapper;
using Microsoft.Data.SqlClient;

/// <summary>One piece of client work.</summary>
/// <param name="EngagementId">Its identifier.</param>
/// <param name="Name">What it is called.</param>
/// <param name="ClientName">Who it is for.</param>
/// <param name="Status">active or archived.</param>
/// <param name="IsRegulated">Drives the complexity multiplier on every estimate.</param>
/// <param name="ReportLanguage">The language reports are produced in.</param>
/// <param name="BacklogLanguage">The language work items are written in, which is not always the same.</param>
/// <param name="CreatedUtc">When.</param>
public sealed record Engagement(
    Guid EngagementId,
    string Name,
    string? ClientName,
    string Status,
    bool IsRegulated,
    string ReportLanguage,
    string BacklogLanguage,
    DateTime CreatedUtc);

/// <summary>A configured way into one system.</summary>
/// <param name="ConnectionId">Its identifier.</param>
/// <param name="EngagementId">Which engagement.</param>
/// <param name="Mode">servicePrincipal, delegated, offlineZip or azureDevOps.</param>
/// <param name="Name">What it is called.</param>
/// <param name="EnvironmentRole">development, test, acceptance, production or unknown.</param>
/// <param name="SettingsJson">Everything that is not a secret.</param>
/// <param name="SecretRef">The Key Vault secret name. Never the secret.</param>
/// <param name="SecretExpiresUtc">When the credential stops working, so it can be warned about rather than discovered on a Friday.</param>
/// <param name="LastTestedUtc">When it was last proved.</param>
/// <param name="LastTestSucceeded">Whether it worked.</param>
/// <param name="LastTestIdentity">Who it authenticated as. A connection that succeeds with too few privileges fails later in a way that looks like an empty estate.</param>
/// <param name="LastTestMessage">What it said, on a failure. The screen shows it rather than "failed".</param>
/// <param name="ReachJson">Which evidence sources it actually reached.</param>
public sealed record Connection(
    Guid ConnectionId,
    Guid EngagementId,
    string Mode,
    string Name,
    string EnvironmentRole,
    string SettingsJson,
    string? SecretRef,
    DateTime? SecretExpiresUtc,
    DateTime? LastTestedUtc,
    bool? LastTestSucceeded,
    string? LastTestIdentity,
    string? LastTestMessage,
    string? ReachJson);

/// <summary>One pass through the pipeline.</summary>
/// <param name="RunId">Its identifier.</param>
/// <param name="EngagementId">Which engagement.</param>
/// <param name="Mode">quickScan, assessment, publish or compare.</param>
/// <param name="Status">Where it is.</param>
/// <param name="SourceConnectionId">What it reads.</param>
/// <param name="TargetConnectionId">What it writes to. Null on everything but a publish.</param>
/// <param name="BasedOnRunId">The assessment a publish or a comparison works from.</param>
/// <param name="CreatedBy">Who asked for it.</param>
/// <param name="CreatedUtc">When.</param>
/// <param name="StartedUtc">When the worker picked it up.</param>
/// <param name="CompletedUtc">When it finished.</param>
/// <param name="Error">Why it did not.</param>
public sealed record AnalysisRun(
    Guid RunId,
    Guid EngagementId,
    string Mode,
    string Status,
    Guid? SourceConnectionId,
    Guid? TargetConnectionId,
    Guid? BasedOnRunId,
    string CreatedBy,
    DateTime CreatedUtc,
    DateTime? StartedUtc,
    DateTime? CompletedUtc,
    string? Error);

/// <summary>What the API asked the worker to do.</summary>
/// <param name="CommandId">Its identifier.</param>
/// <param name="RunId">Which run.</param>
/// <param name="Command">start, resume, retryStage, cancel, approve or publish.</param>
/// <param name="StageId">Which stage, on a retry.</param>
/// <param name="RequestedBy">Who asked.</param>
public sealed record RunCommand(Guid CommandId, Guid RunId, string Command, string? StageId, string RequestedBy);

/// <summary>One solution a run found in the environment, and whether it was chosen.</summary>
/// <param name="RunId">Which run found it.</param>
/// <param name="UniqueName">Its unique name, which is what the extract stage asks for.</param>
/// <param name="FriendlyName">What it is called on screen.</param>
/// <param name="Version">Its version.</param>
/// <param name="IsManaged">Whether it is managed.</param>
/// <param name="PublisherPrefix">The prefix stamped into every component inside it.</param>
/// <param name="PublisherName">Who published it.</param>
/// <param name="ComponentCount">How much is in it, where the environment says.</param>
/// <param name="IsFirstParty">Whether it looked like Microsoft's rather than the client's.</param>
/// <param name="IsSelected">Ticked, unticked, or null when nobody has been asked yet.</param>
public sealed record RunSolution(
    Guid RunId,
    string UniqueName,
    string? FriendlyName,
    string? Version,
    bool IsManaged,
    string? PublisherPrefix,
    string? PublisherName,
    int? ComponentCount,
    bool IsFirstParty,
    bool? IsSelected);

/// <summary>
/// Which optional checks a run was told to do.
/// </summary>
/// <remarks>
/// Every one is nullable and null means "whatever the mode says". The contract owns the
/// default per mode, and storing a resolved value here would mean a mode whose default
/// changes still applying the old one to every run that never expressed an opinion.
/// </remarks>
/// <param name="SolutionChecker">Microsoft's static analysis, the slowest part of a run.</param>
/// <param name="ModelEstimates">Estimates with rationale rather than band defaults.</param>
/// <param name="EnvironmentHealth">What the identity can actually read.</param>
public sealed record RunCheckChoices(bool? SolutionChecker, bool? ModelEstimates, bool? EnvironmentHealth);

/// <summary>What somebody told a paused run to do.</summary>
/// <param name="Checks">Which optional checks to run.</param>
/// <param name="Solutions">The unique names that were ticked.</param>
public sealed record RunSelection(RunCheckChoices Checks, IReadOnlyList<string> Solutions);

/// <summary>Where one stage of a run got to.</summary>
/// <param name="StageId">Which stage.</param>
/// <param name="Status">Where it is.</param>
/// <param name="Attempt">How many times it has been tried.</param>
/// <param name="StartedUtc">When it started.</param>
/// <param name="CompletedUtc">When it finished.</param>
/// <param name="Error">Why it did not.</param>
public sealed record RunStageState(
    string StageId,
    string Status,
    int Attempt,
    DateTime? StartedUtc,
    DateTime? CompletedUtc,
    string? Error);

/// <summary>
/// Engagements, connections and runs.
/// </summary>
/// <remarks>
/// Dapper rather than an ORM. The queries here are the product's own schema, they are read in
/// full on this page, and a change of shape should be visible as a changed query rather than
/// as a changed attribute three files away.
/// </remarks>
public sealed class WorkspaceStore(string connectionString)
{
    /// <summary>
    /// The columns each record declares, in one place.
    /// </summary>
    /// <remarks>
    /// Dapper needs a constructor matching the columns it is given, so a starred select
    /// against a table carrying audit columns the record does not declare fails to
    /// materialise at run time with a message about constructors rather than about columns.
    /// Naming them here keeps the list beside the record it belongs to and means adding a
    /// column to a table cannot break a read that never wanted it.
    /// </remarks>
    private const string EngagementColumns = "EngagementId, Name, ClientName, Status, IsRegulated, ReportLanguage, BacklogLanguage, CreatedUtc";

    /// <summary>The columns <see cref="AnalysisRun"/> declares.</summary>
    private const string RunColumns = "RunId, EngagementId, Mode, Status, SourceConnectionId, TargetConnectionId, BasedOnRunId, CreatedBy, CreatedUtc, StartedUtc, CompletedUtc, Error";

    /// <summary>The solution columns, in the record's order, written once for the same reason.</summary>
    private const string RunSolutionColumns = "RunId, UniqueName, FriendlyName, Version, IsManaged, PublisherPrefix, PublisherName, ComponentCount, IsFirstParty, IsSelected";

    private SqlConnection Connect() => new(connectionString);

    /// <summary>Every engagement somebody may see.</summary>
    /// <param name="access">What they hold.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<IReadOnlyList<Engagement>> ListEngagementsAsync(UserAccess access, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);

        await using var connection = Connect();

        // A global administrator sees everything. Everybody else sees exactly the engagements
        // in the access they were handed, filtered in the query rather than in code, so a
        // screen that forgets to filter cannot leak one.
        //
        // Filtered on the identifiers rather than by joining EngagementAccess again, because
        // not every grant is a row: the demonstration engagement is granted to everybody
        // admitted, in one place, and a join here would be a second answer to who may see
        // what. Reading it from the same dictionary Holds() reads means the list and the
        // endpoints cannot disagree.
        if (!access.IsGlobalAdmin && access.Roles.Count == 0) return [];

        // The columns are named rather than starred.
        //
        // Dapper materialises a record through its constructor, and it needs a constructor
        // whose parameters match the columns it was handed. SELECT * hands it every column
        // the table has, including the audit columns the record deliberately does not carry,
        // and it throws rather than ignoring them. Every engagement list threw, so the
        // product accepted a new engagement and then showed an empty picker: the row was
        // written and could not be read back.
        //
        // Naming them also means a migration that adds a column cannot break a read.
        // The demonstration first, then newest.
        //
        // It is the oldest engagement on any deployment, because it is written the first time
        // a container starts, so ordering by date alone buries it under everything a
        // consultant has created since. It is the one engagement that is meant to be found
        // without being looked for.
        const string order = """
            ORDER BY CASE WHEN EngagementId = @demoId THEN 0 ELSE 1 END, CreatedUtc DESC;
            """;

        var sql = access.IsGlobalAdmin
            ? $"SELECT {EngagementColumns} FROM ops.Engagement {order}"
            : $"SELECT {EngagementColumns} FROM ops.Engagement WHERE EngagementId IN @engagementIds {order}";

        var rows = await connection.QueryAsync<Engagement>(new CommandDefinition(
            sql,
            new { engagementIds = access.Roles.Keys.ToArray(), demoId = AccessStore.DemoEngagementId },
            cancellationToken: cancellationToken));

        return [.. rows];
    }

    /// <summary>
    /// Changes an engagement.
    /// </summary>
    /// <remarks>
    /// There was no way to. A client's name typed wrong on the day the engagement was
    /// created stayed wrong in every report afterwards, and the report language could only
    /// be chosen once, before anybody knew who the report was for.
    ///
    /// Not the status and not the identifier. Status is moved by what happens to the
    /// engagement rather than by somebody editing a field, and an identifier is what every
    /// run, connection and finding hangs off.
    /// </remarks>
    /// <param name="engagementId">Which engagement.</param>
    /// <param name="name">What it is called.</param>
    /// <param name="clientName">Who it is for, which is what the report says on its cover.</param>
    /// <param name="isRegulated">Drives a multiplier on every estimate.</param>
    /// <param name="reportLanguage">The language the report is written in.</param>
    /// <param name="backlogLanguage">The language work items are written in, which is not always the same.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<bool> UpdateEngagementAsync(
        Guid engagementId,
        string name,
        string? clientName,
        bool isRegulated,
        string reportLanguage,
        string backlogLanguage,
        CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        var changed = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE ops.Engagement
            SET Name = @name,
                ClientName = @clientName,
                IsRegulated = @isRegulated,
                ReportLanguage = @reportLanguage,
                BacklogLanguage = @backlogLanguage
            WHERE EngagementId = @engagementId;
            """,
            new { engagementId, name, clientName, isRegulated, reportLanguage, backlogLanguage },
            cancellationToken: cancellationToken));

        return changed > 0;
    }

    /// <summary>Creates an engagement and makes its creator an administrator of it.</summary>
    /// <param name="engagement">The engagement.</param>
    /// <param name="createdBy">Who created it.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task CreateEngagementAsync(Engagement engagement, string createdBy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(engagement);

        await using var connection = Connect();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO ops.Engagement
                (EngagementId, Name, ClientName, Status, IsRegulated, ReportLanguage, BacklogLanguage, CreatedBy)
            VALUES
                (@EngagementId, @Name, @ClientName, @Status, @IsRegulated, @ReportLanguage, @BacklogLanguage, @createdBy);
            """,
            new
            {
                engagement.EngagementId,
                engagement.Name,
                engagement.ClientName,
                engagement.Status,
                engagement.IsRegulated,
                engagement.ReportLanguage,
                engagement.BacklogLanguage,
                createdBy
            },
            transaction, cancellationToken: cancellationToken));

        // Creating an engagement nobody can open is a support ticket on the first day.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO ops.EngagementAccess (EngagementId, UserId, Role, GrantedBy)
            VALUES (@engagementId, @createdBy, @role, @createdBy);
            """,
            new { engagementId = engagement.EngagementId, createdBy = AccessStore.Normalise(createdBy), role = EngagementRoles.Admin },
            transaction, cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The connections on one engagement.</summary>
    /// <param name="engagementId">Which engagement.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<IReadOnlyList<Connection>> ListConnectionsAsync(Guid engagementId, CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        var rows = await connection.QueryAsync<Connection>(new CommandDefinition(
            """
            SELECT ConnectionId, EngagementId, Mode, Name, EnvironmentRole, SettingsJson,
                   SecretRef, SecretExpiresUtc, LastTestedUtc, LastTestSucceeded,
                   LastTestIdentity, LastTestMessage, ReachJson
            FROM ops.[Connection]
            WHERE EngagementId = @engagementId
            ORDER BY Name;
            """,
            new { engagementId },
            cancellationToken: cancellationToken));

        return [.. rows];
    }

    /// <summary>Credentials that stop working soon, so somebody is told before a publish fails.</summary>
    /// <param name="within">How far ahead to look.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<IReadOnlyList<Connection>> ListExpiringCredentialsAsync(TimeSpan within, CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        var rows = await connection.QueryAsync<Connection>(new CommandDefinition(
            """
            SELECT ConnectionId, EngagementId, Mode, Name, EnvironmentRole, SettingsJson,
                   SecretRef, SecretExpiresUtc, LastTestedUtc, LastTestSucceeded,
                   LastTestIdentity, LastTestMessage, ReachJson
            FROM ops.[Connection]
            WHERE SecretExpiresUtc IS NOT NULL AND SecretExpiresUtc < @deadline
            ORDER BY SecretExpiresUtc;
            """,
            new { deadline = DateTime.UtcNow + within },
            cancellationToken: cancellationToken));

        return [.. rows];
    }

    /// <summary>One connection, by its own identifier.</summary>
    /// <remarks>
    /// Carries the engagement, so a caller that was handed only a connection id can still
    /// check what the reader is allowed to see before showing them anything.
    /// </remarks>
    /// <param name="connectionId">Which connection.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<Connection?> GetConnectionAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        return await connection.QuerySingleOrDefaultAsync<Connection?>(new CommandDefinition(
            """
            SELECT ConnectionId, EngagementId, Mode, Name, EnvironmentRole, SettingsJson,
                   SecretRef, SecretExpiresUtc, LastTestedUtc, LastTestSucceeded,
                   LastTestIdentity, LastTestMessage, ReachJson
            FROM ops.[Connection]
            WHERE ConnectionId = @connectionId;
            """,
            new { connectionId },
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Points a connection at a credential in the vault.
    /// </summary>
    /// <remarks>
    /// Separate from creating the connection because an interactive sign-in cannot supply one
    /// up front: the connection has to exist before the browser is sent to Entra, because the
    /// round trip needs something to come back to.
    /// </remarks>
    /// <param name="connectionId">Which connection.</param>
    /// <param name="secretRef">The vault prefix, never the secret.</param>
    /// <param name="expiresUtc">When the credential stops working, where that is known.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task SetConnectionSecretAsync(
        Guid connectionId,
        string secretRef,
        DateTime? expiresUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE ops.[Connection] SET
                SecretRef = @secretRef,
                SecretExpiresUtc = @expiresUtc,
                UpdatedUtc = SYSUTCDATETIME()
            WHERE ConnectionId = @connectionId;
            """,
            new { connectionId, secretRef, expiresUtc },
            cancellationToken: cancellationToken));
    }

    /// <summary>Records what a connection test found, including who it authenticated as.</summary>
    /// <param name="connectionId">Which connection.</param>
    /// <param name="succeeded">Whether it worked.</param>
    /// <param name="identity">Who it was.</param>
    /// <param name="message">What it said.</param>
    /// <param name="reachJson">What it could reach.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task RecordConnectionTestAsync(
        Guid connectionId,
        bool succeeded,
        string? identity,
        string message,
        string? reachJson,
        CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE ops.Connection SET
                LastTestedUtc = SYSUTCDATETIME(),
                LastTestSucceeded = @succeeded,
                LastTestIdentity = @identity,
                LastTestMessage = @message,
                ReachJson = @reachJson,
                UpdatedUtc = SYSUTCDATETIME()
            WHERE ConnectionId = @connectionId;
            """,
            new { connectionId, succeeded, identity, message, reachJson },
            cancellationToken: cancellationToken));
    }

    /// <summary>Queues a run and asks the worker to start it.</summary>
    /// <param name="run">The run.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<Guid> QueueRunAsync(AnalysisRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);

        await using var connection = Connect();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO ops.AnalysisRun
                (RunId, EngagementId, Mode, Status, SourceConnectionId, TargetConnectionId, BasedOnRunId, CreatedBy)
            VALUES
                (@RunId, @EngagementId, @Mode, 'pending', @SourceConnectionId, @TargetConnectionId, @BasedOnRunId, @CreatedBy);
            """,
            run, transaction, cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO ops.RunCommand (CommandId, RunId, Command, RequestedBy)
            VALUES (@commandId, @runId, 'start', @requestedBy);
            """,
            new { commandId = Guid.NewGuid(), runId = run.RunId, requestedBy = run.CreatedBy },
            transaction, cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return run.RunId;
    }

    /// <summary>One run, or null when the identifier does not match anything.</summary>
    /// <param name="runId">Which run.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<AnalysisRun?> GetRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        return await connection.QuerySingleOrDefaultAsync<AnalysisRun?>(new CommandDefinition(
            $"SELECT {RunColumns} FROM ops.AnalysisRun WHERE RunId = @runId;",
            new { runId },
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Takes the next command, if there is one.
    /// </summary>
    /// <remarks>
    /// An update with a predicate rather than a read then a write. Two workers polling the
    /// same queue will both see the same unclaimed row; only one of them updates it, and the
    /// other gets nothing back rather than a second copy of the job.
    /// </remarks>
    /// <param name="workerId">Which worker.</param>
    /// <param name="version">Which image it is running. A Container Apps job keeps the image it started with, so a worker can be behind the deployment while every version number agrees.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<RunCommand?> ClaimCommandAsync(string workerId, string version, CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        var claimed = await connection.QuerySingleOrDefaultAsync<RunCommand?>(new CommandDefinition(
            """
            UPDATE TOP (1) ops.RunCommand
            SET ClaimedUtc = SYSUTCDATETIME(), ClaimedBy = @workerId, ClaimedVersion = @version
            OUTPUT inserted.CommandId, inserted.RunId, inserted.Command, inserted.StageId, inserted.RequestedBy
            WHERE ClaimedUtc IS NULL;
            """,
            new { workerId, version },
            cancellationToken: cancellationToken));

        return claimed;
    }

    /// <summary>Records how a command ended.</summary>
    /// <param name="commandId">Which command.</param>
    /// <param name="succeeded">Whether it worked.</param>
    /// <param name="error">Why not.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task CompleteCommandAsync(Guid commandId, bool succeeded, string? error, CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE ops.RunCommand
            SET CompletedUtc = SYSUTCDATETIME(), Succeeded = @succeeded, Error = @error
            WHERE CommandId = @commandId;
            """,
            new { commandId, succeeded, error },
            cancellationToken: cancellationToken));
    }

    /// <summary>Moves a run's status, and stamps the start and finish times with it.</summary>
    /// <param name="runId">Which run.</param>
    /// <param name="status">Where it is now.</param>
    /// <param name="failure">Why, on a failure.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task SetRunStatusAsync(Guid runId, string status, string? failure, CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE ops.AnalysisRun SET
                Status = @status,
                Error = @error,
                StartedUtc = CASE WHEN @status = 'running' AND StartedUtc IS NULL THEN SYSUTCDATETIME() ELSE StartedUtc END,
                CompletedUtc = CASE WHEN @status IN ('succeeded','partial','failed','cancelled') THEN SYSUTCDATETIME() ELSE CompletedUtc END
            WHERE RunId = @runId;
            """,
            new { runId, status, error = failure },
            cancellationToken: cancellationToken));
    }

    /// <summary>Records where a stage got to, including what it needs to resume.</summary>
    /// <param name="runId">Which run.</param>
    /// <param name="stageId">Which stage.</param>
    /// <param name="status">Where it is.</param>
    /// <param name="failure">Why, on a failure.</param>
    /// <param name="checkpointJson">Whatever it needs to pick up again.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task SetStageAsync(
        Guid runId,
        string stageId,
        string status,
        string? failure,
        string? checkpointJson,
        CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        await connection.ExecuteAsync(new CommandDefinition(
            """
            MERGE ops.RunStage AS target
            USING (SELECT @runId AS RunId, @stageId AS StageId) AS source
                ON target.RunId = source.RunId AND target.StageId = source.StageId
            WHEN MATCHED THEN UPDATE SET
                Status = @status,
                Error = @error,
                CheckpointJson = COALESCE(@checkpointJson, target.CheckpointJson),
                Attempt = CASE WHEN @status = 'running' THEN target.Attempt + 1 ELSE target.Attempt END,
                StartedUtc = CASE WHEN @status = 'running' THEN SYSUTCDATETIME() ELSE target.StartedUtc END,
                CompletedUtc = CASE WHEN @status IN ('succeeded','partial','failed','skipped') THEN SYSUTCDATETIME() ELSE target.CompletedUtc END
            WHEN NOT MATCHED THEN
                INSERT (RunId, StageId, Status, Attempt, StartedUtc, CheckpointJson)
                VALUES (@runId, @stageId, @status, 1, SYSUTCDATETIME(), @checkpointJson);
            """,
            new { runId, stageId, status, error = failure, checkpointJson },
            cancellationToken: cancellationToken));
    }

    /// <summary>The stages that already succeeded, so a resumed run skips them.</summary>
    /// <param name="runId">Which run.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<IReadOnlyDictionary<string, string?>> GetCompletedStagesAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        var rows = await connection.QueryAsync<(string StageId, string? CheckpointJson)>(new CommandDefinition(
            "SELECT StageId, CheckpointJson FROM ops.RunStage WHERE RunId = @runId AND Status = 'succeeded';",
            new { runId },
            cancellationToken: cancellationToken));

        return rows.ToDictionary(row => row.StageId, row => row.CheckpointJson, StringComparer.Ordinal);
    }

    /// <summary>
    /// Records an approval against the exact backlog somebody looked at.
    /// </summary>
    /// <remarks>
    /// An insert rather than a flag, so approving cannot happen as a side effect of updating
    /// something else, and so the absence of a row is the default state rather than something
    /// anybody has to remember to reset.
    /// </remarks>
    /// <param name="runId">Which run.</param>
    /// <param name="backlogHash">What they saw.</param>
    /// <param name="itemCount">How many items. Printed back at them before they confirm.</param>
    /// <param name="userId">Who.</param>
    /// <param name="displayName">Their name, so the record reads as a person.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task ApproveAsync(
        Guid runId,
        string backlogHash,
        int itemCount,
        string userId,
        string displayName,
        CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO ops.RunApproval (RunId, BacklogHash, ItemCount, ApprovedBy, ApprovedByName)
            VALUES (@runId, @backlogHash, @itemCount, @userId, @displayName);
            """,
            new { runId, backlogHash, itemCount, userId = AccessStore.Normalise(userId), displayName },
            cancellationToken: cancellationToken));
    }

    /// <summary>The hash an approval was bound to, or null when nobody has approved anything.</summary>
    /// <param name="runId">Which run.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<string?> GetApprovedHashAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT BacklogHash FROM ops.RunApproval WHERE RunId = @runId;",
            new { runId },
            cancellationToken: cancellationToken));
    }

    /// <summary>Every run on one engagement, newest first.</summary>
    /// <param name="engagementId">Which engagement.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<IReadOnlyList<AnalysisRun>> ListRunsAsync(
        Guid engagementId, CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        var rows = await connection.QueryAsync<AnalysisRun>(new CommandDefinition(
            """
            SELECT RunId, EngagementId, Mode, Status, SourceConnectionId, TargetConnectionId,
                   BasedOnRunId, CreatedBy, CreatedUtc, StartedUtc, CompletedUtc, Error
            FROM ops.AnalysisRun
            WHERE EngagementId = @engagementId
            ORDER BY CreatedUtc DESC;
            """,
            new { engagementId },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    /// <summary>
    /// Changes what a connection points at.
    /// </summary>
    /// <remarks>
    /// A connection could be created and never changed. A mistyped environment URL, a
    /// renamed tenant or a rotated credential meant making a second connection and living
    /// with the first one in the picker forever, which is how somebody eventually runs a
    /// discovery against the wrong environment.
    ///
    /// The mode is not changeable here. Each mode carries different settings and a
    /// different shape of credential, and a connection that changed from an offline file
    /// to a service principal in place would keep a secret reference that means nothing
    /// and a settings blob nothing reads. Changing how an estate is reached is a new
    /// connection, and the run history keeps pointing at the one it actually used.
    ///
    /// The secret is not touched. It lives in Key Vault under a reference this row holds,
    /// and an update that carried it would mean the credential travelled through a request
    /// body on every rename.
    /// </remarks>
    /// <param name="connectionId">Which connection.</param>
    /// <param name="name">What to call it.</param>
    /// <param name="environmentRole">Production, test, development or unknown.</param>
    /// <param name="settingsJson">The mode's settings.</param>
    /// <param name="secretExpiresUtc">When the credential expires, where anybody said.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<bool> UpdateConnectionAsync(
        Guid connectionId,
        string name,
        string environmentRole,
        string settingsJson,
        DateTime? secretExpiresUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        var changed = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE ops.[Connection]
            SET Name = @name,
                EnvironmentRole = @environmentRole,
                SettingsJson = @settingsJson,
                SecretExpiresUtc = @secretExpiresUtc
            WHERE ConnectionId = @connectionId;
            """,
            new { connectionId, name, environmentRole, settingsJson, secretExpiresUtc },
            cancellationToken: cancellationToken));

        return changed > 0;
    }

    /// <summary>How many runs were made through a connection.</summary>
    /// <remarks>
    /// Asked before deleting one. A run records which connection produced it, and a report
    /// that cannot say what it was read through is a report nobody can defend, so a
    /// connection with history is kept and the caller is told why.
    /// </remarks>
    /// <param name="connectionId">Which connection.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<int> CountRunsUsingAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*) FROM ops.AnalysisRun
            WHERE SourceConnectionId = @connectionId OR TargetConnectionId = @connectionId;
            """,
            new { connectionId },
            cancellationToken: cancellationToken));
    }

    /// <summary>Removes a connection nothing has used.</summary>
    /// <param name="connectionId">Which connection.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<bool> DeleteConnectionAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        var removed = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM ops.[Connection] WHERE ConnectionId = @connectionId;",
            new { connectionId },
            cancellationToken: cancellationToken));

        return removed > 0;
    }

    /// <summary>Records a connection, and the reference to its credentials.</summary>
    /// <remarks>
    /// The secret itself never arrives here. The caller has already put it in the vault and
    /// what lands in this row is the reference, because a connection row is read by every
    /// screen and included in every export.
    /// </remarks>
    /// <param name="connection">The connection to store.</param>
    /// <param name="createdBy">Who added it.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task CreateConnectionAsync(
        Connection connection, string createdBy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var sql = Connect();

        await sql.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO ops.[Connection]
                (ConnectionId, EngagementId, Mode, Name, EnvironmentRole, SettingsJson,
                 SecretRef, SecretExpiresUtc, CreatedBy)
            VALUES
                (@ConnectionId, @EngagementId, @Mode, @Name, @EnvironmentRole, @SettingsJson,
                 @SecretRef, @SecretExpiresUtc, @createdBy);
            """,
            new
            {
                connection.ConnectionId,
                connection.EngagementId,
                connection.Mode,
                connection.Name,
                connection.EnvironmentRole,
                connection.SettingsJson,
                connection.SecretRef,
                connection.SecretExpiresUtc,
                createdBy
            },
            cancellationToken: cancellationToken));
    }

    /// <summary>Removes an engagement and everything hanging off it.</summary>
    /// <remarks>
    /// The cascades in the schema do the work: connections, runs and everything a run found
    /// go with it. Deliberately not a soft delete. An engagement is a client's estate, and a
    /// row that is invisible but still present is the kind of thing that turns up in a
    /// backup two years later.
    /// </remarks>
    /// <param name="engagementId">Which engagement.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>How many engagements were removed, which is zero or one.</returns>
    public async Task<int> DeleteEngagementAsync(Guid engagementId, CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM ops.Engagement WHERE EngagementId = @engagementId;",
            new { engagementId },
            cancellationToken: cancellationToken));
    }

    /// <summary>One engagement, by its identifier.</summary>
    /// <remarks>
    /// Without an access check, because the worker is not a person: it runs a command that a
    /// person already had the right to queue. Every entry point a person reaches goes through
    /// ListEngagementsAsync, which filters by what they hold.
    /// </remarks>
    /// <summary>
    /// Records every solution a run found, and which of them start ticked.
    /// </summary>
    /// <remarks>
    /// Every solution, not only the chosen ones. A report covering four of nineteen solutions
    /// and a report covering all nineteen look identical on the cover page, so the list of
    /// what was there and deliberately not read is the only thing that tells them apart
    /// afterwards.
    ///
    /// Replaces rather than merges. A run listed twice, because somebody resumed it an hour
    /// later, should describe the environment as it is now rather than the union of two
    /// moments.
    /// </remarks>
    /// <param name="runId">Which run.</param>
    /// <param name="solutions">What the environment reported, with the default tick.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task RecordRunSolutionsAsync(
        Guid runId, IReadOnlyList<RunSolution> solutions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(solutions);

        await using var connection = Connect();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM ops.RunSolution WHERE RunId = @runId;",
            new { runId }, transaction, cancellationToken: cancellationToken));

        foreach (var solution in solutions)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                $"""
                 INSERT INTO ops.RunSolution ({RunSolutionColumns})
                 VALUES (@RunId, @UniqueName, @FriendlyName, @Version, @IsManaged, @PublisherPrefix,
                         @PublisherName, @ComponentCount, @IsFirstParty, @IsSelected);
                 """,
                solution with { RunId = runId }, transaction, cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>What a run found, in the order somebody would want to tick it.</summary>
    /// <remarks>
    /// The client's own solutions first, biggest first within that. The list exists to be
    /// read and ticked, and the thing somebody came to analyse should not be below forty rows
    /// of Microsoft's.
    /// </remarks>
    /// <param name="runId">Which run.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<IReadOnlyList<RunSolution>> ListRunSolutionsAsync(
        Guid runId, CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        var rows = await connection.QueryAsync<RunSolution>(new CommandDefinition(
            $"""
             SELECT {RunSolutionColumns} FROM ops.RunSolution
             WHERE RunId = @runId
             ORDER BY IsFirstParty, ISNULL(ComponentCount, 0) DESC, UniqueName;
             """,
            new { runId },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    /// <summary>
    /// Records what somebody chose, and releases the run to carry on.
    /// </summary>
    /// <remarks>
    /// One transaction. A selection recorded without the command that resumes the run leaves
    /// a run paused for ever against a question that has been answered, and a command without
    /// the selection resumes it against the question nobody answered.
    /// </remarks>
    /// <param name="runId">Which run.</param>
    /// <param name="selected">The unique names that were ticked.</param>
    /// <param name="checks">Which optional checks to run, null meaning the mode's default.</param>
    /// <param name="chosenBy">Who chose.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task RecordSelectionAsync(
        Guid runId,
        IReadOnlyCollection<string> selected,
        RunCheckChoices checks,
        string chosenBy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(checks);

        await using var connection = Connect();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // Everything to false first, then the ticked ones to true. Setting only the ticked
        // ones would leave a box unticked on a second pass still reading as selected from
        // the first.
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE ops.RunSolution SET IsSelected = 0 WHERE RunId = @runId;",
            new { runId }, transaction, cancellationToken: cancellationToken));

        if (selected.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE ops.RunSolution SET IsSelected = 1 WHERE RunId = @runId AND UniqueName IN @selected;",
                new { runId, selected }, transaction, cancellationToken: cancellationToken));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            MERGE ops.RunSelection AS target
            USING (SELECT @runId AS RunId) AS source ON target.RunId = source.RunId
            WHEN MATCHED THEN UPDATE SET
                RunsChecker = @runsChecker, ModelEstimates = @modelEstimates,
                EnvironmentHealth = @environmentHealth, ChosenUtc = SYSUTCDATETIME(), ChosenBy = @chosenBy
            WHEN NOT MATCHED THEN
                INSERT (RunId, RunsChecker, ModelEstimates, EnvironmentHealth, ChosenBy)
                VALUES (@runId, @runsChecker, @modelEstimates, @environmentHealth, @chosenBy);
            """,
            new
            {
                runId,
                runsChecker = checks.SolutionChecker,
                modelEstimates = checks.ModelEstimates,
                environmentHealth = checks.EnvironmentHealth,
                chosenBy
            },
            transaction, cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE ops.AnalysisRun SET Status = 'pending' WHERE RunId = @runId AND Status = 'awaitingSelection';",
            new { runId }, transaction, cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO ops.RunCommand (CommandId, RunId, Command, RequestedBy)
            VALUES (@commandId, @runId, 'resume', @chosenBy);
            """,
            new { commandId = Guid.NewGuid(), runId, chosenBy },
            transaction, cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// What a run was told to do, or null when nobody has said yet.
    /// </summary>
    /// <remarks>
    /// Null is the whole point. It is what tells the pipeline to stop and ask rather than to
    /// read everything, and it is a different answer from "none of them".
    /// </remarks>
    /// <param name="runId">Which run.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<RunSelection?> GetSelectionAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        var checks = await connection.QuerySingleOrDefaultAsync<RunCheckChoices>(new CommandDefinition(
            """
            SELECT RunsChecker AS SolutionChecker, ModelEstimates, EnvironmentHealth
            FROM ops.RunSelection WHERE RunId = @runId;
            """,
            new { runId },
            cancellationToken: cancellationToken));

        if (checks is null) return null;

        var chosen = await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT UniqueName FROM ops.RunSolution WHERE RunId = @runId AND IsSelected = 1;",
            new { runId },
            cancellationToken: cancellationToken));

        return new RunSelection(checks, chosen.ToList());
    }

    /// <summary>
    /// Forgets a stage and everything recorded after it, so a run can be resumed from there.
    /// </summary>
    /// <remarks>
    /// Everything after it, not only the stage named, and that is the whole point. A stage
    /// re-run on its own would write over what the stages behind it had already consumed: the
    /// findings would come from one extraction and the score from another, the run would look
    /// perfectly healthy, and nothing on the screen would say the two halves disagreed.
    ///
    /// Ordered by the stage list the caller passes rather than by a column, because the order
    /// of the pipeline is the pipeline's to know. A second copy of it here is a second copy
    /// to fall out of step.
    /// </remarks>
    /// <param name="runId">Which run.</param>
    /// <param name="order">Every stage id, in pipeline order.</param>
    /// <param name="fromStageId">The stage to run again.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task ClearStagesFromAsync(
        Guid runId, IReadOnlyList<string> order, string fromStageId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);

        var at = order.ToList().IndexOf(fromStageId);

        if (at < 0)
        {
            throw new InvalidOperationException(
                $"'{fromStageId}' is not a stage of this pipeline, so there is nothing to run again from.");
        }

        var discarded = order.Skip(at).ToList();

        await using var connection = Connect();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM ops.RunStage WHERE RunId = @runId AND StageId IN @discarded;",
            new { runId, discarded }, transaction, cancellationToken: cancellationToken));

        // Back to pending, so the screen stops showing a finished run while the worker is
        // about to start doing it again.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE ops.AnalysisRun
            SET Status = 'pending', CompletedUtc = NULL, Error = NULL
            WHERE RunId = @runId;
            """,
            new { runId }, transaction, cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes a run and everything it produced.
    /// </summary>
    /// <remarks>
    /// Almost every table hangs off the run with a cascade, so this is mostly one delete.
    /// Two things do not, and both are deliberate.
    ///
    /// findings.Estimate has no cascade, so it is deleted explicitly. Left out, the delete
    /// fails on a foreign key with a message naming a constraint rather than a reason.
    ///
    /// A run that another run was based on cannot go. A publish records what it wrote from
    /// the assessment somebody approved, and removing that assessment would leave a record
    /// of work items raised from nothing. The caller is told which run is holding it.
    ///
    /// What this cannot undo is a publish. Work items already in Azure DevOps or Jira stay
    /// exactly where they are; only this product's record of having written them goes. The
    /// count comes back so the caller can say so before anybody presses the button.
    /// </remarks>
    /// <param name="runId">Which run.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>How many published work items the deleted run had recorded.</returns>
    /// <exception cref="InvalidOperationException">Another run is based on this one.</exception>
    public async Task<int> DeleteRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var connection = Connect();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var derived = await connection.QueryAsync<string>(new CommandDefinition(
            """
            SELECT CONVERT(nvarchar(36), RunId) FROM ops.AnalysisRun WHERE BasedOnRunId = @runId;
            """,
            new { runId }, transaction, cancellationToken: cancellationToken));

        var blocking = derived.ToList();

        if (blocking.Count > 0)
        {
            throw new InvalidOperationException(
                $"{blocking.Count} later run(s) were made from this one and would be left describing "
                + $"an assessment that no longer exists: {string.Join(", ", blocking)}. Remove those first.");
        }

        var published = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM findings.PublishedWorkItem WHERE RunId = @runId;",
            new { runId }, transaction, cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM findings.Estimate WHERE RunId = @runId;",
            new { runId }, transaction, cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM ops.AnalysisRun WHERE RunId = @runId;",
            new { runId }, transaction, cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return published;
    }

    /// <summary>
    /// Asks the worker to do something to a run that already exists.
    /// </summary>
    /// <param name="runId">Which run.</param>
    /// <param name="command">start, resume, retryStage or cancel.</param>
    /// <param name="stageId">Which stage, on a retry.</param>
    /// <param name="requestedBy">Who asked.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task QueueCommandAsync(
        Guid runId, string command, string? stageId, string requestedBy, CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO ops.RunCommand (CommandId, RunId, Command, StageId, RequestedBy)
            VALUES (@commandId, @runId, @command, @stageId, @requestedBy);
            """,
            new { commandId = Guid.NewGuid(), runId, command, stageId, requestedBy },
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Every stage of a run with where it got to, for the screen that watches one.
    /// </summary>
    /// <remarks>
    /// The whole row rather than the identifiers. Watching a run is what a consultant does
    /// while a client waits, and "extract, running, four minutes" is the difference between a
    /// product that is working and one that has hung.
    /// </remarks>
    /// <param name="runId">Which run.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<IReadOnlyList<RunStageState>> ListStagesAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        var rows = await connection.QueryAsync<RunStageState>(new CommandDefinition(
            """
            SELECT StageId, Status, Attempt, StartedUtc, CompletedUtc, Error
            FROM ops.RunStage WHERE RunId = @runId;
            """,
            new { runId },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    /// <param name="engagementId">Which engagement.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<Engagement?> GetEngagementAsync(Guid engagementId, CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        // The shared column list, not a second one written out by hand.
        //
        // This selected CreatedBy as well, which the record does not have, so Dapper could
        // not find a constructor for the nine columns and threw. It is on the worker's
        // setup path, which means every run ever queued died before its first stage with a
        // message about constructors rather than about a column.
        //
        // The existing guard only catches SELECT *, and this was an explicit list that had
        // simply drifted from the record beside it. One constant cannot drift.
        return await connection.QuerySingleOrDefaultAsync<Engagement>(new CommandDefinition(
            $"SELECT {EngagementColumns} FROM ops.Engagement WHERE EngagementId = @engagementId;",
            new { engagementId },
            cancellationToken: cancellationToken));
    }
}
