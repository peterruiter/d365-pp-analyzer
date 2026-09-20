namespace PowerPete.Analyzer.Api;

using System.Globalization;
using Azure;
using Azure.Security.KeyVault.Secrets;
using Dapper;
using Microsoft.Data.SqlClient;
using PowerPete.Analyzer.Data;
using PowerPete.Analyzer.Domain;
using PowerPete.Analyzer.Export.Pdf;
using PowerPete.Analyzer.Pipeline;

/// <summary>How a part of the system is doing.</summary>
public enum HealthState
{
    /// <summary>Present, reachable, and this identity is allowed to use it.</summary>
    Ok,

    /// <summary>Working, but something about it will bite later.</summary>
    Degraded,

    /// <summary>Absent, unreachable, or refused.</summary>
    Failed,

    /// <summary>Deliberately not configured. Not a fault.</summary>
    NotConfigured
}

/// <summary>
/// One thing that was checked, and what to do when it is wrong.
/// </summary>
/// <param name="Id">Stable identifier, also the repair target.</param>
/// <param name="Name">What a reader calls this part.</param>
/// <param name="Group">Which section of the page it belongs under.</param>
/// <param name="State">How it is doing.</param>
/// <param name="Detail">What was actually observed, in plain terms.</param>
/// <param name="Remediation">What to do about it, when it is not Ok.</param>
/// <param name="Command">A command that fixes it, when one exists.</param>
/// <param name="CanRepair">Whether this page can fix it without anybody leaving the screen.</param>
public sealed record SystemCheck(
    string Id,
    string Name,
    string Group,
    HealthState State,
    string Detail,
    string? Remediation = null,
    string? Command = null,
    bool CanRepair = false);

/// <summary>
/// Whether this deployment is actually wired up.
/// </summary>
/// <remarks>
/// Every check does the thing rather than reading configuration that says it could. A
/// connection string naming a database proves nothing; selecting a row from it proves the
/// database exists, that the network reaches it and that this identity is allowed to read.
/// Those are three different failures with three different fixes, and a page that cannot
/// tell them apart sends somebody to the wrong place.
///
/// Nothing here throws. A failed check is the output, not an error: this page exists
/// precisely for the moment when something is broken, and a health page that returns a 500
/// when the system is unhealthy is the one thing it must never do.
/// </remarks>
public sealed class SystemHealth(IConfiguration configuration)
{
    /// <summary>What must work at all.</summary>
    public const string PlatformGroup = "platform";

    /// <summary>Inside this many days of expiry, a lapsing secret is a failure rather than a warning.</summary>
    /// <remarks>
    /// Two weeks, because rotating a secret needs somebody with rights on the application
    /// registration and a deployment, and neither is always available the same afternoon.
    /// </remarks>
    private const int UrgentSecretDays = 14;

    /// <summary>Inside this many days, it is worth somebody knowing.</summary>
    /// <remarks>
    /// Sixty, which is roughly a planning cycle. Earlier than that and the warning is noise
    /// that teaches people to ignore this page.
    /// </remarks>
    private const int WarnSecretDays = 60;

    /// <summary>What it reads from.</summary>
    public const string SourcesGroup = "sources";

    /// <summary>What it writes to.</summary>
    public const string TargetGroup = "target";

    /// <summary>What does the work.</summary>
    public const string PipelineGroup = "pipeline";

    /// <summary>What comes out.</summary>
    public const string DeliverablesGroup = "deliverables";

    private string? ConnectionString => configuration.GetConnectionString("Analyzer");

    /// <summary>Runs every check, concurrently where they do not depend on each other.</summary>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<IReadOnlyList<SystemCheck>> RunAsync(CancellationToken cancellationToken)
    {
        var checks = await Task.WhenAll(
            DatabaseAsync(cancellationToken),
            SchemaAsync(cancellationToken),
            VaultAsync(cancellationToken),
            SignInAsync(cancellationToken),
            WorkerAsync(cancellationToken),
            StuckRunsAsync(cancellationToken));

        return
        [
            .. checks,
            SignInSecret(),
            Reporting(),
            PdfRenderer(),
            Documentation()
        ];
    }

