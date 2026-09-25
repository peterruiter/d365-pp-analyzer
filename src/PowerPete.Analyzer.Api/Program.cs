using Azure;
using PowerPete.Analyzer.Domain.Localization;
using Azure.Identity;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.Globalization;
using PowerPete.Analyzer.Export;
using PowerPete.Analyzer.Export.Pdf;
using PowerPete.Analyzer.Api;
using Microsoft.AspNetCore.DataProtection;
using PowerPete.Analyzer.Data;
using PowerPete.Analyzer.DevOps;
using PowerPete.Analyzer.Dataverse;
using PowerPete.Analyzer.Domain;
using PowerPete.Analyzer.Pipeline;
using Microsoft.Data.SqlClient;

// The four modes ops.Connection is constrained to. Kept beside the endpoint that writes one
// so a mode the database would refuse is refused here, with a sentence rather than a 500.
// A file name a file system will accept and a person can read, from a client's name.
static string Slug(string value)
{
    var cleaned = new string([.. value.Trim().ToLowerInvariant()
        .Select(character => char.IsLetterOrDigit(character) ? character : '-')]);

    while (cleaned.Contains("--", StringComparison.Ordinal))
    {
        cleaned = cleaned.Replace("--", "-", StringComparison.Ordinal);
    }

    cleaned = cleaned.Trim('-');

    return cleaned.Length == 0 ? "engagement" : cleaned;
}

// Worst first. The catalogue declares the order and this keeps one copy of it, so a screen
// cannot sort findings differently from the report.
static int SeverityRank(string severity) => severity switch
{
    "critical" => 0,
    "high" => 1,
    "medium" => 2,
    "low" => 3,
    _ => 4
};

// Every mode a connection can be, which is the three ways in plus the two ways out.
// Publish targets live in the same table as sources and are told apart by this value.
string[] ExtractionModesList() => ["servicePrincipal", "delegated", "offlineZip", "azureDevOps", "jira", "github"];

// The modes that are somewhere a backlog goes rather than somewhere an estate is read. Held
// once: this used to be an equality check against azureDevOps in three places, and adding a
// second target would have meant finding all three.
string[] PublishTargets() => ["azureDevOps", "jira", "github"];

var builder = WebApplication.CreateBuilder(args);

// ------------------------------------------------------------------ settings --
var connectionString = builder.Configuration.GetConnectionString("Analyzer")
    ?? Environment.GetEnvironmentVariable("ANALYZER_SQL_CONNECTION")
    ?? throw new InvalidOperationException(
        "No connection string. The API cannot start without somewhere to read engagements from, and starting " +
        "anyway would mean a sign-in page in front of nothing.");

// Through DeploymentSettings rather than by name, because reading them by name here is
// what broke the deployment: the container was given KeyVault__Uri, this line asked for
// KeyVaultUri, and interactive sign-in failed at the last step saying no vault was
// configured while the operations page said one was.
string? Setting(DeploymentSetting setting) =>
    DeploymentSettings.Read(setting, name => builder.Configuration[name]);

// Before anything renders. The report, the workbook and the stylesheet below all read
// Brand, and a brand chosen after the first request would mean the first client to ask for
// a report got the wrong one.
PowerPete.Analyzer.Export.Brand.Use(Setting(DeploymentSettings.Brand));

var keyVaultUri = Setting(DeploymentSettings.KeyVaultUri);
var initialAdmin = Setting(DeploymentSettings.InitialGlobalAdmin);
var adminContact = Setting(DeploymentSettings.AdminContact) ?? string.Empty;

builder.Services.AddSingleton<DevOpsProjects>();
builder.Services.AddSingleton<JiraProjects>();
builder.Services.AddSingleton<GitHubRepositories>();
builder.Services.AddSingleton(new WorkspaceStore(connectionString));
builder.Services.AddSingleton(new AnalysisStore(connectionString));
builder.Services.AddSingleton(new AccessStore(connectionString));
builder.Services.AddSingleton(SecretStore.For(keyVaultUri));

// The same factory the worker authenticates with, so testing a connection from the screen
// and reading an environment on a run cannot disagree about whether a credential works.
builder.Services.AddSingleton<ConnectionFactory>();

// Every date this API returns is UTC and says so on the wire. Without this the browser reads
// a stage that started two hours ago as one that started now, which is how every running
// stage came to show an elapsed time of about two hours.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new UtcDateTimeConverter());
    options.SerializerOptions.Converters.Add(new NullableUtcDateTimeConverter());
});
builder.Services.AddSingleton<SystemHealth>();
builder.Services.AddSingleton<ReportComposer>();

// Where an uploaded solution file goes. Absent on a local run, which the endpoint reports
// rather than throwing: everything except the offline mode works without it.
var uploadContainer = Setting(DeploymentSettings.UploadContainer);

builder.Services.AddSingleton(new SolutionUploads(
    string.IsNullOrWhiteSpace(uploadContainer) ? null : new Uri(uploadContainer)));

// Interactive sign-in needs the product's own app registration, because it is the product
// asking for consent to read somebody's environment, not a registration per client.
//
// The keys that sign its state parameter are kept in blob storage and wrapped with a vault
// key. Left to itself ASP.NET writes them to /root/.aspnet/DataProtection-Keys inside the
// container: unencrypted, gone when the replica is replaced, and shared with none of the
// other replicas the API scales to. A sign-in begun on one and returned to another could
// not be unprotected, and the failure reads as a tampered request rather than as a missing
// key, which is close to undiagnosable from the outside.
//
// SetApplicationName is load bearing, not decoration. It is what makes the keys written by
// one revision readable by the next; without it every deployment would silently start a new
// ring in the same blob and invalidate every sign-in in flight.
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("PowerPete.Analyzer");

var keyBlob = Setting(DeploymentSettings.DataProtectionBlob);
var keyWrap = Setting(DeploymentSettings.DataProtectionKey);

if (!string.IsNullOrWhiteSpace(keyBlob))
{
    var credential = new DefaultAzureCredential();

    dataProtection.PersistKeysToAzureBlobStorage(new Uri(keyBlob), credential);

    if (!string.IsNullOrWhiteSpace(keyWrap))
    {
        dataProtection.ProtectKeysWithAzureKeyVault(new Uri(keyWrap), credential);
    }
    else
    {
        // Said out loud rather than accepted quietly. Persisting the keys without wrapping
        // them writes the material that signs every sign-in into a container whose role is
        // scoped to the whole storage account, which is a worse position than the one this
        // is fixing, not a better one.
        Console.Error.WriteLine(
            "DataProtection__BlobUri is set and DataProtection__KeyUri is not. The keys that sign a "
            + "sign-in will be written to blob storage unencrypted. Set both, or neither.");
    }
}
else
{
    // A local run, where the default is right: keys in the profile directory, one process,
    // nothing to share them with.
    Console.WriteLine(
        "No data protection blob is configured, so sign-in keys stay in this container. "
        + "Correct locally; on a deployment it means a sign-in breaks whenever a replica is replaced.");
}

builder.Services.AddHttpClient();

builder.Services.AddSingleton(new InteractiveSignIn.Options(
    builder.Configuration["AzureAd:Instance"] ?? DelegatedTokens.DefaultInstance,
    builder.Configuration["AzureAd:TenantId"],
    builder.Configuration["AzureAd:ClientId"],
    builder.Configuration["AzureAd:ClientSecret"]));

builder.Services.AddSingleton(provider => new InteractiveSignIn(
    provider.GetRequiredService<WorkspaceStore>(),
    provider.GetRequiredService<ISecretStore>(),
    provider.GetRequiredService<IDataProtectionProvider>(),
    provider.GetRequiredService<InteractiveSignIn.Options>(),
    provider.GetRequiredService<IHttpClientFactory>().CreateClient("entra")));

// ------------------------------------------------------------ authentication --
// Entra sign-in, cookie session. The product knows who somebody is from their token and what
// they may do from a row in the database, and the two are deliberately separate: anybody in
// the tenant can sign in, only an admitted person sees an engagement.
//
// Configured or not, the same way the sibling products decide it: a tenant and a client id
// present means sign in against Entra, absent means this is somebody's laptop. Every
// deployment of this product sets both, from Key Vault, so the local path cannot engage in a
// container without somebody changing the pipeline that publishes one.
var entraSection = builder.Configuration.GetSection("AzureAd");

var entraConfigured = !string.IsNullOrWhiteSpace(entraSection["TenantId"])
    && !string.IsNullOrWhiteSpace(entraSection["ClientId"]);

if (entraConfigured)
{
    builder.Services
        .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApp(entraSection);

    builder.Services.ConfigureApplicationCookie(options =>
    {
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
}
else
{
    // A scheme rather than an exemption. Every endpoint keeps its RequireAuthorization and
    // the whole authorisation pipeline still runs; what changes is only where the identity
    // came from. Removing the checks for a local run would mean the local run exercises a
    // different product from the deployed one, which is how a permissions bug ships.
    builder.Services
        .AddSingleton(new LocalSignInHandler.Identity(
            initialAdmin ?? "local@localhost",

            // Configurable because this name is on screen, and the screenshots on the
            // public site are taken from a local run. "Local development" across the top
            // of a picture on a marketing page is not what it is trying to say.
            Setting(DeploymentSettings.LocalSignInDisplayName) ?? "Local development"))
        .AddAuthentication(LocalSignInHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, LocalSignInHandler>(LocalSignInHandler.SchemeName, null);
}

builder.Services.AddAuthorization();
builder.Services.AddHealthChecks();

var app = builder.Build();

// The PDF licence, registered from configuration at startup.
//
// The renderer registers it itself, but only from the SYNCFUSION_LICENSE environment
// variable, which is right for a local run and wrong here: the deployment supplies it as
// Syncfusion__LicenseKey, because that is where Key Vault and the container app put a
// setting. So the key was present, the health check said so, and every PDF download failed
// with "no licence key is configured" anyway.
//
// Registering here rather than teaching the Export library about ASP.NET configuration. It
// has no dependency on the web stack and should not gain one to read a string.
if (builder.Configuration["Syncfusion:LicenseKey"] is { Length: > 0 } syncfusionKey)
{
    try
    {
        SyncfusionLicence.Register(syncfusionKey, force: true);
    }
    catch (InvalidOperationException exception)
    {
        app.Logger.LogError(exception, "The Syncfusion licence key was rejected. The PDF report will not render.");
    }
}

// The demonstration engagement, rebuilt whenever its seed version has moved.
//
// Before the pipeline rather than inside a hosted service, so a deployment that cannot write
// it says so in the startup log where somebody is already looking, rather than at three in
// the afternoon when a consultant opens the product in front of a client and finds it empty.
//
// It is allowed to fail. A demonstration is not worth refusing to start over: a database
// that has not been migrated yet, or a managed identity that has not been granted a login,
// should produce a product that works for everybody who already has an engagement and a line
// in the log explaining why the demonstration is missing.
try
{
    var written = await new DemoSeeder(connectionString).EnsureAsync(CancellationToken.None);

    if (app.Logger.IsEnabled(LogLevel.Information))
    {
        app.Logger.LogInformation("{Message}", DemoSeeder.Describe(written));
    }
}
catch (SqlException exception)
{
    app.Logger.LogError(exception,
        "The demonstration engagement could not be written. Everything else still works; the demonstration will be missing.");
}

// The container app terminates TLS at its ingress and forwards plain HTTP to this process,
// so without this every URL the framework builds for itself carries the scheme it was
// reached on rather than the one the browser used. The sign-in redirect is the first casualty:
// Entra is handed http://.../signin-oidc, refuses it against a registration that says https,
// and the error names the redirect URI rather than the proxy that rewrote it.
//
// The ingress is not in a known network or a known proxy list, and clearing both is what tells
// the middleware to trust the hop in front of it. That is safe here because nothing reaches
// this container except through that ingress.
var forwarded = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
};

forwarded.KnownNetworks.Clear();
forwarded.KnownProxies.Clear();

app.UseForwardedHeaders(forwarded);

// Static files before routing, and routing called explicitly so it is not inserted at the top
// of the pipeline on our behalf.
//
// This order is load bearing. MapFallbackToFile registers an endpoint that matches "/", and
// once routing has selected an endpoint the static file middleware declines to serve
// anything. The microsite was therefore in the image, reachable at /index.html and /de/
// index.html, and invisible at / and /de/ where anybody would actually look: the default
// files rewrite happened after the fallback had already won. The fallback's route pattern
// excludes paths that look like files, which is why the explicit ones worked and hid it.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// ------------------------------------------------------------------- helpers --
static string UserId(ClaimsPrincipal user) =>
    AccessStore.Normalise(user.FindFirstValue("preferred_username") ?? user.FindFirstValue(ClaimTypes.Upn) ?? string.Empty);

static string DisplayName(ClaimsPrincipal user) =>
    user.FindFirstValue("name") ?? user.FindFirstValue(ClaimTypes.Name) ?? UserId(user);

// The callback, built from the request rather than configured.
//
// It has to match what the authorize call sent and what is registered in Entra, exactly. A
// configured value is a value that is wrong on every deployment but the one it was written
// for, and the failure is an Entra error naming a redirect URI rather than a mismatch.
static string CallbackUri(HttpContext context) =>
    $"{context.Request.Scheme}://{context.Request.Host}/api/connections/callback";

// A connection's settings, as strings a form can put in a text box.
//
// Not a straight deserialise into Dictionary<string, string>. The values are not all
// strings: the demonstration seeder writes isDemonstration as a boolean, and a strict
// deserialise threw on it and took the whole connections list with it, so the screen
// showed 500 where a list of connections should be. Whatever is in there is rendered as
// text, because the only thing reading this is a form.
static Dictionary<string, string> ConnectionSettings(string? json)
{
    if (string.IsNullOrWhiteSpace(json)) return [];

    try
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Object) return [];

        return document.RootElement.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString() ?? string.Empty
                : property.Value.ToString(),
            StringComparer.Ordinal);
    }
    catch (JsonException)
    {
        // A row somebody hand edited. An empty form beats a broken screen.
        return [];
    }
}

// Reads what somebody may do, once per request rather than per endpoint.
async Task<UserAccess> AccessFor(HttpContext context)
{
    var store = context.RequestServices.GetRequiredService<AccessStore>();
    await store.EnsureInitialGlobalAdminAsync(initialAdmin, context.RequestAborted);
    return await store.GetAccessAsync(UserId(context.User), context.RequestAborted);
}

// Every engagement endpoint goes through this. A screen that forgets to check is a screen that
// shows one client's estate to another, and putting the check in one place is the only way to
// be sure none of them forgot.
async Task<IResult?> Denied(HttpContext context, Guid engagementId, string minimumRole)
{
    var access = await AccessFor(context);

    if (!access.Holds(engagementId, minimumRole))
    {
        return Results.Forbid();
    }

    return null;
}

