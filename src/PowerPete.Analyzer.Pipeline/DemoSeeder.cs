namespace PowerPete.Analyzer.Pipeline;

using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using PowerPete.Analyzer.Data;

/// <summary>
/// Writes the demonstration engagement into the database.
/// </summary>
/// <remarks>
/// Runs at startup, every time, and does nothing when the stamped seed version matches the
/// compiled one. Rebuilding on a version change rather than on absence is what keeps the
/// demonstration current: an engagement that is only created when it is missing stays frozen
/// at whatever it looked like the first time a container started, and never gains what a
/// later release added.
///
/// Every write is scoped to the demonstration identifiers in <see cref="DemoEstate"/>, so
/// running this against a database full of client engagements cannot touch one. That is not
/// a theoretical concern: this runs on every container start in every environment.
///
/// It writes through <see cref="StorePersistence"/>, the same adapter the worker writes a
/// real run through. A seeder with its own INSERT statements is a second writer that drifts
/// from the first, and the drift is only ever found by a screen that renders wrongly for the
/// demonstration and correctly for everybody else.
/// </remarks>
/// <param name="connectionString">The product database.</param>
public sealed class DemoSeeder(string connectionString)
{
    /// <summary>
    /// Creates or refreshes the demonstration engagement.
    /// </summary>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Whether anything was written.</returns>
    public async Task<bool> EnsureAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Created here as well as in a migration, so a database whose migrations have not
        // been applied yet repairs its demonstration rather than failing at startup.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            IF OBJECT_ID('ops.DemoSeed', 'U') IS NULL
            CREATE TABLE ops.DemoSeed (
                EngagementId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_DemoSeed PRIMARY KEY,
                SeedVersion  INT              NOT NULL,
                AppliedUtc   DATETIME2(3)     NOT NULL);
            """,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        // Joined to the engagement rather than read on its own. A stamp whose engagement
        // somebody has deleted is a stamp that would stop the demonstration ever coming back.
        var stamped = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            """
            SELECT seed.SeedVersion
            FROM ops.DemoSeed seed
            INNER JOIN ops.Engagement engagement ON engagement.EngagementId = seed.EngagementId
            WHERE seed.EngagementId = @id;
            """,
            new { id = DemoEstate.EngagementId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (stamped == DemoEstate.SeedVersion) return false;

        var estate = DemoEstate.Build();

        await ClearAsync(connection, cancellationToken).ConfigureAwait(false);
        await WriteEngagementAsync(connection, cancellationToken).ConfigureAwait(false);

        var persistence = new StorePersistence(
            new AnalysisStore(connectionString),
            new WorkspaceStore(connectionString));

        await persistence.SaveSolutionsAsync(DemoEstate.RunId, estate.Solutions, cancellationToken).ConfigureAwait(false);
        await persistence.SaveComponentsAsync(DemoEstate.RunId, estate.Components, cancellationToken).ConfigureAwait(false);

        foreach (var (typeId, evidence, succeeded, count, reason) in estate.Reads)
        {
            await persistence.RecordReadAsync(DemoEstate.RunId, typeId, evidence, succeeded, count, reason, cancellationToken)
                .ConfigureAwait(false);
        }

        await persistence.SaveFindingsAsync(DemoEstate.RunId, DemoEstate.EngagementId, estate.Findings, cancellationToken)
            .ConfigureAwait(false);

        await persistence.SaveNotAssessedAsync(DemoEstate.RunId, estate.NotAssessed, cancellationToken).ConfigureAwait(false);

        await persistence.SaveScoreAsync(DemoEstate.RunId, DemoEstate.EngagementId, estate.Score,
            estate.Customisation, estate.Roadmap, cancellationToken).ConfigureAwait(false);

        await persistence.SaveBacklogAsync(DemoEstate.RunId, DemoEstate.EngagementId, estate.Backlog, cancellationToken)
            .ConfigureAwait(false);

        await ApproveAsync(connection, estate, cancellationToken).ConfigureAwait(false);
        await StampAsync(connection, cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Removes the previous demonstration, in the order the foreign keys allow.
    /// </summary>
    /// <remarks>
    /// Deleted and rewritten rather than merged. A demonstration assembled from a half
    /// removed previous version is worse than no demonstration, because it looks plausible.
    ///
    /// Two tables are deleted by hand before the engagement goes. Both of them reference the
    /// run without a cascade, deliberately: they already cascade from the run through another
    /// table, and a second path from the same run is something SQL Server refuses to create
    /// at all. The consequence is that the run cannot be deleted while their rows exist, and
    /// the consequence of forgetting that here is a container that fails to start.
    /// </remarks>
    private static async Task ClearAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE estimate
            FROM findings.Estimate estimate
            INNER JOIN ops.AnalysisRun run ON run.RunId = estimate.RunId
            WHERE run.EngagementId = @id;

            DELETE link
            FROM findings.BacklogItemFinding link
            INNER JOIN findings.BacklogItem item ON item.BacklogItemId = link.BacklogItemId
            INNER JOIN ops.AnalysisRun run ON run.RunId = item.RunId
            WHERE run.EngagementId = @id;

            DELETE reference
            FROM inv.UnresolvedReference reference
            INNER JOIN ops.AnalysisRun run ON run.RunId = reference.RunId
            WHERE run.EngagementId = @id;

            DELETE FROM ops.Engagement WHERE EngagementId = @id;
            """,
            new { id = DemoEstate.EngagementId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static async Task WriteEngagementAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        // The approval below has a foreign key to a real system user, because in every other
        // case an approval is a person taking responsibility and the database is right to
        // insist on knowing who. Nobody approved this one, so rather than borrow a
        // consultant's name the seeder admits itself under a name that cannot be mistaken for
        // a person. It is not a global administrator and it cannot sign in.
        //
        // No EngagementAccess rows. Everybody admitted to the product is granted reader on
        // this engagement in one place, in the access store, so it cannot drift, cannot be
        // revoked by accident and does not need repairing when somebody new is admitted.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            MERGE ops.SystemUser AS target
            USING (SELECT 'system' AS UserId) AS source ON target.UserId = source.UserId
            WHEN NOT MATCHED THEN INSERT (UserId, DisplayName, Email, IsGlobalAdmin, CreatedBy)
                VALUES ('system', 'Demonstration seed (not a person)', NULL, 0, 'system');

            INSERT INTO ops.Engagement
                (EngagementId, Name, ClientName, Status, IsRegulated, ReportLanguage, BacklogLanguage, CreatedBy)
            VALUES
                (@id, @name, @clientName, 'active', 0, 'en', 'en', 'system');

            INSERT INTO ops.[Connection]
                (ConnectionId, EngagementId, Mode, Name, EnvironmentRole, SettingsJson, LastTestedUtc,
                 LastTestSucceeded, LastTestMessage, LastTestIdentity, ReachJson, CreatedBy)
            VALUES
                (@connectionId, @id, 'servicePrincipal', @connectionName, @environmentRole, @settings,
                 SYSUTCDATETIME(), 1, @testMessage, @identity, @reach, 'system');

            INSERT INTO ops.AnalysisRun
                (RunId, EngagementId, Mode, Status, SourceConnectionId, CreatedBy, StartedUtc, CompletedUtc)
            VALUES
                (@runId, @id, 'assessment', 'partial', @connectionId, 'system',
                 DATEADD(minute, -37, SYSUTCDATETIME()), DATEADD(minute, -23, SYSUTCDATETIME()));
            """,
            new
            {
                id = DemoEstate.EngagementId,
                name = DemoEstate.Name,
                clientName = DemoEstate.ClientName,
                connectionId = DemoEstate.ConnectionId,
                connectionName = DemoEstate.ConnectionName,
                environmentRole = DemoEstate.EnvironmentRole,

                // Shaped like a real connection's settings and pointing at nothing. There is
                // no secret reference, because there is no secret: this connection has never
                // been used to reach anything and cannot be.
                settings = JsonSerializer.Serialize(new
                {
                    tenantId = "00000000-0000-0000-0000-000000000000",
                    clientId = "00000000-0000-0000-0000-000000000000",
                    environmentUrl = "https://northwind-utilities.crm4.dynamics.example",
                    isDemonstration = true
                }),
                testMessage = "This is the demonstration estate. It is synthetic, it reaches nothing, and no credential is stored against it.",
                identity = "demo-application-user@northwind-utilities.example",
                reach = JsonSerializer.Serialize(new
                {
                    metadata = "full",
                    solutionZip = "full",
                    checker = "full",
                    runtime = "full"
                })
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        // The stages, so the run detail screen shows a pipeline that ran rather than an empty
        // list. Partial on analyse, which is the truth: rules went unassessed.
        var stages = new (string Id, string Status)[]
        {
            ("extract", "succeeded"),
            ("checker", "succeeded"),
            ("resolve", "succeeded"),
            ("analyse", "partial"),
            ("estimate", "succeeded"),
            ("score", "succeeded"),
            ("backlog", "succeeded"),
            ("publish", "skipped")
        };

        foreach (var (stageId, status) in stages)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO ops.RunStage (RunId, StageId, Status, Attempt, StartedUtc, CompletedUtc, Error)
                VALUES (@runId, @stageId, @status, 1,
                        DATEADD(minute, -37, SYSUTCDATETIME()), DATEADD(minute, -23, SYSUTCDATETIME()), @error);
                """,
                new
                {
                    runId = DemoEstate.RunId,
                    stageId,
                    status,
                    error = status == "partial"
                        ? "Some checks could not run against this estate and are named in the report."
                        : null
                },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Approves the backlog, so the demonstration shows an engagement past the gate.
    /// </summary>
    /// <remarks>
    /// The publish stage is still skipped and there is no Azure DevOps connection on this
    /// engagement, so nothing can be published from it. What the approval buys is the half of
    /// the product that only appears once somebody has approved something: the backlog screen
    /// in its approved state, and the hash that binds it.
    /// </remarks>
    private static async Task ApproveAsync(SqlConnection connection, DemoEstate.Result estate, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO ops.RunApproval (RunId, BacklogHash, ItemCount, ApprovedBy, ApprovedByName)
            VALUES (@runId, @hash, @count, 'system', @name);
            """,
            new
            {
                runId = DemoEstate.RunId,
                hash = estate.BacklogHash,
                count = estate.Backlog.Count,
                name = "Demonstration"
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static async Task StampAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            """
            MERGE ops.DemoSeed AS target
            USING (SELECT @id AS EngagementId) AS source ON target.EngagementId = source.EngagementId
            WHEN MATCHED THEN UPDATE SET SeedVersion = @version, AppliedUtc = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT (EngagementId, SeedVersion, AppliedUtc)
                VALUES (@id, @version, SYSUTCDATETIME());
            """,
            new { id = DemoEstate.EngagementId, version = DemoEstate.SeedVersion },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>A line for the startup log, so a deployment says what it did.</summary>
    /// <param name="written">What <see cref="EnsureAsync"/> returned.</param>
    public static string Describe(bool written) => written
        ? string.Create(CultureInfo.InvariantCulture,
            $"Demonstration engagement rebuilt at seed version {DemoEstate.SeedVersion}.")
        : "Demonstration engagement is current.";
}