    // ------------------------------------------------------------- platform --

    private async Task<SystemCheck> DatabaseAsync(CancellationToken cancellationToken)
    {
        if (ConnectionString is not { Length: > 0 } connectionString)
        {
            return new SystemCheck("database", "Database", PlatformGroup, HealthState.Failed,
                "No connection string is configured, so this deployment is a contract browser.",
                "Set ConnectionStrings__Analyzer on the container app.");
        }

        try
        {
            await using var connection = new SqlConnection(connectionString);

            // Selecting a row, not opening a connection. Opening proves the network; reading
            // proves this identity was actually admitted to the database.
            var engagements = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM ops.Engagement;", cancellationToken: cancellationToken));

            return new SystemCheck("database", "Database", PlatformGroup, HealthState.Ok,
                string.Create(CultureInfo.InvariantCulture,
                    $"Reachable, and this identity can read it. {engagements} engagements."));
        }
        catch (SqlException failure) when (failure.Number is 18456 or 4060)
        {
            return new SystemCheck("database", "Database", PlatformGroup, HealthState.Failed,
                "The database answered and refused this identity. That is a permissions problem, not a network one.",
                "The managed identity needs a contained database user.",
                "./build/Grant-DatabaseAccess.ps1 -SqlServer <server> -IdentityName <prefix>-identity");
        }
        catch (SqlException failure)
        {
            return new SystemCheck("database", "Database", PlatformGroup, HealthState.Failed,
                failure.Message,
                "A serverless database that has paused takes about a minute to wake. If this persists, check the firewall.");
        }
    }

    private async Task<SystemCheck> SchemaAsync(CancellationToken cancellationToken)
    {
        if (ConnectionString is not { Length: > 0 } connectionString)
        {
            return new SystemCheck("schema", "Schema", PlatformGroup, HealthState.NotConfigured,
                "There is no database to compare against.");
        }

        try
        {
            var shipped = DatabaseMigrator.LoadMigrations().Select(migration => migration.Name).ToHashSet(StringComparer.Ordinal);

            await using var connection = new SqlConnection(connectionString);

            var applied = (await connection.QueryAsync<string>(new CommandDefinition(
                "SELECT Name FROM ops.SchemaMigration;", cancellationToken: cancellationToken)))
                .ToHashSet(StringComparer.Ordinal);

            var missing = shipped.Except(applied).Order(StringComparer.Ordinal).ToList();

            if (missing.Count == 0)
            {
                return new SystemCheck("schema", "Schema", PlatformGroup, HealthState.Ok,
                    string.Create(CultureInfo.InvariantCulture,
                        $"All {shipped.Count} migrations are applied."));
            }

            // Failed rather than degraded. An image carrying a migration the database has not
            // applied will write to a table that does not exist, and it will do it during a
            // client's discovery rather than at startup.
            return new SystemCheck("schema", "Schema", PlatformGroup, HealthState.Failed,
                $"This image carries {missing.Count} migrations the database has not applied: {string.Join(", ", missing)}.",
                "Applying migrations is a person's job, done as themselves. The product does not alter its own schema.",
                "./build/Initialize-Database.ps1");
        }
        catch (SqlException failure)
        {
            return new SystemCheck("schema", "Schema", PlatformGroup, HealthState.Failed,
                failure.Message,
                "If ops.SchemaMigration does not exist, no migration has ever been applied.",
                "./build/Initialize-Database.ps1");
        }
    }