// The pipeline's stages, from the contract, read once.
//
// From the contract rather than from a list here, so a stage added to analysis-stages.json
// appears on the screen that watches a run without anybody editing this file. That is the
// same reason the stage list exists in the contract at all, and a second copy here would be
// a second copy to fall out of step with the worker.
List<PipelineStage>? pipelineStages = null;

List<PipelineStage> PipelineStages()
{
    if (pipelineStages is not null) return pipelineStages;

    var path = ContractFiles.Path("analysis-stages.json");

    if (!File.Exists(path))
    {
        pipelineStages = [];
        return pipelineStages;
    }

    using var document = JsonDocument.Parse(File.ReadAllText(path));

    // Materialised inside the using. A lazy sequence handed to the serialiser is read after
    // this method returns and after the document is disposed, which is a mistake this file
    // has already made once and which presents as an empty screen rather than as an error.
    pipelineStages =
    [
        .. document.RootElement.GetProperty("stages").EnumerateArray()
            .Where(stage => PipelineOrder.Stages.Contains(stage.GetProperty("id").GetString() ?? string.Empty))
            .OrderBy(stage => stage.GetProperty("order").GetInt32())
            .Select(stage => new PipelineStage(
                stage.GetProperty("id").GetString() ?? string.Empty,
                stage.GetProperty("name").GetString() ?? string.Empty,
                stage.TryGetProperty("description", out var description) ? description.GetString() ?? string.Empty : string.Empty,
                stage.TryGetProperty("retryable", out var retryable) && retryable.GetBoolean()))
    ];

    return pipelineStages;
}

// --------------------------------------------------------------------- shell --
app.MapGet("/api/version", () => Results.Ok(new
{
    version = typeof(Program).Assembly.GetName().Version?.ToString(),
    rules = RuleCatalogue.All.Count,
    componentTypes = ComponentCatalogue.All.Count
}));

app.MapGet("/api/me", async (HttpContext context) =>
{
    if (context.User.Identity?.IsAuthenticated != true) return Results.Unauthorized();

    var access = await AccessFor(context);

    return Results.Ok(new
    {
        userId = UserId(context.User),
        displayName = DisplayName(context.User),
        access.IsGlobalAdmin,
        engagements = access.Roles.Select(entry => new { engagementId = entry.Key, role = entry.Value })
    });
}).RequireAuthorization();

// Into the product, not back to the front door.
//
// This redirected to "/" until somebody signed in for the first time and was returned to the
// public microsite, having just proved who they were, with no sign that anything had
// happened. The site root is the page for people who are not signed in; a person who has
// just signed in wants the thing they signed in for.
//
// A return address is honoured when the sign-in was provoked by a link into the product, so
// somebody who followed a link to a report lands on the report rather than on the overview.
// Only relative paths: an absolute one here is an open redirect, and a sign-in page that
// forwards to wherever the query string says is a phishing primitive with our domain on it.
app.MapGet("/account/signin", (HttpContext context) =>
{
    var requested = context.Request.Query["returnUrl"].ToString();

    var target = requested.StartsWith('/') && !requested.StartsWith("//", StringComparison.Ordinal)
        ? requested
        : "/app/";

    // Nothing to challenge against on a local run: the request already arrived
    // authenticated, and challenging a scheme that is not registered answers 401 to
    // somebody who is signed in, which is a confusing way to say "you already are".
    return entraConfigured
        ? Results.Challenge(new() { RedirectUri = target })
        : Results.Redirect(target);
});

// Out to the microsite, which is the only page a signed out person can read.
app.MapGet("/account/signout", () => entraConfigured
    ? Results.SignOut(new() { RedirectUri = "/" },
        [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme])

    // Signing out of a local run would mean signing out of nothing, and naming the cookie
    // scheme here would throw because it was never registered.
    : Results.Redirect("/"));

// --------------------------------------------------------------- engagements --
app.MapGet("/api/engagements", async (HttpContext context, WorkspaceStore store) =>
{
    var access = await AccessFor(context);
    var engagements = await store.ListEngagementsAsync(access, context.RequestAborted);

    // The role goes out with the row. It is not a column on the engagement, because it is a
    // property of the reader rather than of the engagement, but every screen needs it to
    // decide what to show and the alternative is the shell asking a second time per row.
    //
    // A global administrator holds Admin on everything, including engagements they have no
    // row for, which is the whole point of being one.
    return Results.Ok(engagements.Select(engagement => new
    {
        engagement.EngagementId,
        engagement.Name,
        engagement.ClientName,
        engagement.Status,
        engagement.IsRegulated,
        engagement.ReportLanguage,
        engagement.BacklogLanguage,
        engagement.CreatedUtc,
        accessRole = access.Roles.TryGetValue(engagement.EngagementId, out var role)
            ? role
            : access.IsGlobalAdmin ? EngagementRoles.Admin : EngagementRoles.Viewer,

        // The demonstration is read only for everybody and says so, rather than letting
        // somebody discover it by pressing a button that fails.
        isDemonstration = engagement.EngagementId == AccessStore.DemoEngagementId
    }));
}).RequireAuthorization();

app.MapPost("/api/engagements", async (HttpContext context, WorkspaceStore store, CreateEngagement request) =>
{
    var access = await AccessFor(context);

    // Creating one is a global admin action. Otherwise anybody in the tenant who signs in can
    // fill the product with engagements nobody asked for and nobody can find.
    if (!access.IsGlobalAdmin) return Results.Forbid();

    var engagement = new Engagement(
        Guid.NewGuid(),
        request.Name,
        request.ClientName,
        "active",
        request.IsRegulated,
        request.ReportLanguage ?? "en",
        request.BacklogLanguage ?? request.ReportLanguage ?? "en",
        DateTime.UtcNow);

    await store.CreateEngagementAsync(engagement, UserId(context.User), context.RequestAborted);
    return Results.Created($"/api/engagements/{engagement.EngagementId}", engagement);
}).RequireAuthorization();

// --------------------------------------------------------------- connections --
app.MapGet("/api/engagements/{engagementId:guid}/connections",
    async (HttpContext context, WorkspaceStore store, Guid engagementId) =>
{
    if (await Denied(context, engagementId, EngagementRoles.Viewer) is { } denied) return denied;

    var connections = await store.ListConnectionsAsync(engagementId, context.RequestAborted);

    // The secret reference goes nowhere near the browser, and neither does anything that could
    // be one. What the screen needs is whether it works and who it authenticated as.
    return Results.Ok(connections.Select(connection => new
    {
        connection.ConnectionId,
        connection.Mode,
        connection.Name,
        connection.EnvironmentRole,
        connection.LastTestedUtc,
        connection.LastTestSucceeded,
        connection.LastTestIdentity,
        connection.SecretExpiresUtc,

        // Derived from the mode rather than stored. Azure DevOps is the one place this
        // product writes, so it is the one connection that is a target, and a column nobody
        // sets is a column that eventually says a Dataverse connection writes somewhere.
        direction = PublishTargets().Contains(connection.Mode, StringComparer.Ordinal) ? "target" : "source",
        connection.LastTestMessage,

        // The settings, so the screen can show what a connection points at and offer to
        // change it. Safe to send and deliberately so: every mode's settings are a tenant,
        // a client id, an environment URL or a file name, and the credential itself lives
        // in Key Vault behind a reference that is not in this response.
        settings = ConnectionSettings(connection.SettingsJson),

        expiringSoon = connection.SecretExpiresUtc is { } expiry && expiry < DateTime.UtcNow.AddDays(30),
        reach = connection.ReachJson is null ? null : (JsonElement?)JsonDocument.Parse(connection.ReachJson).RootElement
    }));
}).RequireAuthorization();

// ----------------------------------------------------------------------- runs --
app.MapPost("/api/engagements/{engagementId:guid}/runs",
    async (HttpContext context, WorkspaceStore store, Guid engagementId, StartRun request) =>
{
    if (await Denied(context, engagementId, EngagementRoles.Contributor) is { } denied) return denied;

    // A publish is the only mode that writes anywhere, and it works from an assessment that
    // already exists rather than making a new one. The constraint is in the database as
    // well; this one is here so the message names the reason rather than a constraint.
    if (request.Mode == "publish" && request.BasedOnRunId is null)
    {
        return Results.BadRequest(new
        {
            error = "A publish works from an existing assessment rather than re-analysing. " +
                    "Re-analysing first would mean the thing published is not the thing somebody read."
        });
    }

    // Refused here rather than queued to fail at the first stage.
    //
    // A run with no source reaches connect, fails, and sits in the list saying "stopped at
    // check connections", which reads as a broken connection rather than as one that was
    // never chosen. The engagement's connections are right here and the answer is knowable
    // before anything is written.
    if (request.Mode != "publish" && request.SourceConnectionId is null)
    {
        var sources = (await store.ListConnectionsAsync(engagementId, context.RequestAborted))
            .Where(connection => connection.Mode != "azureDevOps")
            .ToList();

        return Results.BadRequest(new
        {
            error = sources.Count == 0
                ? "This engagement has nothing to read. Add a connection to an environment, or upload a "
                  + "solution export, before starting a run."
                : "No source was chosen. This engagement has "
                  + $"{string.Join(" and ", sources.Select(source => $"'{source.Name}'"))}, "
                  + "and a run has to say which one it read."
        });
    }

    var run = new AnalysisRun(
        Guid.NewGuid(),
        engagementId,
        request.Mode,
        "pending",
        request.SourceConnectionId,
        request.Mode == "publish" ? request.TargetConnectionId : null,
        request.BasedOnRunId,
        UserId(context.User),
        DateTime.UtcNow,
        null, null, null);

    await store.QueueRunAsync(run, context.RequestAborted);
    return Results.Accepted($"/api/runs/{run.RunId}", new { run.RunId });
}).RequireAuthorization();

app.MapGet("/api/runs/{runId:guid}", async (HttpContext context, WorkspaceStore store, Guid runId) =>
{
    var run = await store.GetRunAsync(runId, context.RequestAborted);
    if (run is null) return Results.NotFound();
    if (await Denied(context, run.EngagementId, EngagementRoles.Viewer) is { } denied) return denied;

    // Every stage in pipeline order with where it got to, rather than the identifiers of the
    // ones that finished. Watching a run is what a consultant does while a client waits, and
    // a list of what has completed cannot say which stage is running now, how long it has
    // been running, or which one stopped.
    var recorded = (await store.ListStagesAsync(runId, context.RequestAborted))
        .ToDictionary(stage => stage.StageId, StringComparer.Ordinal);

    var stages = PipelineStages()
        .Select((stage, order) =>
        {
            recorded.TryGetValue(stage.Id, out var state);

            return new
            {
                stageId = stage.Id,
                name = stage.Name,
                stage.Description,
                order,

                // Pending rather than absent. A stage with no row has not been reached, and
                // leaving it out of the list would make a run that stopped at stage three
                // look like a pipeline with three stages in it.
                status = state?.Status ?? "pending",
                attempt = state?.Attempt ?? 0,
                startedUtc = state?.StartedUtc,
                completedUtc = state?.CompletedUtc,
                error = state?.Error,

                // What it is doing right now, as a key and its arguments. Only ever set on a
                // running stage: the worker clears it when the stage stops, so a finished row
                // cannot still claim to be exporting something.
                progress = state?.Progress,
                stage.Retryable
            };
        })
        .ToList();

    var completed = stages.Count(stage => stage.status is "succeeded" or "partial" or "skipped");

    var current = stages.FirstOrDefault(stage => stage.status is "running" or "awaitingSelection")
        ?? stages.FirstOrDefault(stage => stage.status == "failed");

    return Results.Ok(new
    {
        run,
        stages,
        stagesComplete = completed,
        stagesTotal = stages.Count,
        currentStageName = current?.name
    });
}).RequireAuthorization();

// ------------------------------------------------------------ what to read --

// The picker. A run stops after listing the environment and waits here, because most of what
// a Dataverse environment holds is Microsoft's own solutions: reading them is the longest
// part of a run and produces a report about Dynamics rather than about the client's work.
app.MapGet("/api/runs/{runId:guid}/solutions", async (HttpContext context, WorkspaceStore store, Guid runId) =>
{
    var run = await store.GetRunAsync(runId, context.RequestAborted);
    if (run is null) return Results.NotFound();
    if (await Denied(context, run.EngagementId, EngagementRoles.Viewer) is { } denied) return denied;

    var solutions = await store.ListRunSolutionsAsync(runId, context.RequestAborted);
    var selection = await store.GetSelectionAsync(runId, context.RequestAborted);
    var defaults = RunChecks.ForMode(run.Mode);

    // Whether the checker can actually be submitted to, asked here rather than found out
    // later.
    //
    // The checker refuses without a geography, on purpose: where a client's solution is
    // uploaded for analysis is a data residency decision and must never be defaulted
    // quietly. What was wrong was when they found out. On the run this was written for, the
    // extraction took eight minutes, the checker refused in one second, and two rules came
    // back as not assessed over a field nobody had been asked for.
    var source = run.SourceConnectionId is null
        ? null
        : await store.GetConnectionAsync(run.SourceConnectionId.Value, context.RequestAborted);

    var checkerGeography = source is null ? null : ConnectionFactory.Read(source).CheckerGeography;

    return Results.Ok(new
    {
        runId,
        run.Mode,

        // Whether the run is actually waiting, as opposed to the list being available to
        // read afterwards. The screen shows a picker for one and a record for the other.
        awaiting = run.Status == "awaitingSelection",

        solutions = solutions.Select(solution => new
        {
            solution.UniqueName,
            solution.FriendlyName,
            solution.Version,
            solution.IsManaged,
            solution.PublisherPrefix,
            solution.PublisherName,
            solution.ComponentCount,
            solution.IsFirstParty,

            // The tick the box starts with. Null means nobody has been asked, and the
            // default is everything that is not Microsoft's.
            selected = solution.IsSelected ?? !solution.IsFirstParty
        }),

        checks = new
        {
            solutionChecker = selection?.Checks.SolutionChecker ?? defaults.SolutionChecker,
            modelEstimates = selection?.Checks.ModelEstimates ?? defaults.ModelEstimates,
            environmentHealth = selection?.Checks.EnvironmentHealth ?? defaults.EnvironmentHealth,
            exportSolutions = selection?.Checks.ExportSolutions ?? defaults.ExportSolutions
        },

        // Null means the checker will refuse. The screen says so beside the box rather than
        // letting somebody tick it and find out after the extraction.
        checkerGeography
    });
}).RequireAuthorization();

