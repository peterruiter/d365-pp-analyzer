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
/// <param name="Command">start, retryStage, cancel, approve or publish.</param>
/// <param name="StageId">Which stage, on a retry.</param>
/// <param name="RequestedBy">Who asked.</param>
public sealed record RunCommand(Guid CommandId, Guid RunId, string Command, string? StageId, string RequestedBy);

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

        var sql = access.IsGlobalAdmin
            ? "SELECT * FROM ops.Engagement ORDER BY CreatedUtc DESC;"
            : """
              SELECT e.* FROM ops.Engagement e
              WHERE e.EngagementId IN @engagementIds
              ORDER BY e.CreatedUtc DESC;
              """;

        var rows = await connection.QueryAsync<Engagement>(new CommandDefinition(
            sql,
            new { engagementIds = access.Roles.Keys.ToArray() },
            cancellationToken: cancellationToken));

        return [.. rows];
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
            SELECT * FROM ops.Connection
            WHERE SecretExpiresUtc IS NOT NULL AND SecretExpiresUtc < @deadline
            ORDER BY SecretExpiresUtc;
            """,
            new { deadline = DateTime.UtcNow + within },
            cancellationToken: cancellationToken));

        return [.. rows];
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
            "SELECT * FROM ops.AnalysisRun WHERE RunId = @runId;",
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
    /// <param name="engagementId">Which engagement.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<Engagement?> GetEngagementAsync(Guid engagementId, CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        return await connection.QuerySingleOrDefaultAsync<Engagement>(new CommandDefinition(
            """
            SELECT EngagementId, Name, ClientName, Status, IsRegulated, ReportLanguage, BacklogLanguage,
                   CreatedUtc, CreatedBy
            FROM ops.Engagement WHERE EngagementId = @engagementId;
            """,
            new { engagementId },
            cancellationToken: cancellationToken));
    }
}