    private async Task<SystemCheck> VaultAsync(CancellationToken cancellationToken)
    {
        var uri = DeploymentSettings.Read(DeploymentSettings.KeyVaultUri, name => configuration[name]);

        if (string.IsNullOrWhiteSpace(uri))
        {
            // Failed, not NotConfigured. Without a vault no connection needing a credential
            // can be created, which is every connector except the file drop.
            return new SystemCheck("vault", "Key Vault", PlatformGroup, HealthState.Failed,
                "No vault is configured, so there is nowhere to put a client's credentials. " +
                "The product refuses to create a connection that needs one rather than putting it in the database.",
                "Set KeyVault__Uri on the container app.",
                "./build/Deploy-Infrastructure.ps1");
        }

        try
        {
            var client = new SecretClient(new Uri(uri), Credential());

            // Written, not listed. Listing proves the vault exists; writing proves this
            // identity holds Secrets Officer, which is what saving a connection needs and the
            // thing that is actually missing when it is missing.
            //
            // Written and left, not written and deleted. Soft delete is on, so a deleted
            // secret sits in a recoverable state and the same name cannot be used again until
            // it is purged: the check passed the first time and failed every time after, with
            // an error about a deleted secret that read like a fault in the vault. One probe
            // secret, overwritten, costs nothing and works every time.
            await client.SetSecretAsync("health-probe", "ok", cancellationToken);

            return new SystemCheck("vault", "Key Vault", PlatformGroup, HealthState.Ok,
                "Reachable, and this identity can write a secret to it.");
        }
        catch (RequestFailedException failure) when (failure.Status is 403)
        {
            return new SystemCheck("vault", "Key Vault", PlatformGroup, HealthState.Failed,
                "The vault answered and refused this identity. It can probably read and cannot write.",
                "The container's managed identity needs the Key Vault Secrets Officer role, not Secrets User. " +
                "Saving a connection writes a secret.");
        }
        catch (Exception failure) when (failure is RequestFailedException or InvalidOperationException or HttpRequestException)
        {
            return new SystemCheck("vault", "Key Vault", PlatformGroup, HealthState.Failed, failure.Message);
        }
    }

    private Task<SystemCheck> SignInAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        var tenant = configuration["AzureAd:TenantId"];
        var client = configuration["AzureAd:ClientId"];
        var admin = DeploymentSettings.Read(DeploymentSettings.InitialGlobalAdmin, name => configuration[name]);

        if (string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(client))
        {
            // The most serious thing this page can report. Without a tenant the guard is not
            // installed at all and every caller is admitted as a global administrator.
            return Task.FromResult(new SystemCheck("signin", "Sign in", PlatformGroup, HealthState.Failed,
                "No Entra application is configured. Every caller is admitted as a global administrator, " +
                "on whatever address this is reachable at.",
                "Nothing belonging to a client may go near this deployment until an application is registered.",
                "./build/Deploy-Infrastructure.ps1 -AzureAdTenantId <tenant> -AzureAdClientId <app>"));
        }

        if (string.IsNullOrWhiteSpace(admin))
        {
            return Task.FromResult(new SystemCheck("signin", "Sign in", PlatformGroup, HealthState.Degraded,
                "Sign in is enforced, but no first global administrator is configured. If nobody has been " +
                "admitted yet, nobody can be.",
                "Set Access__InitialGlobalAdminUpn to a sign-in name."));
        }