app.MapPost("/api/runs/{runId:guid}/selection",
    async (HttpContext context, WorkspaceStore store, Guid runId, ChooseSolutions request) =>
{
    var run = await store.GetRunAsync(runId, context.RequestAborted);
    if (run is null) return Results.NotFound();
    if (await Denied(context, run.EngagementId, EngagementRoles.Contributor) is { } denied) return denied;

    if (run.Status != "awaitingSelection")
    {
        // Refused rather than recorded. A selection against a run that has already read the
        // environment would sit in the database looking like the scope of a report it had
        // nothing to do with.
        return Results.BadRequest(new
        {
            error = $"This run is {run.Status}, not waiting for a selection. Start a new run to analyse a "
                  + "different set of solutions."
        });
    }

    var chosen = request.Solutions ?? [];

    await store.RecordSelectionAsync(
        runId,
        chosen,
        new RunCheckChoices(
            request.SolutionChecker, request.ModelEstimates, request.EnvironmentHealth, request.ExportSolutions),
        UserId(context.User),
        context.RequestAborted);

    return Results.Accepted($"/api/runs/{runId}", new
    {
        resumed = true,
        chosen = chosen.Count,
        note = chosen.Count == 0
            ? "Nothing was chosen, so the run will report an unread estate rather than a clean one."
            : null
    });
}).RequireAuthorization();

// ------------------------------------------------------------ removing one --

// Housekeeping, and it matters for the estate rather than for the disk. An engagement
// accumulates a run from every attempt, and while every findings screen reads exactly one
// run, the list is what somebody scrolls when they are looking for the one they mean.
app.MapDelete("/api/runs/{runId:guid}",
    async (HttpContext context, WorkspaceStore store, SolutionUploads uploads, Guid runId) =>
{
    var run = await store.GetRunAsync(runId, context.RequestAborted);
    if (run is null) return Results.NotFound();

    // Admin, not Contributor. Starting a run is routine; removing the analysis a report was
    // written from is not, and nothing here can undo it.
    if (await Denied(context, run.EngagementId, EngagementRoles.Admin) is { } denied) return denied;

    if (run.Status is "running" or "pending")
    {
        return Results.BadRequest(new
        {
            error = "This run is still going. Wait for it to stop, or cancel it, before removing it: a worker "
                  + "part way through would carry on writing findings against a run that no longer exists."
        });
    }

    try
    {
        var published = await store.DeleteRunAsync(runId, context.RequestAborted);

        // The solutions this run exported from the client's environment. After the database
        // rather than before it: a failure here leaves files that the account's lifecycle
        // rule removes anyway, while a failure there would have left findings pointing at
        // solutions that were no longer anywhere.
        var files = 0;

        try
        {
            files = await uploads.RemoveAsync($"exports/{runId}/", context.RequestAborted);
        }
        catch (RequestFailedException storage)
        {
            // Said, not thrown. The run is gone either way, and that is what was asked for.
            app.Logger.LogWarning(
                "Removed run {RunId} but could not remove its exported solutions: {Reason}",
                runId, storage.Message);
        }

        return Results.Ok(new
        {
            deleted = true,
            exportsRemoved = files,

            // Said plainly. Removing the run removes this product's record of a publish and
            // nothing at all in the client's board, and somebody who assumed otherwise would
            // go looking for work items that are still sitting there.
            note = published > 0
                ? $"{published} work item(s) were published from this run. They are still in the target "
                  + "project; only this product's record of them is gone."
                : null
        });
    }
    catch (InvalidOperationException refusal)
    {
        return Results.BadRequest(new { error = refusal.Message });
    }
    catch (SqlException failure)
    {
        // A database failure here reached the browser as a bare 500, which says nothing a
        // person can act on. Two of them are not the same, and telling them apart is the
        // difference between useful advice and a loop.
        //
        // A timeout on a run that had read a whole environment is worth retrying: the delete
        // works in batches and resumes where it stopped.
        //
        // A foreign key conflict is not. Something still points at what is being deleted,
        // and it will point at it just as firmly on the next attempt. Telling somebody to
        // press the button again is telling them to do the same thing until they give up,
        // which is exactly what happened to the first run anybody published from: nothing
        // deleted the published work items before the backlog rows they referenced.
        const int foreignKeyConflict = 547;

        return Results.BadRequest(new
        {
            error = "The run was not fully removed: " + failure.Message
                  + (failure.Number == foreignKeyConflict
                      ? " Something in the database still refers to part of this run, and pressing the button "
                        + "again will fail in the same place. This is a defect in the product rather than "
                        + "anything you did: please report it with this message."
                      : " Removing a large run can take more than one attempt. Press it again and it will "
                        + "carry on from where it stopped.")
        });
    }
}).RequireAuthorization();

// --------------------------------------------------------------- run again --

// From a stage rather than from the beginning, because a run that failed at the checker after
// forty minutes of extraction should not pay for the extraction twice.
app.MapPost("/api/runs/{runId:guid}/stages/{stageId}/retry",
    async (HttpContext context, WorkspaceStore store, Guid runId, string stageId) =>
{
    var run = await store.GetRunAsync(runId, context.RequestAborted);
    if (run is null) return Results.NotFound();
    if (await Denied(context, run.EngagementId, EngagementRoles.Contributor) is { } denied) return denied;

    if (run.Status is "running" or "pending")
    {
        return Results.BadRequest(new
        {
            error = "This run is still going. Wait for it to stop before running a stage again, or two "
                  + "workers will be writing the same findings."
        });
    }

    if (!PipelineStages().Any(stage => string.Equals(stage.Id, stageId, StringComparison.Ordinal)))
    {
        return Results.BadRequest(new { error = $"'{stageId}' is not a stage of this pipeline." });
    }

    await store.QueueCommandAsync(runId, "retryStage", stageId, UserId(context.User), context.RequestAborted);

    return Results.Accepted($"/api/runs/{runId}", new
    {
        queued = true,

        // Said plainly, because it is the surprising part. Everything after the stage is
        // discarded as well, and a person who expected one stage to re-run would otherwise
        // watch the run redo an hour of work with no explanation.
        note = "This stage and everything after it will run again."
    });
}).RequireAuthorization();

// ------------------------------------------------------ the reader's language --

// Every screen that shows rule text needs it, and until now none of them asked.
//
// The reports have localised since the first release: the PDF and the workbook take the
// engagement's language and look every sentence up. The JSON endpoints behind the screens
// did not, so a consultant reading in German got a German shell around English rule names,
// English explanations and, where the sentence was composed by the engine rather than
// written by hand, a raw lookup key.
//
// The reader's own language rather than the engagement's. A report is a document for the
// client and is written in the language the engagement chose; a screen is being read right
// now by whoever is looking at it.
async Task<string> LanguageFor(HttpContext context)
{
    var access = context.RequestServices.GetRequiredService<AccessStore>();
    var preferences = await access.GetPreferencesAsync(UserId(context.User), context.RequestAborted);

    return LocaleCatalogue.Resolve(preferences.Language).Code;
}

// -------------------------------------------------------------------- findings --

// The same sentence every time, so it is built once rather than on every request.
string[] unreadEstate =
[
    "No analysis has completed on this engagement yet. This is not a clean estate; it is an unread one."
];

app.MapGet("/api/engagements/{engagementId:guid}/findings",
    async (HttpContext context, AnalysisStore store, Guid engagementId, Guid? runId) =>
{
    if (await Denied(context, engagementId, EngagementRoles.Viewer) is { } denied) return denied;

    // Two namespaces, because the text comes from two places: a rule's name and explanation
    // are written per rule, and the sentence saying why a check could not run is composed
    // from a key and the evidence it needed.
    var language = await LanguageFor(context);
    var ruleText = new Localiser("finding", language);
    var reportText = new Localiser("report", language);

    // Latest run that got as far as scoring, not latest that was started. Defaulting to a run
    // that died during extraction would show an empty estate and look like a finished one.
    var run = runId ?? await store.GetLatestScoredRunAsync(engagementId, context.RequestAborted);

    if (run is null)
    {
        // Explicitly "no run", never an empty findings list. The screen can say nobody has
        // analysed this engagement yet, which is a different sentence from "no findings".
        return Results.Ok(new
        {
            runId = (Guid?)null,
            findings = Array.Empty<object>(),
            notAssessed = Array.Empty<object>(),
            ruleCount = RuleCatalogue.All.Count,
            caveats = unreadEstate
        });
    }

    var findings = await store.GetFindingsAsync(run.Value, context.RequestAborted);
    var notAssessed = await store.GetNotAssessedAsync(run.Value, context.RequestAborted);
    var score = await store.GetScoreAsync(run.Value, context.RequestAborted);

    var caveats = score is null
        ? ["This run has findings and no score, which means it did not finish. Read it as partial."]
        : JsonDocument.Parse(score.Value.BreakdownJson).RootElement.TryGetProperty("Caveats", out var stored)
            ? stored.EnumerateArray().Select(entry => entry.GetString()).Where(entry => entry is not null).ToArray()
            : Array.Empty<string>();

    return Results.Ok(new
    {
        runId = run,
        ruleCount = RuleCatalogue.All.Count,
        caveats,
        // The rule's name and a finished sentence, not an identifier and a lookup key.
        //
        // This section is the one a careful reader reads first, because it is the shape of
        // the hole around every number above it. It was printing
        // "lifecycle.classicWorkflowDormant notAssessed.unreachable", which is the product
        // talking to itself in front of a client.
        notAssessed = notAssessed.Select(entry => new
        {
            entry.RuleId,
            ruleName = ruleText[$"finding.{entry.RuleId}.name", RuleCatalogue.Find(entry.RuleId)?.Name ?? entry.RuleId],
            // Rebuilt into the domain record the describer takes. The store hands back a
            // tuple, which is the same three fields under a different name.
            reason = NotAssessedReasons.Describe(
                reportText, new NotAssessed(entry.RuleId, entry.Reason, entry.MissingEvidence)),
            entry.MissingEvidence,
        }),
        findings = findings.Select(finding =>
        {
            // The rule's own text is joined on here rather than stored per finding. It is the
            // same sentence for every finding of a rule, by design, and storing four hundred
            // copies would mean a corrected sentence only fixing the ones written afterwards.
            var rule = RuleCatalogue.Find(finding.RuleId);

            return new
            {
                finding.FindingId,
                finding.StableKey,
                finding.RuleId,
                ruleName = ruleText[$"finding.{finding.RuleId}.name", rule?.Name ?? finding.RuleId],
                finding.Category,
                finding.Severity,
                finding.ComponentName,
                componentType = finding.ComponentTypeId is null ? null : ComponentCatalogue.Find(finding.ComponentTypeId)?.Name,
                finding.SolutionName,
                finding.IsManaged,
                evidence = JsonDocument.Parse(finding.EvidenceJson).RootElement,
                finding.LowHours,
                finding.HighHours,
                finding.StoryPoints,
                finding.Confidence,
                estimateLayer = finding.Layer,
                finding.Rationale,
                finding.FlaggedReason,
                why = rule is null ? null : ruleText[$"finding.{finding.RuleId}.why", rule.Why],
                recommendation = rule is null ? null : ruleText[$"finding.{finding.RuleId}.recommendation", rule.Recommendation],
                falsePositive = rule?.FalsePositive is null
                    ? null
                    : ruleText[$"finding.{finding.RuleId}.falsePositive", rule.FalsePositive],
                roadmap = rule is null ? null : new { rule.RoadmapRow, rule.RoadmapColumn, rule.RoadmapBand }
            };
        })
    });
}).RequireAuthorization();

app.MapPost("/api/engagements/{engagementId:guid}/overrides",
    async (HttpContext context, AnalysisStore store, Guid engagementId, SetOverride request) =>
{
    if (await Denied(context, engagementId, EngagementRoles.Contributor) is { } denied) return denied;

    if (string.IsNullOrWhiteSpace(request.Rationale))
    {
        return Results.BadRequest(new
        {
            error = "An override needs a reason. It is the number that ends up in the statement of work, and " +
                    "one with nothing behind it is indistinguishable from a typo three months later."
        });
    }

    if (request.LowHours > request.HighHours)
    {
        return Results.BadRequest(new { error = "That is not a range." });
    }

    if (request.StoryPoints is { } points && !Estimate.PointScale.Contains(points))
    {
        return Results.BadRequest(new { error = $"{points} is not on the point scale." });
    }

    await store.SetOverrideAsync(
        engagementId, request.Scope, request.RuleId, request.ComponentTypeId, request.FindingKey,
        request.LowHours, request.HighHours, request.StoryPoints, request.Rationale.Trim(),
        UserId(context.User), context.RequestAborted);

    return Results.Ok(new
    {
        saved = true,
        note = "This beats anything a model produces, survives a re-extraction, and applies to the same finding " +
               "on the next run."
    });
}).RequireAuthorization();

app.MapGet("/api/engagements/{engagementId:guid}/backlog",
    async (HttpContext context, AnalysisStore store, Guid engagementId, Guid? runId) =>
{
    if (await Denied(context, engagementId, EngagementRoles.Viewer) is { } denied) return denied;

    var run = runId ?? await store.GetLatestScoredRunAsync(engagementId, context.RequestAborted);
    if (run is null) return Results.Ok(new { runId = (Guid?)null, items = Array.Empty<object>() });

    var items = await store.GetBacklogAsync(run.Value, context.RequestAborted);

    return Results.Ok(new
    {
        runId = run,

        items = items.Select(item => new
        {
            item.BacklogItemId,
            item.ParentItemId,
            item.WorkItemType,
            item.Title,

            // The body, which the screen never had. It is what makes an item groomable:
            // what was found, where it is, which component in which solution, why it
            // matters and what to do. All of it was written, stored and published to
            // Azure DevOps, and the one place it was not shown was the product that
            // produced it, so the app read as a thinner version of its own output.
            item.DescriptionHtml,
            item.AcceptanceCriteria,
            item.TestRequirement,
            item.Priority,
            item.StoryPoints,
            item.LowHours,
            item.HighHours,
            item.DeterministicKey
        })
    });
}).RequireAuthorization();

// The steps, and where each one is done. Built once rather than on every request to a screen
// somebody opens at the start of every session.
string[] stepIds = ["connect", "discover", "review", "publish"];
string[] stepWorkspaces = ["Connections", "Runs", "Findings", "Backlog"];

// -------------------------------------------------------------------- reports --
// Composed on demand rather than produced by a run and kept. A client's estate in a document
// is the most sensitive thing this product makes, and a blob container quietly accumulating
// them is a retention question nobody asked for. The run is stored; the document is rendered
// from it when somebody asks, and is identical every time because the score it quotes is the
// score the run recorded rather than one recomputed.
app.MapGet("/api/engagements/{engagementId:guid}/reports",
    async (HttpContext context, ReportComposer composer, Guid engagementId) =>
{
    if (await Denied(context, engagementId, EngagementRoles.Viewer) is { } denied) return denied;

    var composed = await composer.ComposeAsync(engagementId, context.RequestAborted);

    if (composed is null)
    {
        return Results.Ok(new { available = false, reason = "noRun", reports = Array.Empty<object>() });
    }

    // The PDF says whether it can be produced rather than offering a download that fails.
    // Syncfusion does not refuse an absent licence key, it watermarks every page, and a
    // watermarked document reaching a client who was asked to sign it is worse than no
    // document. A reader cannot tell whose fault a failed download is.
    var pdfAvailable = SyncfusionLicence.IsRegisteredForPdf
        || !string.IsNullOrWhiteSpace(builder.Configuration["Syncfusion:LicenseKey"])
        || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SyncfusionLicence.EnvironmentVariable));

    return Results.Ok(new
    {
        available = true,
        producedFrom = composed.ProducedUtc,
        runId = composed.RunId,
        totalRecords = composed.Workbook.Score.ComponentsTotal,
        reports = new object[]
        {
            new { id = "inventory", file = "workbook.xlsx", rows = composed.Workbook.Components.Count },
            new { id = "pdf", file = "report.pdf", rows = composed.Report.Findings.Count, available = pdfAvailable }
        }
    });
}).RequireAuthorization();