        return Task.FromResult(new SystemCheck("signin", "Sign in", PlatformGroup, HealthState.Ok,
            $"Entra sign in is enforced, and {admin} is seeded as a global administrator."));
    }

    /// <summary>
    /// How long the Entra client secret has left.
    /// </summary>
    /// <remarks>
    /// The failure this exists to prevent is specific and nasty. A client secret does not
    /// degrade: it works perfectly until a date and then every sign in fails at once, for
    /// everybody, with an error that reads like an outage rather than like an expiry. The
    /// person best placed to fix it is usually the person locked out.
    ///
    /// The date is configured rather than read from Entra. Reading it would mean granting this
    /// application permission to read its own application registration, which is a standing
    /// permission held all year to answer a question asked once, and the answer is known at the
    /// moment the secret is created anyway.
    ///
    /// A date nobody recorded is itself a finding. Silence here would be indistinguishable from
    /// a secret with years left.
    /// </remarks>
    private SystemCheck SignInSecret()
    {
        const string id = "signin-secret";
        const string name = "Sign in secret";

        // Nothing to warn about when there is no application. The sign in check above already
        // reports that, in much stronger terms.
        if (string.IsNullOrWhiteSpace(configuration["AzureAd:TenantId"])
            || string.IsNullOrWhiteSpace(configuration["AzureAd:ClientId"]))
        {
            return new SystemCheck(id, name, PlatformGroup, HealthState.NotConfigured,
                "No Entra application is configured, so there is no secret to expire.");
        }

        var configured = configuration["AzureAd:ClientSecretExpiresUtc"];

        if (string.IsNullOrWhiteSpace(configured)
            || !DateTime.TryParse(configured, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var expires))
        {
            return new SystemCheck(id, name, PlatformGroup, HealthState.Degraded,
                "Nothing records when the client secret expires, so nothing can warn about it.",
                "A secret works perfectly until a date and then every sign in fails at once. Pass the expiry "
                + "on the next deployment so this page can count down to it.",
                "./build/Deploy-Infrastructure.ps1 -AzureAdClientSecretExpiresUtc 2027-09-16");
        }

        // Whole days between two dates rather than a floor of the elapsed time. Flooring a
        // TimeSpan makes every threshold fragile: an expiry set fifteen days out is already
        // 14.999 days away by the time the subtraction runs, which floors to fourteen and
        // turns a warning into a failure. It also matches what somebody means when they type
        // a date: the countdown is in days, not in hours.
        var days = (expires.Date - DateTime.UtcNow.Date).Days;
        var on = expires.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);

        if (days < 0)
        {
            return new SystemCheck(id, name, PlatformGroup, HealthState.Failed,
                string.Create(CultureInfo.InvariantCulture,
                    $"The client secret expired on {on}, {-days} days ago. Sign in is failing for everybody."),
                "Create a new secret on the application registration and deploy it. Nobody can sign in until "
                + "you do, including whoever normally would.");
        }

        if (days <= UrgentSecretDays)
        {
            return new SystemCheck(id, name, PlatformGroup, HealthState.Failed,
                string.Create(CultureInfo.InvariantCulture,
                    $"The client secret expires on {on}, in {days} days."),
                "Rotate it now. When it lapses every sign in fails at once, and the error reads like an "
                + "outage rather than like an expiry.",
                "./build/Deploy-Infrastructure.ps1 -AzureAdClientSecret <new secret> -AzureAdClientSecretExpiresUtc <date>");
        }

        if (days <= WarnSecretDays)
        {
            return new SystemCheck(id, name, PlatformGroup, HealthState.Degraded,
                string.Create(CultureInfo.InvariantCulture,
                    $"The client secret expires on {on}, in {days} days."),
                "Worth putting in somebody's calendar now rather than finding out on the morning it lapses.");
        }

        return new SystemCheck(id, name, PlatformGroup, HealthState.Ok,
            string.Create(CultureInfo.InvariantCulture,
                $"The client secret expires on {on}, in {days} days."));
    }

    // ------------------------------------------------------------- pipeline --

    private async Task<SystemCheck> WorkerAsync(CancellationToken cancellationToken)
    {
        if (ConnectionString is not { Length: > 0 } connectionString)
        {
            return new SystemCheck("worker", "Worker", PipelineGroup, HealthState.NotConfigured,
                "There is no command queue to watch.");
        }

        try
        {
            await using var connection = new SqlConnection(connectionString);

            var waiting = await connection.QuerySingleAsync<(int Unclaimed, int Stale, DateTime? LastClaim)>(
                new CommandDefinition(
                    """
                    SELECT
                        SUM(CASE WHEN ClaimedUtc IS NULL THEN 1 ELSE 0 END) AS Unclaimed,
                        SUM(CASE WHEN ClaimedUtc IS NULL AND RequestedUtc < DATEADD(minute, -5, SYSUTCDATETIME())
                                 THEN 1 ELSE 0 END) AS Stale,
                        MAX(ClaimedUtc) AS LastClaim
                    FROM ops.RunCommand;
                    """,
                    cancellationToken: cancellationToken));

            if (waiting.Stale > 0)
            {
                // The one symptom that means the worker is not running. An unclaimed command
                // seconds old is normal; one five minutes old is a container that is not there.
                return new SystemCheck("worker", "Worker", PipelineGroup, HealthState.Failed,
                    string.Create(CultureInfo.InvariantCulture,
                        $"{waiting.Stale} commands have been waiting more than five minutes. Nothing is claiming them."),
                    "The worker container app is not running, cannot reach the database, or is behind this image.",
                    "az containerapp replica list -n <prefix>-worker -g <group>");
            }

            var last = waiting.LastClaim is { } claimed
                ? string.Create(CultureInfo.InvariantCulture, $"Last claim {claimed:u}.")
                : "Nothing has ever been queued.";

            return new SystemCheck("worker", "Worker", PipelineGroup, HealthState.Ok,
                string.Create(CultureInfo.InvariantCulture, $"{waiting.Unclaimed} commands waiting. {last}"));
        }
        catch (SqlException failure)
        {
            return new SystemCheck("worker", "Worker", PipelineGroup, HealthState.Failed, failure.Message);
        }
    }

    private async Task<SystemCheck> StuckRunsAsync(CancellationToken cancellationToken)
    {
        if (ConnectionString is not { Length: > 0 } connectionString)
        {
            return new SystemCheck("runs", "Runs", PipelineGroup, HealthState.NotConfigured, "There is no database.");
        }

        try
        {
            await using var connection = new SqlConnection(connectionString);

            var stuck = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                -- AnalysisRun. This read ops.MigrationRun, which is the migrator's table
                -- and has never existed in this database, so the one check that watches for
                -- a wedged run has reported "not working" since the day it was written and
                -- the operations page has never been green.
                SELECT COUNT(*) FROM ops.AnalysisRun
                WHERE Status = 'running' AND StartedUtc < DATEADD(hour, -6, SYSUTCDATETIME());
                """,
                cancellationToken: cancellationToken));

            return stuck == 0
                ? new SystemCheck("runs", "Runs", PipelineGroup, HealthState.Ok, "No run has been going for more than six hours.")
                : new SystemCheck("runs", "Runs", PipelineGroup, HealthState.Degraded,
                    string.Create(CultureInfo.InvariantCulture,
                        $"{stuck} runs have been running for more than six hours."),
                    "A worker replaced mid-run leaves its run marked running. The ledger is intact, so the work is recoverable.");
        }
        catch (SqlException failure)
        {
            return new SystemCheck("runs", "Runs", PipelineGroup, HealthState.Failed, failure.Message);
        }
    }

    // --------------------------------------------------------- deliverables --

    private static SystemCheck Reporting() =>
        new("reporting", "Inventory workbook and runbook", DeliverablesGroup, HealthState.Ok,
            "Both are produced in process and need no licence.");

    /// <summary>
    /// Whether the PDF can be produced without a trial banner across it.
    /// </summary>
    /// <remarks>
    /// Three states rather than two, because the middle one is the dangerous one. A key that
    /// is accepted but covers only the UI components validates successfully and then stamps a
    /// watermark on every page, and the only place that would otherwise be noticed is a
    /// client opening the document.
    /// </remarks>
    /// <summary>
    /// Replaces the Syncfusion licence key, storing it where secrets belong.
    /// </summary>
    /// <remarks>
    /// Into Key Vault, never into the database and never into a configuration file in the
    /// repository. The container reads it from there on the next start, which is why this
    /// reports that a restart is needed rather than pretending the change is live.
    ///
    /// The key is checked before it is stored. A key issued for the UI components alone
    /// validates and then watermarks every page, and finding that out when a client opens the
    /// report is finding out too late.
    /// </remarks>
    /// <param name="key">The key, as Syncfusion issued it.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Whether it was stored, whether it covers the PDF library, and what to say.</returns>
    public async Task<(bool Stored, bool CoversPdf, string Note)> SaveSyncfusionKeyAsync(
        string key, CancellationToken cancellationToken)
    {
        var trimmed = key?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            return (false, false, "No key was supplied.");
        }

        // Registered first, so a key that does not cover document processing is refused here
        // rather than discovered by a client holding a watermarked report.
        SyncfusionLicence.Register(trimmed, force: true);
        var coversPdf = SyncfusionLicence.IsRegisteredForPdf;

        var vault = DeploymentSettings.Read(DeploymentSettings.KeyVaultUri, name => configuration[name]);

        if (string.IsNullOrWhiteSpace(vault))
        {
            return (false, coversPdf,
                "This deployment has no Key Vault, so there is nowhere to put the key. It is in use for as "
                + "long as this container runs and will be gone when it restarts.");
        }

        try
        {
            var client = new SecretClient(new Uri(vault), Credential());
            await client.SetSecretAsync("Syncfusion--LicenseKey", trimmed, cancellationToken);
        }
#pragma warning disable CA1031 // A failed save is output, not an exception.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            return (false, coversPdf, $"The key could not be written to Key Vault. {failure.Message}");
        }

        return (true, coversPdf,
            "Stored in Key Vault. It is in use now, and the container will read it from there on its next "
            + "start so it survives a restart.");
    }

    private SystemCheck PdfRenderer()
    {
        var configured = !string.IsNullOrWhiteSpace(configuration["Syncfusion:LicenseKey"])
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SyncfusionLicence.EnvironmentVariable));

        if (!configured)
        {
            return new SystemCheck("pdf", "PDF report", DeliverablesGroup, HealthState.Degraded,
                "No Syncfusion licence key is configured, so the PDF is not offered.",
                "The workbook and the runbook are unaffected. Pass -SyncfusionLicenseKey on the next deployment "
                + "to turn the PDF on.",
                "./build/Deploy-Infrastructure.ps1 -SyncfusionLicenseKey <key>");
        }

        return SyncfusionLicence.IsRegisteredForPdf
            ? new SystemCheck("pdf", "PDF report", DeliverablesGroup, HealthState.Ok,
                "The licence covers document processing, so the report renders without a watermark.")
            : new SystemCheck("pdf", "PDF report", DeliverablesGroup, HealthState.Failed,
                "The Syncfusion key was accepted but does not cover document processing.",
                "A key issued for the UI components alone validates and then watermarks every page. Ask "
                + "Syncfusion for one that includes the PDF library. The renderer refuses rather than "
                + "producing a watermarked document, so nothing has gone out.");
    }

    private static SystemCheck Documentation()
    {
        // Looked for on disk rather than assumed. The documentation screen reads these over
        // HTTP, and a container built without them serves a list of guides that all 404.
        //
        // Both places, because the API serves them from wwwroot in a container and from the
        // repository on a developer machine. Checking only the first reported a fault on
        // every laptop, which is a health page crying wolf.
        string[] roots =
        [
            Path.Combine(AppContext.BaseDirectory, "wwwroot", "documentation"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs")
        ];

        var guides = roots
            .Where(Directory.Exists)
            .Sum(root => Directory.GetFiles(root, "*.md").Length);

        return guides > 0
            ? new SystemCheck("documentation", "Documentation", DeliverablesGroup, HealthState.Ok,
                string.Create(CultureInfo.InvariantCulture, $"{guides} guides are being served."))
            : new SystemCheck("documentation", "Documentation", DeliverablesGroup, HealthState.Degraded,
                "No guides were found in wwwroot/documentation. Every guide on the documentation screen will fail to load.",
                "The image build copies docs/ into wwwroot. A container built without it serves an empty reader.");
    }

    /// <summary>
    /// How this application proves who it is to Azure.
    /// </summary>
    /// <remarks>
    /// The managed identity in Azure, and whatever the developer is signed in as on a laptop.
    /// Naming the client id matters: a container with a user assigned identity and no
    /// AZURE_CLIENT_ID authenticates as nothing in particular, and the failure reads as a
    /// permissions problem rather than a configuration one.
    /// </remarks>
    private Azure.Identity.DefaultAzureCredential Credential() =>
        configuration["AZURE_CLIENT_ID"] is { Length: > 0 } clientId
            ? new Azure.Identity.DefaultAzureCredential(
                new Azure.Identity.DefaultAzureCredentialOptions { ManagedIdentityClientId = clientId })
            : new Azure.Identity.DefaultAzureCredential();
}