app.MapGet("/api/engagements/{engagementId:guid}/reports/{file}",
    async (HttpContext context, ReportComposer composer, Guid engagementId, string file) =>
{
    if (await Denied(context, engagementId, EngagementRoles.Viewer) is { } denied) return denied;

    if (file is not "workbook.xlsx" and not "report.pdf") return Results.NotFound();

    var composed = await composer.ComposeAsync(engagementId, context.RequestAborted);

    if (composed is null)
    {
        return Results.BadRequest(new
        {
            error = "Nothing has been analysed on this engagement, so there is nothing to put in a document."
        });
    }

    // Named for the client and the date the analysis ran, not the date it was downloaded.
    // Somebody with six of these in a folder has to be able to tell which is which, and the
    // one that matters is when the estate was read.
    var stamp = composed.ProducedUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    var safe = Slug(composed.Report.ClientName ?? composed.Report.EngagementName);

    if (file == "workbook.xlsx")
    {
        var bytes = FindingsWorkbook.Build(composed.Workbook);

        return Results.File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"{safe}-findings-{stamp}.xlsx");
    }

    try
    {
        var bytes = AssessmentReportPdf.Build(composed.Report);

        return Results.File(bytes, "application/pdf", $"{safe}-assessment-{stamp}.pdf");
    }
    catch (InvalidOperationException failure)
    {
        // Almost always the licence key. Said plainly, because the alternative is a broken
        // download and a reader who cannot tell whether the product or their browser failed.
        return Results.Problem(failure.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).RequireAuthorization();

// ------------------------------------------------------ extraction modes --
// Served from the contract rather than described again here. extraction-sources.json says it
// generates the connection wizard and the capability matrix, and the moment this file carried
// its own copy the screen and the report would start disagreeing about what a mode reaches.
// The palette, as a stylesheet.
//
// Everything the web app and the public site draw with derives from one ramp, which both
// stylesheets declare on :root as their built-in default. This redefines those same custom
// properties from the brand file, and it is linked after the stylesheet, so a brand is
// twenty-odd values and nothing else in the CSS has to know a second brand exists.
//
// Anonymous on purpose. It is linked from the sign-in page and from every page of the
// public site, both of which are read by people who have not signed in, and a stylesheet
// behind authentication is a sign-in page with no colours on it.
app.MapGet("/brand.css", () =>
{
    var lines = PowerPete.Analyzer.Export.Brand.Ramp
        .Select(entry => $"  --ramp-{entry.Key}: {entry.Value};");

    var css = $"/* {PowerPete.Analyzer.Export.Brand.Name}. Generated from brands/{PowerPete.Analyzer.Export.Brand.Id}.json. */\n"
        + ":root {\n" + string.Join("\n", lines) + "\n}\n";

    // Not cached. The whole point of choosing the brand at run time is that a restart
    // changes it, and a stylesheet a browser held for a year would mean it did not.
    return Results.Text(css, "text/css", System.Text.Encoding.UTF8);
}).AllowAnonymous();

// The brand's marks, at paths that do not change when the brand does.
//
// The alternative was a placeholder the microsite build fills in, which would have baked
// the logo into the image and left a restart changing the colours but not the wordmark.
// The alternative to that was swapping the src from script on load, which is a flash of
// the wrong logo on every page view. A fixed path the server resolves is neither.
//
// Anonymous, for the same reason the stylesheet is: the sign-in page carries the wordmark
// and is read by people who have not signed in.
app.MapGet("/brand/{mark}", (string mark, IWebHostEnvironment host) =>
{
    var file = mark switch
    {
        "wordmark" => PowerPete.Analyzer.Export.Brand.AssetName("siteWordmark"),
        "mark" => PowerPete.Analyzer.Export.Brand.AssetName("siteMark"),
        "favicon" => PowerPete.Analyzer.Export.Brand.AssetName("favicon"),
        _ => null
    };

    if (file is null) return Results.NotFound();

    // Out of the same folder the public site's own images come from, which is where the
    // microsite build puts every brand's assets. Path.GetFileName because the value comes
    // from a brand file and a brand file should not be able to read /etc/passwd.
    var path = Path.Combine(host.WebRootPath ?? "wwwroot", "assets", Path.GetFileName(file));

    if (!File.Exists(path)) return Results.NotFound();

    var type = Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        _ => "application/octet-stream"
    };

    // No caching, so a restart that changes the brand changes the logo with it.
    return Results.File(path, type);
}).AllowAnonymous();

// What the product is called and who by, for the shell and the public site.
app.MapGet("/api/brand", () => Results.Ok(new
{
    id = PowerPete.Analyzer.Export.Brand.Id,
    name = PowerPete.Analyzer.Export.Brand.Name,
    product = PowerPete.Analyzer.Export.Brand.Product,
    site = PowerPete.Analyzer.Export.Brand.Site,
    tagline = PowerPete.Analyzer.Export.Brand.Tagline,
    copy = PowerPete.Analyzer.Export.Brand.Copy
})).AllowAnonymous();

app.MapGet("/api/extraction-modes", () =>
{
    var path = ContractFiles.Path("extraction-sources.json");

    if (!File.Exists(path))
    {
        return Results.Problem(
            "The extraction sources contract is not in this image. The connection wizard is generated from it.",
            statusCode: StatusCodes.Status500InternalServerError);
    }

    using var document = JsonDocument.Parse(File.ReadAllText(path));

    // Materialised here, inside the using, rather than handed to the serialiser as a lazy
    // sequence. Select does not read anything until somebody enumerates it, and the only
    // thing that ever enumerates this is the JSON serialiser, which runs after the method has
    // returned and therefore after the document has been disposed. Every request threw
    // ObjectDisposedException, the wizard received no modes and rendered an empty grid with
    // no error on it, so it looked like a layout fault rather than a failed request.
    var modes = document.RootElement.GetProperty("modes").EnumerateArray().Select(mode => new
    {
        id = mode.GetProperty("id").GetString(),
        name = mode.GetProperty("name").GetString(),
        status = mode.GetProperty("status").GetString(),
        summary = mode.GetProperty("summary").GetString(),
        settings = mode.GetProperty("auth").GetProperty("settings")
            .EnumerateArray().Select(setting => setting.GetString()).ToList(),

        // The ones somebody may leave empty. Absent from a mode that has none, which is
        // most of them, so the wizard reads an empty list and requires everything as it
        // always did.
        optional = mode.GetProperty("auth").TryGetProperty("optional", out var spare)
            ? spare.EnumerateArray().Select(setting => setting.GetString()).ToList()
            : [],
        // Whether the person has to type one, which is not the same as whether the mode
        // has a secret. Interactive sign-in ends with a refresh token in the vault and
        // nobody ever types it; asking for one on that screen is asking for the wrong
        // credential and getting a connection that cannot work.
        needsSecret = mode.GetProperty("auth").GetProperty("type").GetString() == "clientCredentials",
        authType = mode.GetProperty("auth").GetProperty("type").GetString(),
        reaches = mode.GetProperty("reaches").EnumerateObject()
            .ToDictionary(entry => entry.Name, entry => entry.Value.GetString())
    }).ToList();

    return Results.Ok(modes);
}).RequireAuthorization();

// Where a backlog can be published. Separate from the extraction modes because publishing
// is the one thing this product does that writes, and a screen that listed it beside the
// ways of reading an estate would be inviting somebody to pick it as a source.
app.MapGet("/api/publish-targets", () =>
{
    var path = ContractFiles.Path("extraction-sources.json");

    if (!File.Exists(path)) return Results.Ok(Array.Empty<object>());

    using var document = JsonDocument.Parse(File.ReadAllText(path));

    if (!document.RootElement.TryGetProperty("targets", out var targets)) return Results.Ok(Array.Empty<object>());

    // Materialised inside the using. The same lazy-Select-over-a-disposed-document that
    // emptied the connection wizard once already.
    var list = targets.EnumerateArray().Select(target => new
    {
        id = target.GetProperty("id").GetString(),
        name = target.GetProperty("name").GetString(),
        status = target.GetProperty("status").GetString(),
        summary = target.GetProperty("summary").GetString(),
        settings = target.GetProperty("auth").GetProperty("settings")
            .EnumerateArray().Select(setting => setting.GetString()).ToList(),

        // As above. GitHub's API address is the only one so far: it is empty for github.com
        // and is the only way a GitHub Enterprise Server installation is reachable at all.
        optional = target.GetProperty("auth").TryGetProperty("optional", out var spare)
            ? spare.EnumerateArray().Select(setting => setting.GetString()).ToList()
            : [],
        needsSecret = true,
        authType = target.GetProperty("auth").GetProperty("type").GetString(),
        reaches = new Dictionary<string, string>(StringComparer.Ordinal)
    }).ToList();

    return Results.Ok(list);
}).RequireAuthorization();

// The projects in the organisation a connection points at.
//
// Listed rather than typed. Azure DevOps answers 404 both for a project that does not exist
// and for one the token cannot see, and from here the two are the same reply, so a typed
// name fails in the one way nobody can diagnose. A list that came back through the same
// token that will do the publishing cannot contain either.
// ------------------------------------------------------- proving a connection --

// The connections screen has said "not tested" since the day it was written and nothing
// could ever change it. RecordConnectionTestAsync is called from inside the extract stage,
// so a source could only be proved by analysing an estate with it and a publish target
// could not be proved at all: the one thing a consultant wants to do before a client
// meeting is the one thing the product did not offer.
//
// A read in every case. Testing Azure DevOps by creating a work item would be a product
// that writes to a client's board to find out whether it can, so each target is proved by
// listing what it can see, which is the same call the project picker makes.
app.MapPost("/api/engagements/{engagementId:guid}/connections/{connectionId:guid}/test",
    async (HttpContext context, WorkspaceStore store, DevOpsProjects projects, Guid engagementId, Guid connectionId) =>
{
    if (await Denied(context, engagementId, EngagementRoles.Contributor) is { } denied) return denied;

    var connection = await store.GetConnectionAsync(connectionId, context.RequestAborted);

    if (connection is null || connection.EngagementId != engagementId) return Results.NotFound();

    var settings = ConnectionSettings(connection.SettingsJson);
    var secrets = context.RequestServices.GetRequiredService<ISecretStore>();

    var token = connection.SecretRef is { Length: > 0 } reference
        ? await secrets.GetAsync(SecretNames.For(reference, "secret"), context.RequestAborted)
        : null;

    var succeeded = false;
    string message;
    string? identity = null;

    try
    {
        switch (connection.Mode)
        {
            case "offlineZip":
                // Nothing to authenticate against. Either a file was uploaded or it was not,
                // and saying so is more use than refusing to answer.
                settings.TryGetValue("uploadedFile", out var file);
                succeeded = !string.IsNullOrWhiteSpace(file);
                message = succeeded
                    ? "A solution export is uploaded and will be read by the next run."
                    : "No solution export has been uploaded to this connection yet.";
                break;

            case "jira":
            {
                settings.TryGetValue("siteUrl", out var site);
                settings.TryGetValue("email", out var email);

                var boards = context.RequestServices.GetRequiredService<JiraProjects>();

                var found = await boards.ListAsync(
                    site ?? string.Empty, email ?? string.Empty, token ?? string.Empty, context.RequestAborted);

                succeeded = found.Error is null;
                identity = email;

                // The count, not just a tick. A token that authenticates and can see no
                // project is the failure that looks like success until a publish runs.
                message = found.Error
                    ?? $"Authenticated as {email}, and {found.Projects.Count} project(s) are visible.";
                break;
            }

            case "github":
            {
                settings.TryGetValue("owner", out var owner);
                settings.TryGetValue("apiBaseUrl", out var apiBase);

                var repositories = context.RequestServices.GetRequiredService<GitHubRepositories>();

                var found = await repositories.ListAsync(
                    owner ?? string.Empty, token ?? string.Empty, apiBase, context.RequestAborted);

                succeeded = found.Error is null;
                identity = owner;

                // What this proved, and what it did not. Listing repositories needs only
                // Metadata: Read, so a token that passes here can still be refused on the
                // first issue it tries to create, which is exactly what happened the first
                // time anybody published. "Can take a backlog" was a claim about writing
                // made by a call that only read.
                message = found.Error
                    ?? $"Authenticated against {owner}. {found.Repositories.Count} repository(s) are visible "
                        + "and have issues enabled"
                        + (found.Hidden > 0
                            ? $", and {found.Hidden} more are archived or have issues turned off"
                            : string.Empty)
                        + ". This proves the token can read. Only a publish proves it can write issues: for "
                        + "that a fine grained token needs Issues: Read and write on the repository.";
                break;
            }

            case "azureDevOps":
            {
                settings.TryGetValue("organisationUrl", out var organisation);

                var found = await projects.ListAsync(
                    organisation ?? string.Empty, token ?? string.Empty, context.RequestAborted);

                succeeded = found.Error is null;
                message = found.Error
                    ?? $"Authenticated, and {found.Projects.Count} project(s) are visible.";
                break;
            }

            default:
            {
                // servicePrincipal and delegated, through the same factory the worker uses.
                // A second implementation of this would eventually disagree with the first
                // about whether a client's credential works, and the screen would say one
                // thing while the run did another.
                var factory = context.RequestServices.GetRequiredService<ConnectionFactory>();
                using var client = await factory.ForDataverseAsync(connection, context.RequestAborted);
                var probe = await new DataverseReader(client).TestAsync(context.RequestAborted);

                succeeded = probe.Succeeded;
                identity = probe.Identity;
                message = probe.Message;
                break;
            }
        }
    }
    catch (Exception failure) when (failure is HttpRequestException or InvalidOperationException
        or Azure.RequestFailedException or Azure.Identity.AuthenticationFailedException)
    {
        // Recorded as a failure rather than thrown. A test that throws a 500 tells the
        // reader the product is broken when what is broken is the credential.
        succeeded = false;
        message = failure.Message;
    }

    await store.RecordConnectionTestAsync(
        connectionId, succeeded, identity, message, null, context.RequestAborted);

    return Results.Ok(new { succeeded, message, identity });
}).RequireAuthorization();

app.MapGet("/api/engagements/{engagementId:guid}/connections/{connectionId:guid}/projects",
    async (HttpContext context, WorkspaceStore store, DevOpsProjects projects, Guid engagementId, Guid connectionId) =>
{
    if (await Denied(context, engagementId, EngagementRoles.Contributor) is { } denied) return denied;

    var connection = await store.GetConnectionAsync(connectionId, context.RequestAborted);

    if (connection is null || connection.EngagementId != engagementId) return Results.NotFound();

    if (!PublishTargets().Contains(connection.Mode, StringComparer.Ordinal))
    {
        return Results.BadRequest(new { error = "That connection does not publish anywhere." });
    }

    var settings = ConnectionSettings(connection.SettingsJson);

    var secrets = context.RequestServices.GetRequiredService<ISecretStore>();
    var token = connection.SecretRef is { Length: > 0 } reference
        ? await secrets.GetAsync(SecretNames.For(reference, "secret"), context.RequestAborted)
        : null;

    // Both targets answer the same question and neither answers it the same way. What the
    // screen gets back is the same shape either way: a name a person recognises and a value
    // the publish call wants, which is a project name in Azure DevOps and a project key in
    // Jira.
    if (string.Equals(connection.Mode, "github", StringComparison.Ordinal))
    {
        settings.TryGetValue("owner", out var owner);
        settings.TryGetValue("apiBaseUrl", out var apiBase);

        var repositories = context.RequestServices.GetRequiredService<GitHubRepositories>();

        var found = await repositories.ListAsync(
            owner ?? string.Empty, token ?? string.Empty, apiBase, context.RequestAborted);

        if (found.Error is not null) return Results.BadRequest(new { error = found.Error });

        // owner/name to read and the bare name to send, because the owner is already on the
        // connection and sending it twice is how a repository ends up addressed as
        // contoso/contoso/thing.
        return Results.Ok(found.Repositories.Select(repository => new
        {
            repository.Id,
            name = repository.FullName,
            value = repository.Name,
            repository.Description
        }));
    }

    if (string.Equals(connection.Mode, "jira", StringComparison.Ordinal))
    {
        settings.TryGetValue("siteUrl", out var site);
        settings.TryGetValue("email", out var email);

        var boards = context.RequestServices.GetRequiredService<JiraProjects>();

        var found = await boards.ListAsync(
            site ?? string.Empty, email ?? string.Empty, token ?? string.Empty, context.RequestAborted);

        if (found.Error is not null) return Results.BadRequest(new { error = found.Error });

        return Results.Ok(found.Projects.Select(project => new
        {
            project.Id,
            name = $"{project.Name} ({project.Key})",
            value = project.Key,
            description = (string?)null
        }));
    }

    settings.TryGetValue("organisationUrl", out var organisation);

    var result = await projects.ListAsync(organisation ?? string.Empty, token ?? string.Empty, context.RequestAborted);

    if (result.Error is not null) return Results.BadRequest(new { error = result.Error });

    return Results.Ok(result.Projects.Select(project => new
    {
        project.Id,
        project.Name,
        value = project.Name,
        project.Description
    }));
}).RequireAuthorization();

// Publishing a chosen set of the backlog into a chosen project.
//
// Both halves are the point. The worker's publish run mode sends the whole backlog to the
// project named on the connection, which is right for a pipeline and wrong for a person: the usual case is a consultant walking a client through the backlog and agreeing
// that this epic and those four tasks go in now. So the items are chosen here and the
// project is chosen here, and neither is remembered on the connection.
app.MapPost("/api/engagements/{engagementId:guid}/publish",
    async (HttpContext context,
        WorkspaceStore workspace,
        AnalysisStore analysis,
        IHttpClientFactory factory,
        Guid engagementId,
        PublishRequest request) =>
{
    ArgumentNullException.ThrowIfNull(request);

    if (await Denied(context, engagementId, EngagementRoles.Contributor) is { } denied) return denied;

    var connection = await workspace.GetConnectionAsync(request.ConnectionId, context.RequestAborted);

    if (connection is null || connection.EngagementId != engagementId) return Results.NotFound();

    if (!PublishTargets().Contains(connection.Mode, StringComparer.Ordinal))
    {
        return Results.BadRequest(new { error = "That connection does not publish anywhere." });
    }

    if (string.IsNullOrWhiteSpace(request.Project))
    {
        return Results.BadRequest(new { error = "Choose a project." });
    }

    var run = await analysis.GetLatestScoredRunAsync(engagementId, context.RequestAborted);

    if (run is null) return Results.BadRequest(new { error = "Nothing has been analysed, so there is no backlog." });

    var all = await analysis.GetPublishableBacklogAsync(run.Value, context.RequestAborted);

    var chosen = request.Keys is { Count: > 0 }
        ? all.Where(item => request.Keys.Contains(item.Key, StringComparer.Ordinal)).ToList()
        : [.. all];

    if (chosen.Count == 0) return Results.BadRequest(new { error = "Nothing was selected." });

    // A child without its parent is an orphan in the target project, so the parents of
    // everything chosen come too. Somebody ticking three tasks under an epic means those
    // three tasks in that epic, not three tasks loose in a backlog.
    var byKey = all.ToDictionary(item => item.Key, StringComparer.Ordinal);
    var wanted = new HashSet<string>(chosen.Select(item => item.Key), StringComparer.Ordinal);

    foreach (var item in chosen)
    {
        var parent = item.ParentKey;

        while (parent is not null && wanted.Add(parent) && byKey.TryGetValue(parent, out var above))
        {
            parent = above.ParentKey;
        }
    }

    var items = all
        .Where(item => wanted.Contains(item.Key))
        .Select(item => new BacklogItem(
            item.Key, item.Type, item.Title, item.DescriptionHtml, item.AcceptanceCriteria,
            item.TestRequirement, item.Priority, item.StoryPoints, item.LowHours, item.HighHours,
            item.Tags, item.ParentKey, []))
        .ToList();

    var settings = ConnectionSettings(connection.SettingsJson);

    var secrets = context.RequestServices.GetRequiredService<ISecretStore>();
    var token = connection.SecretRef is { Length: > 0 } reference
        ? await secrets.GetAsync(SecretNames.For(reference, "secret"), context.RequestAborted)
        : null;

    if (string.IsNullOrWhiteSpace(token))
    {
        return Results.BadRequest(new { error = "This connection has no credential on it. Edit it and add one." });
    }

    // What each target calls the thing the connection points at, and what to call the
    // target when it cannot be reached. A bool held this while there were two of them and
    // was already doing three jobs; a third target is where that stops working.
    var (addressSetting, targetName) = connection.Mode switch
    {
        "jira" => ("siteUrl", "Jira"),
        "github" => ("owner", "GitHub"),
        _ => ("organisationUrl", "Azure DevOps")
    };

    settings.TryGetValue(addressSetting, out var host);

    if (string.IsNullOrWhiteSpace(host))
    {
        return Results.BadRequest(new
        {
            error = connection.Mode switch
            {
                "github" => "This connection has no owner on it. That is the organisation or user the "
                    + "repositories belong to.",
                _ => "This connection has no address on it."
            }
        });
    }

    try
    {
        // The three publishers answer the same shape and nothing below this line cares
        // which one ran. All three refuse more than two hundred items without a confirmed
        // count, and none reopens anything somebody closed.
        IReadOnlyList<(string Key, string Id, string Url, string Action)> published;

        // What worked less than completely. Empty for the two targets that have nothing to
        // half-do; GitHub can put an item in a repository and fail to nest it under its
        // parent, and an epic with nothing under it looks exactly like one that never had
        // children.
        IReadOnlyList<string> warnings = [];

        if (string.Equals(connection.Mode, "github", StringComparison.Ordinal))
        {
            settings.TryGetValue("apiBaseUrl", out var apiBase);

            using var gitHubClient = factory.CreateClient("github");
            GitHubPublisher.Authenticate(gitHubClient, token);

            var result = await new GitHubPublisher(gitHubClient, host, request.Project, apiBase).PublishAsync(
                items, request.DryRun, confirmedCount: items.Count, context.RequestAborted);

            published = [.. result.Items.Select(entry => (
                entry.Key,
                entry.Number.ToString(CultureInfo.InvariantCulture),
                entry.Url,
                entry.Action))];

            warnings = result.Warnings;
        }
        else if (string.Equals(connection.Mode, "jira", StringComparison.Ordinal))
        {
            settings.TryGetValue("email", out var email);

            var boards = context.RequestServices.GetRequiredService<JiraProjects>();
            using var jiraClient = boards.Authenticated(email ?? string.Empty, token);

            // Read from the project rather than assumed. A team managed project and a
            // company managed one do not offer the same issue types.
            var types = await JiraProjects.IssueTypesAsync(jiraClient, host, request.Project, context.RequestAborted);

            var result = await new JiraPublisher(jiraClient, host, request.Project, types).PublishAsync(
                items, request.DryRun, confirmedCount: items.Count, context.RequestAborted);

            published = [.. result.Select(entry => (entry.Key, entry.IssueKey, entry.Url, entry.Action))];
        }
        else
        {
            using var client = factory.CreateClient("devops");
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{token}")));

            // What the project actually accepts, read before anything is written. A project
            // on the Basic process has Epic, Issue and Task and none of the Agile fields,
            // and publishing a feature into it returned 400 with no explanation.
            var shape = await WorkItemPublisher.ReadShapeAsync(client, host, request.Project, context.RequestAborted);

            var result = await new WorkItemPublisher(client, host, request.Project, shape).PublishAsync(
                items, request.DryRun, confirmedCount: items.Count, context.RequestAborted);

            published = [.. result.Select(entry => (
                entry.Key,
                entry.WorkItemId.ToString(CultureInfo.InvariantCulture),
                entry.Url,
                entry.Action))];
        }

        if (!request.DryRun)
        {
            await analysis.WritePublishedAsync(run.Value,
                [.. published.Select(entry => (entry.Key, host, request.Project,

                    // The identifier column is an integer, which Azure DevOps work item
                    // identifiers and GitHub issue numbers are and Jira issue keys are not.
                    // A Jira key carries its project in it and is in the URL beside this, so
                    // nothing is lost by storing nought where there is no number.
                    int.TryParse(entry.Id, CultureInfo.InvariantCulture, out var numeric) ? numeric : 0,
                    entry.Url,
                    entry.Action))],
                context.RequestAborted);
        }

        return Results.Ok(new
        {
            request.DryRun,
            project = request.Project,
            requested = chosen.Count,
            published = published.Count,

            // Said because it is surprising. Ticking a task pulls in the epic above it, and
            // a count that came back larger than the number of boxes ticked needs to say
            // why before somebody thinks it published the wrong thing.
            parentsIncluded = items.Count - chosen.Count,

            // Said out loud rather than left for somebody to notice. A publish that wrote
            // every item and nested none of them is a success by count and a disappointment
            // to open, and the difference between those two is the argument of this product.
            warnings,
            items = published.Select(entry => new { entry.Key, id = entry.Id, entry.Url, entry.Action })
        });
    }
    catch (InvalidOperationException refused)
    {
        // The publishers' own refusals, which are written for a person to read.
        return Results.BadRequest(new { error = refused.Message });
    }
    catch (HttpRequestException failure)
    {
        return Results.BadRequest(new { error = $"{targetName} could not be reached: {failure.Message}" });
    }
}).RequireAuthorization();

// ------------------------------------------------------------------ overview --
// What the latest finished analysis adds up to. The overview reads this and nothing else, so
// the headline figures on the first screen of the product and the figures in the report come
// from the same stored score rather than from two additions that can disagree.
app.MapGet("/api/engagements/{engagementId:guid}/assessment",
    async (HttpContext context, AnalysisStore store, Guid engagementId) =>
{
    if (await Denied(context, engagementId, EngagementRoles.Viewer) is { } denied) return denied;

    var run = await store.GetLatestScoredRunAsync(engagementId, context.RequestAborted);

    // No run is not an empty estate, and the difference is the whole point of this product.
    // The screen draws "nothing discovered yet" from the null run rather than from zeros.
    if (run is null) return Results.Ok(new { runId = (Guid?)null, componentCount = 0 });

    var score = await store.GetScoreAsync(run.Value, context.RequestAborted);
    if (score is null) return Results.Ok(new { runId = (Guid?)null, componentCount = 0 });

    var findings = await store.GetFindingsAsync(run.Value, context.RequestAborted);

    var breakdown = JsonDocument.Parse(score.Value.BreakdownJson).RootElement;

    // Under "score", not at the root.
    //
    // The stored breakdown wraps the whole RunScore in a "score" property, and this read
    // the root, so it never found the figure and the overview reported no ratio at all
    // for every run this product has ever scored. It did not fail: a null share renders as
    // an absent number, which looks like an estate the ratio does not apply to.
    var figures = breakdown.TryGetProperty("score", out var nested)
        && nested.ValueKind == JsonValueKind.Object
            ? nested
            : breakdown;

    decimal? lowCodeShare = figures.TryGetProperty("LowCodeShare", out var share)
        && share.ValueKind == JsonValueKind.Number
        ? share.GetDecimal()
        : null;

    var componentTypes = await store.CountComponentTypesAsync(run.Value, context.RequestAborted);

    return Results.Ok(new
    {
        runId = run,
        componentCount = score.Value.ComponentsTotal,
        componentTypeCount = componentTypes,
        findingCount = score.Value.FindingsTotal,
        notAssessedCount = score.Value.NotAssessedCount,
        ruleCount = RuleCatalogue.All.Count,
        lowCodeShare,
        totalLowHours = score.Value.TotalLowHours,
        totalHighHours = score.Value.TotalHighHours,

        // The worst handful, for the panel under the figures. Severity order, not database
        // order: the first thing somebody reads on this screen should be the worst thing in
        // the estate rather than whichever row came back first.
        topFindings = findings
            .OrderBy(finding => SeverityRank(finding.Severity))
            .Take(5)
            .Select(finding => new
            {
                id = finding.FindingId,
                finding.Severity,

                // The rule's name, and under it where it is. It was the other way round
                // and the second line was the raw rule identifier, so the panel on the
                // first screen of the product read "alm.unmanagedInProduction" twice.
                title = RuleCatalogue.Find(finding.RuleId)?.Name ?? finding.RuleId,
                where = finding.ComponentName ?? finding.SolutionName ?? "solution wide"
            })
    });
}).RequireAuthorization();

// The order an engagement actually happens in, worked out from what exists rather than
// stored. There is no progress column for anybody to forget to update, and deleting a run
// puts the checklist back where it belongs on its own.
app.MapGet("/api/engagements/{engagementId:guid}/next-steps",
    async (HttpContext context, WorkspaceStore workspace, AnalysisStore analysis, Guid engagementId) =>
{
    if (await Denied(context, engagementId, EngagementRoles.Viewer) is { } denied) return denied;

    var connections = await workspace.ListConnectionsAsync(engagementId, context.RequestAborted);
    var runs = await workspace.ListRunsAsync(engagementId, context.RequestAborted);
    var scored = await analysis.GetLatestScoredRunAsync(engagementId, context.RequestAborted);

    var connected = connections.Count > 0;
    var analysed = scored is not null;

    var published = runs.Any(run =>
        string.Equals(run.Mode, "publish", StringComparison.Ordinal)
        && string.Equals(run.Status, "succeeded", StringComparison.Ordinal));

    // Exactly one step is current: the first thing that is not done. Everything after it is
    // blocked, because offering a button for work that cannot start yet is how somebody ends
    // up publishing a backlog from an analysis that never ran.
    var done = new[] { connected, analysed, analysed, published };

    var current = Array.IndexOf(done, false);

    return Results.Ok(stepIds.Select((id, index) => new
    {
        id,
        workspace = stepWorkspaces[index],
        state = done[index] ? "done" : index == current ? "current" : "blocked"
    }));
}).RequireAuthorization();

// ---------------------------------------------------------------- engagements --
app.MapDelete("/api/engagements/{engagementId:guid}", async (HttpContext context, WorkspaceStore store, Guid engagementId) =>
{
    if (await Denied(context, engagementId, EngagementRoles.Admin) is { } denied) return denied;

    var removed = await store.DeleteEngagementAsync(engagementId, context.RequestAborted);

    return removed == 0 ? Results.NotFound() : Results.NoContent();
}).RequireAuthorization();

// ----------------------------------------------------------------------- runs --
app.MapGet("/api/engagements/{engagementId:guid}/runs", async (HttpContext context, WorkspaceStore store, Guid engagementId) =>
{
    if (await Denied(context, engagementId, EngagementRoles.Viewer) is { } denied) return denied;

    var runs = await store.ListRunsAsync(engagementId, context.RequestAborted);

    return Results.Ok(runs.Select(run => new
    {
        run.RunId,
        run.Mode,
        run.Status,
        run.CreatedBy,
        run.CreatedUtc,
        run.StartedUtc,
        run.CompletedUtc,
        run.Error,

        // Which runs are allowed to write, read from the contract rather than from a list
        // kept here. One stage in analysis-stages.json declares a target write and a test
        // asserts it is the publish, so this cannot drift from what the pipeline does.
        writes = string.Equals(run.Mode, "publish", StringComparison.Ordinal)
    }));
}).RequireAuthorization();

app.MapGet("/api/engagements/{engagementId:guid}/runs/{runId:guid}/discovery",
    async (HttpContext context, AnalysisStore store, Guid engagementId, Guid runId) =>
{
    if (await Denied(context, engagementId, EngagementRoles.Viewer) is { } denied) return denied;

    var reads = await store.GetEntityReadsAsync(runId, context.RequestAborted);

    return Results.Ok(new
    {
        runId,

        // Every component type the run tried to read, whether it succeeded, and why not when
        // it did not. The failures are the point: a type that could not be read has to look
        // different from a type with nothing in it, and this is where that distinction lives.
        entities = reads.Select(read => new
        {
            componentTypeId = read.ComponentTypeId,
            read.EvidenceSource,
            read.Succeeded,
            read.RecordCount,
            error = read.FailureReason
        })
    });
}).RequireAuthorization();

// ---------------------------------------------------------------- connections --
app.MapPost("/api/engagements/{engagementId:guid}/connections",
    async (HttpContext context, WorkspaceStore store, Guid engagementId, CreateConnection request) =>
{
    ArgumentNullException.ThrowIfNull(request);

    if (await Denied(context, engagementId, EngagementRoles.Contributor) is { } denied) return denied;

    if (!ExtractionModesList().Contains(request.Mode, StringComparer.Ordinal))
    {
        return Results.BadRequest(new
        {
            error = $"'{request.Mode}' is not a mode. It is one of {string.Join(", ", ExtractionModesList())}."
        });
    }

    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return Results.BadRequest(new { error = "A connection needs a name, so the person choosing one on a run can tell them apart." });
    }

    var secrets = context.RequestServices.GetRequiredService<ISecretStore>();
    string? secretRef = null;

    if (!string.IsNullOrWhiteSpace(request.Secret))
    {
        // Refused rather than stored somewhere else. A development fallback that quietly puts
        // a client's credential in the database is the kind of convenience that reaches
        // production, and the database is read by every screen and included in every export.
        if (!secrets.IsConfigured)
        {
            return Results.BadRequest(new
            {
                error = "No Key Vault is configured, so there is nowhere to put this credential. Secrets are never written to the database."
            });
        }

        var prefix = SecretNames.NewPrefix();
        await secrets.SetAsync(SecretNames.For(prefix, "secret"), request.Secret, context.RequestAborted);
        secretRef = prefix;
    }

    var connection = new Connection(
        Guid.NewGuid(),
        engagementId,
        request.Mode,
        request.Name.Trim(),
        string.IsNullOrWhiteSpace(request.EnvironmentRole) ? "unknown" : request.EnvironmentRole,
        JsonSerializer.Serialize(request.Settings ?? new Dictionary<string, string>()),
        secretRef,
        request.SecretExpiresUtc,
        null, null, null, null, null);

    await store.CreateConnectionAsync(connection, UserId(context.User), context.RequestAborted);

    return Results.Created(
        $"/api/engagements/{engagementId}/connections",
        new { connection.ConnectionId, connection.Mode, connection.Name, connection.EnvironmentRole });
}).RequireAuthorization();

// Changing an engagement, which was also not possible.
//
// A client's name typed wrong on the day it was created stayed wrong on the cover of every
// report afterwards, and the report language could only be chosen at creation, before
// anybody knew who was going to read it.
app.MapPut("/api/engagements/{engagementId:guid}",
    async (HttpContext context, WorkspaceStore store, Guid engagementId, UpdateEngagement request) =>
{
    ArgumentNullException.ThrowIfNull(request);

    // Admin rather than contributor. Renaming an engagement changes what every report it
    // produces says on its cover.
    if (await Denied(context, engagementId, EngagementRoles.Admin) is { } denied) return denied;

    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return Results.BadRequest(new { error = "An engagement needs a name." });
    }

    // Refused rather than stored. A language this product does not ship would fall through
    // every lookup and print resource keys down the page of a document with a client's name
    // on it.
    foreach (var language in new[] { request.ReportLanguage, request.BacklogLanguage })
    {
        if (language is { Length: > 0 } code && !LocaleCatalogue.All.Any(locale => locale.Code == code))
        {
            return Results.BadRequest(new { error = $"'{code}' is not a language this product ships." });
        }
    }

    var updated = await store.UpdateEngagementAsync(
        engagementId,
        request.Name.Trim(),
        string.IsNullOrWhiteSpace(request.ClientName) ? null : request.ClientName.Trim(),
        request.IsRegulated,
        request.ReportLanguage ?? "en",
        request.BacklogLanguage ?? request.ReportLanguage ?? "en",
        context.RequestAborted);

    return updated ? Results.Ok(new { engagementId, request.Name }) : Results.NotFound();
}).RequireAuthorization();

// Changing one, which was not possible at all.
//
// A connection could be created and never touched again, so a mistyped environment URL or
// a rotated credential meant adding a second connection and leaving the wrong one in the
// picker. That is how somebody eventually runs a discovery against the wrong environment.
app.MapPut("/api/engagements/{engagementId:guid}/connections/{connectionId:guid}",
    async (HttpContext context, WorkspaceStore store, Guid engagementId, Guid connectionId, UpdateConnection request) =>
{
    ArgumentNullException.ThrowIfNull(request);

    if (await Denied(context, engagementId, EngagementRoles.Contributor) is { } denied) return denied;

    var existing = await store.GetConnectionAsync(connectionId, context.RequestAborted);

    // Checked against the engagement in the route rather than trusted from the body. A
    // contributor on one engagement must not be able to rename a connection on another by
    // knowing its identifier.
    if (existing is null || existing.EngagementId != engagementId) return Results.NotFound();

    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return Results.BadRequest(new { error = "A connection needs a name, so the person choosing one on a run can tell them apart." });
    }

    var updated = await store.UpdateConnectionAsync(
        connectionId,
        request.Name.Trim(),
        string.IsNullOrWhiteSpace(request.EnvironmentRole) ? "unknown" : request.EnvironmentRole,
        JsonSerializer.Serialize(request.Settings ?? new Dictionary<string, string>()),
        request.SecretExpiresUtc,
        context.RequestAborted);

    if (!updated) return Results.NotFound();

    // The credential, only where a new one was typed. An empty secret means leave the one
    // in the vault alone, which is what a rename has to do: the alternative is that every
    // edit of a name asks somebody to paste a client's credential again.
    if (!string.IsNullOrWhiteSpace(request.Secret))
    {
        var secrets = context.RequestServices.GetRequiredService<ISecretStore>();

        if (!secrets.IsConfigured)
        {
            return Results.BadRequest(new
            {
                error = "No Key Vault is configured, so there is nowhere to put this credential. Secrets are never written to the database."
            });
        }

        // A new prefix rather than overwriting the old secret in place. A run that is in
        // flight is holding the reference it started with, and replacing the value under
        // it changes what that run is using halfway through.
        var prefix = SecretNames.NewPrefix();
        await secrets.SetAsync(SecretNames.For(prefix, "secret"), request.Secret, context.RequestAborted);
        await store.SetConnectionSecretAsync(connectionId, prefix, request.SecretExpiresUtc, context.RequestAborted);
    }

    return Results.Ok(new { connectionId, request.Name, request.EnvironmentRole });
}).RequireAuthorization();

// Removing one, which was also not possible.
//
// Only where nothing has been read through it. A run records the connection that produced
// it, and a report that cannot say what it was read through is a report nobody can defend,
// so a connection with history stays and the caller is told how much history.
app.MapDelete("/api/engagements/{engagementId:guid}/connections/{connectionId:guid}",
    async (HttpContext context, WorkspaceStore store, Guid engagementId, Guid connectionId) =>
{
    if (await Denied(context, engagementId, EngagementRoles.Contributor) is { } denied) return denied;

    var existing = await store.GetConnectionAsync(connectionId, context.RequestAborted);

    if (existing is null || existing.EngagementId != engagementId) return Results.NotFound();

    var runs = await store.CountRunsUsingAsync(connectionId, context.RequestAborted);

    if (runs > 0)
    {
        return Results.Conflict(new
        {
            error = $"{runs} run(s) were read through this connection, so it stays. A report has to be able to say what produced it.",
            runs
        });
    }

    // The credential goes with it. A connection row holds a reference rather than a
    // secret, so deleting the row on its own leaves a client's credential in the vault
    // with nothing pointing at it and nobody able to tell what it was for.
    if (existing.SecretRef is { Length: > 0 } reference)
    {
        var secrets = context.RequestServices.GetRequiredService<ISecretStore>();

        if (secrets.IsConfigured)
        {
            await secrets.DeleteAsync(SecretNames.For(reference, "secret"), context.RequestAborted);
        }
    }

    await store.DeleteConnectionAsync(connectionId, context.RequestAborted);

    return Results.NoContent();
}).RequireAuthorization();

// ----------------------------------------------------------------- narrative --
// The half of an assessment that comes from talking to people.
//
// Five sections of the report are written rather than generated. The list of which ones,
// and the prompt for each, comes from report-model.json: the contract already describes the
// report and a second copy here would drift the first time somebody added a section.
app.MapGet("/api/report-sections", () =>
{
    var path = ContractFiles.Path("report-model.json");

    if (!File.Exists(path))
    {
        return Results.Problem(
            "The report model contract is not in this image. The written sections are generated from it.",
            statusCode: StatusCodes.Status500InternalServerError);
    }

    using var document = JsonDocument.Parse(File.ReadAllText(path));

    // Materialised inside the using. A lazy sequence handed to the serialiser is enumerated
    // after this method returns, by which time the document has been disposed.
    var sections = document.RootElement.GetProperty("sections").EnumerateArray()
        .Where(section => section.GetProperty("kind").GetString() is "written" or "hybrid")
        .Select(section => new
        {
            id = section.GetProperty("id").GetString(),
            name = section.GetProperty("name").GetString(),
            kind = section.GetProperty("kind").GetString(),
            prompt = section.TryGetProperty("prompt", out var prompt) ? prompt.GetString() : null,
            context = section.TryGetProperty("content", out var content) ? content.GetString() : null
        })
        .ToList();

    return Results.Ok(sections);
}).RequireAuthorization();

app.MapGet("/api/engagements/{engagementId:guid}/narrative",
    async (HttpContext context, AnalysisStore analysis, Guid engagementId) =>
{
    if (await Denied(context, engagementId, EngagementRoles.Viewer) is { } denied) return denied;

    var written = await analysis.GetNarrativeAsync(engagementId, context.RequestAborted);

    return Results.Ok(written.Select(section => new
    {
        section.SectionId,
        section.Body,
        section.UpdatedUtc,
        section.UpdatedByName
    }));
}).RequireAuthorization();

app.MapPut("/api/engagements/{engagementId:guid}/narrative/{sectionId}",
    async (HttpContext context, AnalysisStore analysis, Guid engagementId, string sectionId, WriteNarrative request) =>
{
    ArgumentNullException.ThrowIfNull(request);

    // A contributor's act. Writing the management summary is doing the assessment, not
    // reading it, and a viewer who can rewrite the conclusion is not a viewer.
    if (await Denied(context, engagementId, EngagementRoles.Contributor) is { } denied) return denied;

    await analysis.SaveNarrativeAsync(
        engagementId, sectionId, request.Body, UserId(context.User), DisplayName(context.User), context.RequestAborted);

    return Results.Ok(new { saved = !string.IsNullOrWhiteSpace(request.Body) });
}).RequireAuthorization();

// ------------------------------------------------------------------ maturity --
// Sixteen capability axes, scored nought to five by somebody who sat in the interviews.
// The axes come from report-model.json; the numbers come from a person and never from
// anything this product infers.
app.MapGet("/api/maturity-axes", () =>
{
    var path = ContractFiles.Path("report-model.json");

    if (!File.Exists(path))
    {
        return Results.Problem(
            "The report model contract is not in this image. The scoring form is generated from it.",
            statusCode: StatusCodes.Status500InternalServerError);
    }

    using var document = JsonDocument.Parse(File.ReadAllText(path));
    var axes = document.RootElement.GetProperty("maturityAxes");

    // Materialised inside the using, or the serialiser reads a disposed document.
    var groups = axes.GetProperty("groups").EnumerateArray()
        .Select(group => new
        {
            id = group.GetProperty("id").GetString(),
            axes = group.GetProperty("axes").EnumerateArray().Select(axis => axis.GetString()).ToList()
        })
        .ToList();

    return Results.Ok(new { scale = axes.GetProperty("scale").GetString(), groups });
}).RequireAuthorization();

app.MapGet("/api/engagements/{engagementId:guid}/maturity",
    async (HttpContext context, AnalysisStore analysis, Guid engagementId) =>
{
    if (await Denied(context, engagementId, EngagementRoles.Viewer) is { } denied) return denied;

    var scores = await analysis.GetMaturityAsync(engagementId, context.RequestAborted);

    return Results.Ok(scores.Select(score => new
    {
        score.AxisId,
        score.Score,
        score.Evidence,
        score.UpdatedByName
    }));
}).RequireAuthorization();

app.MapPut("/api/engagements/{engagementId:guid}/maturity/{axisId}",
    async (HttpContext context, AnalysisStore analysis, Guid engagementId, string axisId, ScoreAxis request) =>
{
    ArgumentNullException.ThrowIfNull(request);

    if (await Denied(context, engagementId, EngagementRoles.Contributor) is { } denied) return denied;

    if (request.Score is { } value && (value < 0 || value > 5))
    {
        return Results.BadRequest(new { error = "The scale is nought to five." });
    }

    await analysis.SaveMaturityAsync(
        engagementId, axisId, request.Score, request.Evidence,
        UserId(context.User), DisplayName(context.User), context.RequestAborted);

    return Results.Ok(new { scored = request.Score is not null });
}).RequireAuthorization();

// ------------------------------------------------------- interactive sign in --
// Two endpoints and a round trip through Entra.
//
// The connection is created first, by the ordinary connections endpoint, carrying only the
// environment address. This sends the browser off to sign in to that environment, and the
// callback below turns what comes back into a stored refresh token the worker can use.
app.MapGet("/api/connections/{connectionId:guid}/authorize",
    async (HttpContext context, WorkspaceStore store, InteractiveSignIn signIn, Guid connectionId) =>
{
    var connection = await store.GetConnectionAsync(connectionId, context.RequestAborted);

    if (connection is null) return Results.NotFound();

    // Checked against the engagement the connection belongs to, not against the connection
    // id somebody happens to hold. Configuring a connection is a contributor's act.
    if (await Denied(context, connection.EngagementId, EngagementRoles.Contributor) is { } denied) return denied;

    if (!signIn.IsConfigured) return Results.Problem(signIn.NotConfiguredReason, statusCode: StatusCodes.Status503ServiceUnavailable);

    var target = signIn.AuthorizeUrl(connection, CallbackUri(context));

    return target is null
        ? Results.BadRequest(new { error = "This connection has no usable environment address on it." })
        : Results.Redirect(target.ToString());
}).RequireAuthorization();

// Entra sends the browser here. Anonymous to the product's own authorization filter would be
// wrong: the reader is signed in, and the state is signed as well, so both halves are checked.
app.MapGet("/api/connections/callback",
    async (HttpContext context, InteractiveSignIn signIn, string? code, string? state, string? error, string? error_description) =>
{
    // Somebody cancelled at the Microsoft prompt, or consent was refused. Not a failure of
    // this product, and it should not read like one.
    if (!string.IsNullOrWhiteSpace(error))
    {
        return Results.Redirect($"/app/#Connections?signin=cancelled&reason={Uri.EscapeDataString(error_description ?? error)}");
    }

    if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
    {
        return Results.BadRequest(new { error = "Entra returned neither a code nor an error." });
    }

    var outcome = await signIn.CompleteAsync(state, code, CallbackUri(context), context.RequestAborted);

    if (outcome is null) return Results.BadRequest(new { error = "This sign-in does not belong to this deployment." });

    // Checked after the state is unwrapped, because until then there is no engagement to
    // check against. The state is signed, so it cannot name an engagement of its own choosing.
    if (await Denied(context, outcome.EngagementId, EngagementRoles.Contributor) is not null)
    {
        return Results.Forbid();
    }

    return Results.Redirect(outcome.Succeeded
        ? "/app/#Connections?signin=done"
        : $"/app/#Connections?signin=failed&reason={Uri.EscapeDataString(outcome.Message)}");
}).RequireAuthorization();

// ------------------------------------------------------------------- uploads --
// An exported solution, on its way to becoming an offline connection.
//
// Separate from creating the connection, because a file upload and a form submission are
// different shapes on the wire and pretending otherwise means a multipart body carrying a
// hundred megabytes through the same code path that validates a tenant id. The wizard
// uploads first, gets a blob name back, and saves that as a setting.
app.MapPost("/api/engagements/{engagementId:guid}/uploads",
    async (HttpContext context, SolutionUploads uploads, Guid engagementId) =>
{
    if (await Denied(context, engagementId, EngagementRoles.Contributor) is { } denied) return denied;

    if (!uploads.IsConfigured)
    {
        return Results.Problem(
            "This deployment has no upload container configured, so the offline mode cannot be used here.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    if (!context.Request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "Send the file as multipart form data." });
    }

    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var file = form.Files["file"] ?? (form.Files.Count > 0 ? form.Files[0] : null);

    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new { error = "No file arrived." });
    }

    await using var content = file.OpenReadStream();

    var result = await uploads.StoreAsync(engagementId, content, file.FileName, context.RequestAborted);

    if (result.Error is not null) return Results.BadRequest(new { error = result.Error });

    // The blob name is what the connection stores and the worker opens. The browser never
    // sees a URL to the container, because it has no business reaching it directly.
    return Results.Ok(new { blobName = result.BlobName, bytes = result.Bytes, fileName = file.FileName });
}).RequireAuthorization().DisableAntiforgery();

// ----------------------------------------------------------------------- auth --
// Anonymous on purpose: it is the first call the web application makes and it is what tells
// the shell whether to draw a sign-in button or a product. Nothing here is privileged. When
// nobody is signed in it says so and says nothing else.
app.MapGet("/api/auth/status", async (HttpContext context) =>
{
    // The same answer the pipeline was built from, not a second opinion about it. This
    // asked only whether a client id was present, so a deployment with a client id and no
    // tenant would run with local sign-in while telling the shell that Entra was
    // configured, and the shell would draw a sign-out link to a scheme that is not there.
    var configured = entraConfigured;
    var authenticated = context.User.Identity?.IsAuthenticated == true;

    if (!authenticated)
    {
        return Results.Ok(new
        {
            authConfigured = configured,
            authenticated = false,
            registered = false,
            adminContact = adminContact
        });
    }

    var store = context.RequestServices.GetRequiredService<AccessStore>();
    var userId = UserId(context.User);

    await store.RecordDisplayNameAsync(userId, DisplayName(context.User), context.RequestAborted);

    var access = await AccessFor(context);
    var preferences = await store.GetPreferencesAsync(userId, context.RequestAborted);

    return Results.Ok(new
    {
        authConfigured = configured,
        authenticated = true,

        // Admitted to the product, which is not the same as having an engagement. The shell
        // draws a different screen for each, and reporting one as the other is how somebody
        // ends up looking at an empty product with no explanation.
        registered = access.IsKnown,
        userId,
        displayName = DisplayName(context.User),
        access.IsGlobalAdmin,
        adminContact = adminContact,
        language = preferences.Language,
        theme = preferences.Theme,

        // Where they were last time, but only if it still exists and is still theirs.
        //
        // Checked here rather than trusted, because an engagement can be deleted and a
        // grant can be revoked between one sign-in and the next, and a shell that tried to
        // open an engagement somebody no longer holds would show them a screen full of
        // failed requests instead of the product.
        lastEngagementId = preferences.LastEngagementId is { } last
            && access.Holds(last, EngagementRoles.Viewer)
                ? last
                : (Guid?)null
    });
});

// ------------------------------------------------------------------------- me --
app.MapPut("/api/me/language", async (HttpContext context, SetLanguage request) =>
{
    ArgumentNullException.ThrowIfNull(request);

    // A language nobody ships is a language nothing can render, so it is refused here rather
    // than stored and handed back to a browser that will fall back to English anyway.
    if (request.Language is not null
        && !LocaleCatalogue.All.Any(locale => string.Equals(locale.Code, request.Language, StringComparison.OrdinalIgnoreCase)))
    {
        return Results.BadRequest(new { error = $"'{request.Language}' is not a language this product ships." });
    }

    var store = context.RequestServices.GetRequiredService<AccessStore>();
    await store.SetLanguageAsync(UserId(context.User), request.Language, context.RequestAborted);

    return Results.NoContent();
}).RequireAuthorization();

app.MapPut("/api/me/theme", async (HttpContext context, SetTheme request) =>
{
    ArgumentNullException.ThrowIfNull(request);

    if (request.Theme is not null and not "light" and not "dark")
    {
        return Results.BadRequest(new { error = "A theme is 'light', 'dark', or absent to follow the browser." });
    }

    var store = context.RequestServices.GetRequiredService<AccessStore>();
    await store.SetThemeAsync(UserId(context.User), request.Theme, context.RequestAborted);

    return Results.NoContent();
}).RequireAuthorization();

// Where somebody was working, so signing in puts them back there.
//
// Against the person rather than the browser. Signing in redirects through Entra and comes
// back to a freshly loaded page, so a selection held in the browser is destroyed by the one
// action most likely to come just before wanting it, and every reader landed in whichever
// engagement sorted first. That is the demonstration estate.
app.MapPut("/api/me/engagement", async (HttpContext context, SetEngagement request) =>
{
    ArgumentNullException.ThrowIfNull(request);

    if (request.EngagementId is { } engagementId
        && await Denied(context, engagementId, EngagementRoles.Viewer) is { } denied)
    {
        return denied;
    }

    var store = context.RequestServices.GetRequiredService<AccessStore>();
    await store.SetLastEngagementAsync(UserId(context.User), request.EngagementId, context.RequestAborted);

    return Results.NoContent();
}).RequireAuthorization();

// ---------------------------------------------------------------------- users --
// Admitting somebody to the product and giving them an engagement are separate decisions,
// made by different people at different times, so they are separate endpoints.
app.MapGet("/api/users", async (HttpContext context) =>
{
    var access = await AccessFor(context);
    if (!access.IsGlobalAdmin) return Results.Forbid();

    var store = context.RequestServices.GetRequiredService<AccessStore>();

    var users = await store.ListUsersAsync(context.RequestAborted);
    var grants = await store.ListAllGrantsAsync(context.RequestAborted);

    // Composed here rather than fetched per row. Forty people would otherwise be forty
    // requests to draw one screen, and the screen is drawn every time somebody opens it.
    var byUser = grants
        .GroupBy(grant => grant.UserId, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

    return Results.Ok(users.Select(user => new
    {
        user.UserId,
        user.DisplayName,
        user.Email,
        user.IsGlobalAdmin,
        user.CreatedUtc,
        user.CreatedBy,
        engagementAccess = byUser.TryGetValue(user.UserId, out var mine)
            ? mine.Select(grant => new { grant.EngagementId, grant.EngagementName, grant.Role })
            : []
    }));
}).RequireAuthorization();

app.MapPut("/api/users", async (HttpContext context, AdmitUser request) =>
{
    ArgumentNullException.ThrowIfNull(request);

    var access = await AccessFor(context);
    if (!access.IsGlobalAdmin) return Results.Forbid();

    if (string.IsNullOrWhiteSpace(request.Upn))
    {
        return Results.BadRequest(new { error = "A sign-in name is required." });
    }

    var store = context.RequestServices.GetRequiredService<AccessStore>();

    await store.AdmitAsync(
        request.Upn,
        string.IsNullOrWhiteSpace(request.DisplayName) ? request.Upn : request.DisplayName,
        request.IsGlobalAdmin,
        UserId(context.User),
        context.RequestAborted);

    return Results.NoContent();
}).RequireAuthorization();

app.MapPut("/api/users/{userId}/global-admin", async (HttpContext context, string userId, SetGlobalAdmin request) =>
{
    ArgumentNullException.ThrowIfNull(request);

    var access = await AccessFor(context);
    if (!access.IsGlobalAdmin) return Results.Forbid();

    var store = context.RequestServices.GetRequiredService<AccessStore>();
    var users = await store.ListUsersAsync(context.RequestAborted);

    var user = users.FirstOrDefault(candidate =>
        string.Equals(candidate.UserId, AccessStore.Normalise(userId), StringComparison.OrdinalIgnoreCase));

    if (user is null) return Results.NotFound();

    // The last administrator cannot demote themselves. There would be nobody left who could
    // admit anyone, including the person who has just locked the door.
    if (!request.IsGlobalAdmin && user.IsGlobalAdmin && await store.CountGlobalAdminsAsync(context.RequestAborted) <= 1)
    {
        return Results.BadRequest(new
        {
            error = "This is the only global administrator. Make somebody else one first, or nobody will be able to admit anyone."
        });
    }

    await store.AdmitAsync(user.UserId, user.DisplayName, request.IsGlobalAdmin, UserId(context.User), context.RequestAborted);

    return Results.NoContent();
}).RequireAuthorization();

app.MapDelete("/api/users/{userId}", async (HttpContext context, string userId) =>
{
    var access = await AccessFor(context);
    if (!access.IsGlobalAdmin) return Results.Forbid();

    var store = context.RequestServices.GetRequiredService<AccessStore>();
    var normalised = AccessStore.Normalise(userId);

    var users = await store.ListUsersAsync(context.RequestAborted);
    var user = users.FirstOrDefault(candidate =>
        string.Equals(candidate.UserId, normalised, StringComparison.OrdinalIgnoreCase));

    if (user is null) return Results.NotFound();

    if (user.IsGlobalAdmin && await store.CountGlobalAdminsAsync(context.RequestAborted) <= 1)
    {
        return Results.BadRequest(new
        {
            error = "This is the only global administrator. Removing them would leave nobody who can admit anyone."
        });
    }

    var removed = await store.RemoveAsync(normalised, context.RequestAborted);

    return removed == 0 ? Results.NotFound() : Results.NoContent();
}).RequireAuthorization();

// ------------------------------------------------------------ engagement access --
app.MapGet("/api/engagements/{engagementId:guid}/access", async (HttpContext context, Guid engagementId) =>
{
    if (await Denied(context, engagementId, EngagementRoles.Admin) is { } denied) return denied;

    var store = context.RequestServices.GetRequiredService<AccessStore>();
    var members = await store.GetEngagementMembersAsync(engagementId, context.RequestAborted);

    return Results.Ok(members);
}).RequireAuthorization();

app.MapGet("/api/engagements/{engagementId:guid}/available-users", async (HttpContext context, Guid engagementId) =>
{
    if (await Denied(context, engagementId, EngagementRoles.Admin) is { } denied) return denied;

    var store = context.RequestServices.GetRequiredService<AccessStore>();
    var candidates = await store.GetAvailableUsersAsync(engagementId, context.RequestAborted);

    return Results.Ok(candidates);
}).RequireAuthorization();

app.MapPut("/api/engagements/{engagementId:guid}/access", async (HttpContext context, Guid engagementId, GrantAccess request) =>
{
    ArgumentNullException.ThrowIfNull(request);

    if (await Denied(context, engagementId, EngagementRoles.Admin) is { } denied) return denied;

    var role = EngagementRoles.Canonical(request.Role);
    if (role is null) return Results.BadRequest(new { error = $"'{request.Role}' is not a role. It is Admin, Contributor or Viewer." });

    var store = context.RequestServices.GetRequiredService<AccessStore>();
    await store.GrantAsync(engagementId, request.Upn, role, UserId(context.User), context.RequestAborted);

    return Results.NoContent();
}).RequireAuthorization();

app.MapDelete("/api/engagements/{engagementId:guid}/access/{userId}", async (HttpContext context, Guid engagementId, string userId) =>
{
    if (await Denied(context, engagementId, EngagementRoles.Admin) is { } denied) return denied;

    var store = context.RequestServices.GetRequiredService<AccessStore>();
    var removed = await store.RevokeAsync(engagementId, userId, context.RequestAborted);

    return removed == 0 ? Results.NotFound() : Results.NoContent();
}).RequireAuthorization();

// -------------------------------------------------------------------- locales --
// The web application imports English at build time and fetches every other language from
// here. Without this endpoint the picker offers six languages and quietly renders five of
// them in English, because the fetch fails and the client is written to keep English rather
// than blank the screen. A silent fallback is the right behaviour and a missing endpoint is
// not the reason to exercise it.
app.MapGet("/api/locales", () => Results.Ok(LocaleCatalogue.All.Select(locale => new
{
    code = locale.Code,
    englishName = locale.EnglishName,
    nativeName = locale.NativeName,
    culture = locale.Culture
})));

app.MapGet("/api/locales/{code}/{ns}", (string code, string ns) =>
{
    // Both halves are path segments and both end up in a file name, so neither is allowed to
    // be anything but a name this product already knows. A catalogue lookup rather than a
    // sanitising pass: there is a fixed list of each and anything outside it is not a typo to
    // be corrected, it is a path being probed.
    var locale = LocaleCatalogue.All.FirstOrDefault(candidate =>
        string.Equals(candidate.Code, code, StringComparison.OrdinalIgnoreCase));

    if (locale is null) return Results.NotFound();

    if (!LocaleCatalogue.Namespaces.Contains(ns, StringComparer.Ordinal)) return Results.NotFound();

    var path = Path.Combine(
        AppContext.BaseDirectory,
        "src", "PowerPete.Analyzer.Domain", "Localization", "Resources",
        $"{ns}.{locale.Code}.json");

    // An absent bundle is not an error. A namespace nobody has translated yet falls back to
    // English on the client, which is the documented behaviour of every locale but English.
    if (!File.Exists(path)) return Results.Ok(new Dictionary<string, string>());

    return Results.Text(File.ReadAllText(path), "application/json");
});

// --------------------------------------------------------------------- health --
app.MapHealthChecks("/healthz");

// The two paths the container app actually probes. They were never mapped, so both returned
// 404, the liveness probe failed three times at thirty second intervals and the platform
// killed the container: a clean restart loop roughly every seventy seconds, with the
// application logging a successful start every time round.
//
// Liveness answers whether this process should be restarted and readiness whether it should
// be sent traffic. Neither consults the database on purpose. A database that is paused, and
// Azure SQL serverless pauses itself, is not a reason to restart a healthy API, and a probe
// that restarts the thing it is measuring turns a slow dependency into an outage.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = _ => false });

app.MapGet("/api/system/health", async (HttpContext context) =>
{
    var access = await AccessFor(context);
    if (!access.IsGlobalAdmin) return Results.Forbid();

    var health = context.RequestServices.GetRequiredService<SystemHealth>();
    var checks = await health.RunAsync(context.RequestAborted);

    // Shaped here rather than returned raw, for two reasons the screen could not survive.
    //
    // The screen reads { checks, checkedUtc } and this returned the bare array, so it read
    // checks off an array, got undefined, called filter on it and threw during render. React
    // unmounts the tree when a render throws, so the whole tab went blank rather than showing
    // an error: the one page somebody opens when something is already wrong.
    //
    // And the state is an enum. Without a string converter it serialises as 0, 1, 2, and the
    // screen calls toLowerCase on it, which throws the same way. Converted here rather than
    // by registering a global converter, because that would quietly change the shape of every
    // other endpoint in this file.
    return Results.Ok(new
    {
        checks = checks.Select(check => new
        {
            check.Id,
            check.Name,
            check.Group,
            state = check.State.ToString(),
            check.Detail,
            check.Remediation,
            check.Command
        }).ToList(),
        checkedUtc = DateTime.UtcNow
    });
}).RequireAuthorization();

app.MapPut("/api/system/settings/syncfusion", async (HttpContext context, SetSyncfusionKey request) =>
{
    ArgumentNullException.ThrowIfNull(request);

    var access = await AccessFor(context);
    if (!access.IsGlobalAdmin) return Results.Forbid();

    if (string.IsNullOrWhiteSpace(request.Key)) return Results.BadRequest(new { error = "A key is required." });

    var health = context.RequestServices.GetRequiredService<SystemHealth>();
    var result = await health.SaveSyncfusionKeyAsync(request.Key, context.RequestAborted);

    // Whether the key covers document processing travels with the answer. Syncfusion does not
    // refuse a key that does not, it watermarks every page, and finding that out from a
    // document a client was asked to sign is the outcome this reports its way out of.
    return Results.Ok(new { stored = result.Stored, coversPdf = result.CoversPdf, note = result.Note });
}).RequireAuthorization();

// The single page application owns every route the API does not, including the root.
//
// It is built to wwwroot/app with a base of /app/, because the root was meant to hold the
// public microsite. There is no microsite, so the root held nothing: / and /app/ both
// returned 404 and only /app/index.html answered, which is a product nobody can find.
//
// Pointed at the application itself rather than moving it. When a microsite exists it takes
// the root back and this goes back to serving it.
app.MapFallbackToFile("app/index.html");

app.Run();

/// <summary>One capability axis, as somebody scored it.</summary>
/// <param name="Score">Nought to five, or null to take the score away.</param>
/// <param name="Evidence">Who told them.</param>
internal sealed record ScoreAxis(decimal? Score, string? Evidence);

/// <summary>One written section, on its way to being stored.</summary>
/// <param name="Body">What the consultant wrote. Blank removes the section.</param>
internal sealed record WriteNarrative(string? Body);

/// <summary>A connection being added to an engagement.</summary>
/// <param name="Mode">servicePrincipal, delegated, offlineZip or azureDevOps.</param>
/// <param name="Name">What it is called here, so a run can tell two apart.</param>
/// <param name="EnvironmentRole">production, test or unknown. It widens nothing and warns.</param>
/// <param name="Settings">Everything that is not a credential.</param>
/// <param name="Secret">The credential, which goes to the vault and never to the database.</param>
/// <param name="SecretExpiresUtc">When it lapses, for the health page to count down to.</param>
internal sealed record CreateConnection(
    string Mode,
    string Name,
    string? EnvironmentRole,
    Dictionary<string, string>? Settings,
    string? Secret,
    DateTime? SecretExpiresUtc);

/// <summary>A replacement Syncfusion licence key.</summary>
/// <param name="Key">The key, from the Syncfusion account.</param>
internal sealed record SetSyncfusionKey(string Key);

/// <summary>One stage of the pipeline, as the contract declares it.</summary>
/// <param name="Id">Its identifier, which the worker records against.</param>
/// <param name="Name">What it is called on screen.</param>
/// <param name="Description">What it does, for somebody watching it happen.</param>
/// <param name="Retryable">Whether running it again is a sensible thing to offer.</param>
internal sealed record PipelineStage(string Id, string Name, string Description, bool Retryable);

/// <summary>
/// What a person chose when a run stopped to ask.
/// </summary>
/// <param name="Solutions">The unique names that were ticked. Empty is an answer, not an absence.</param>
/// <param name="SolutionChecker">Whether to run Microsoft's checker, null to keep the mode's default.</param>
/// <param name="ModelEstimates">Whether to estimate with a model, null to keep the mode's default.</param>
/// <param name="EnvironmentHealth">Whether to report what the identity reaches, null to keep the mode's default.</param>
/// <param name="ExportSolutions">Whether to export the chosen solutions, null to keep the mode's default.</param>
internal sealed record ChooseSolutions(
    IReadOnlyList<string>? Solutions,
    bool? SolutionChecker,
    bool? ModelEstimates,
    bool? EnvironmentHealth,
    bool? ExportSolutions);

/// <summary>Where somebody was working.</summary>
/// <param name="EngagementId">The engagement, or null to forget it.</param>
internal sealed record SetEngagement(Guid? EngagementId);

/// <summary>The language somebody reads the product in.</summary>
/// <param name="Language">A locale code, or null to follow the default.</param>
internal sealed record SetLanguage(string? Language);

/// <summary>Light or dark.</summary>
/// <param name="Theme">light, dark, or null to follow the browser.</param>
internal sealed record SetTheme(string? Theme);

/// <summary>Somebody being admitted to the product.</summary>
/// <param name="Upn">Their sign-in name.</param>
/// <param name="DisplayName">Their name, so lists read as people rather than addresses.</param>
/// <param name="IsGlobalAdmin">Whether they may admit others.</param>
internal sealed record AdmitUser(string Upn, string? DisplayName, bool IsGlobalAdmin);

/// <summary>A change to somebody's standing in the product.</summary>
/// <param name="IsGlobalAdmin">What they should become.</param>
internal sealed record SetGlobalAdmin(bool IsGlobalAdmin);

/// <summary>Somebody being given a role on one engagement.</summary>
/// <param name="Upn">Their sign-in name.</param>
/// <param name="Role">Admin, Contributor or Viewer.</param>
internal sealed record GrantAccess(string Upn, string Role);

/// <summary>
/// A change to a connection.
/// </summary>
/// <remarks>
/// No mode. Each mode carries a different shape of settings and a different kind of
/// credential, and a connection that changed from an offline file to a service principal in
/// place would keep a secret reference that means nothing. Changing how an estate is reached
/// is a new connection, and the run history keeps pointing at the one it used.
/// </remarks>
/// <param name="Name">What to call it.</param>
/// <param name="EnvironmentRole">Production, test, development or unknown.</param>
/// <param name="Settings">The mode's settings, replacing what was there.</param>
/// <param name="Secret">A new credential, or absent to keep the one in the vault.</param>
/// <param name="SecretExpiresUtc">When it expires, where anybody knows.</param>
internal sealed record UpdateConnection(
    string Name,
    string? EnvironmentRole,
    Dictionary<string, string>? Settings,
    string? Secret,
    DateTime? SecretExpiresUtc);

/// <summary>
/// A request to publish part of a backlog.
/// </summary>
/// <param name="ConnectionId">Which Azure DevOps connection.</param>
/// <param name="Project">Which project in that organisation, chosen per publish.</param>
/// <param name="Keys">The deterministic keys chosen, or empty for all of them.</param>
/// <param name="DryRun">Report what would happen and call nothing.</param>
internal sealed record PublishRequest(
    Guid ConnectionId,
    string Project,
    IReadOnlyList<string>? Keys,
    bool DryRun);

/// <summary>
/// A change to an engagement.
/// </summary>
/// <remarks>
/// No status and no identifier. Status is moved by what happens to the engagement rather
/// than by editing a field, and the identifier is what every run hangs off.
/// </remarks>
/// <param name="Name">What it is called.</param>
/// <param name="ClientName">Who it is for, which is what a report says on its cover.</param>
/// <param name="IsRegulated">Drives a multiplier on every estimate.</param>
/// <param name="ReportLanguage">The language the report is produced in.</param>
/// <param name="BacklogLanguage">The language work items are written in.</param>
internal sealed record UpdateEngagement(
    string Name,
    string? ClientName,
    bool IsRegulated,
    string? ReportLanguage,
    string? BacklogLanguage);

/// <summary>A new engagement.</summary>
/// <param name="Name">What it is called.</param>
/// <param name="ClientName">Who it is for.</param>
/// <param name="IsRegulated">Drives a multiplier on every estimate.</param>
/// <param name="ReportLanguage">The language the report is produced in.</param>
/// <param name="BacklogLanguage">The language work items are written in, which is not always the same.</param>
internal sealed record CreateEngagement(
    string Name,
    string? ClientName,
    bool IsRegulated,
    string? ReportLanguage,
    string? BacklogLanguage);

/// <summary>A run to queue.</summary>
/// <param name="Mode">quickScan, assessment, publish or compare.</param>
/// <param name="SourceConnectionId">What to read.</param>
/// <param name="TargetConnectionId">Where to publish. Ignored on every mode but publish.</param>
/// <param name="BasedOnRunId">The assessment a publish or a comparison works from.</param>
internal sealed record StartRun(string Mode, Guid? SourceConnectionId, Guid? TargetConnectionId, Guid? BasedOnRunId);

/// <summary>An estimate a consultant is setting for this engagement.</summary>
/// <param name="Scope">rule, ruleAndComponentType or finding.</param>
/// <param name="RuleId">Which rule, on the first two scopes.</param>
/// <param name="ComponentTypeId">Which component type, on the second.</param>
/// <param name="FindingKey">The finding's stable key, on the third.</param>
/// <param name="LowHours">Lower bound.</param>
/// <param name="HighHours">Upper bound.</param>
/// <param name="StoryPoints">Points, or null.</param>
/// <param name="Rationale">Why. Required.</param>
internal sealed record SetOverride(
    string Scope,
    string? RuleId,
    string? ComponentTypeId,
    string? FindingKey,
    decimal LowHours,
    decimal HighHours,
    int? StoryPoints,
    string Rationale);
