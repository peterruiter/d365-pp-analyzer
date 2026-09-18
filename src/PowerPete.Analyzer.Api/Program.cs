using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using PowerPete.Analyzer.Api;
using PowerPete.Analyzer.Data;
using PowerPete.Analyzer.Domain;

var builder = WebApplication.CreateBuilder(args);

// ------------------------------------------------------------------ settings --
var connectionString = builder.Configuration.GetConnectionString("Analyzer")
    ?? Environment.GetEnvironmentVariable("ANALYZER_SQL_CONNECTION")
    ?? throw new InvalidOperationException(
        "No connection string. The API cannot start without somewhere to read engagements from, and starting " +
        "anyway would mean a sign-in page in front of nothing.");

var keyVaultUri = builder.Configuration["KeyVaultUri"] ?? Environment.GetEnvironmentVariable("ANALYZER_KEYVAULT_URI");
var initialAdmin = builder.Configuration["InitialGlobalAdmin"] ?? Environment.GetEnvironmentVariable("ANALYZER_INITIAL_ADMIN");

builder.Services.AddSingleton(new WorkspaceStore(connectionString));
builder.Services.AddSingleton(new AnalysisStore(connectionString));
builder.Services.AddSingleton(new AccessStore(connectionString));
builder.Services.AddSingleton(SecretStore.For(keyVaultUri));
builder.Services.AddSingleton<SystemHealth>();

// ------------------------------------------------------------ authentication --
// Entra sign-in, cookie session. The product knows who somebody is from their token and what
// they may do from a row in the database, and the two are deliberately separate: anybody in
// the tenant can sign in, only an admitted person sees an engagement.
builder.Services
    .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

builder.Services.AddAuthorization();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseDefaultFiles();
app.UseStaticFiles();

// ------------------------------------------------------------------- helpers --
static string UserId(ClaimsPrincipal user) =>
    AccessStore.Normalise(user.FindFirstValue("preferred_username") ?? user.FindFirstValue(ClaimTypes.Upn) ?? string.Empty);

static string DisplayName(ClaimsPrincipal user) =>
    user.FindFirstValue("name") ?? user.FindFirstValue(ClaimTypes.Name) ?? UserId(user);

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

app.MapGet("/account/signin", () => Results.Challenge(new() { RedirectUri = "/" }));
app.MapGet("/account/signout", () => Results.SignOut(new() { RedirectUri = "/" },
    [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]));

// --------------------------------------------------------------- engagements --
app.MapGet("/api/engagements", async (HttpContext context, WorkspaceStore store) =>
{
    var access = await AccessFor(context);
    return Results.Ok(await store.ListEngagementsAsync(access, context.RequestAborted));
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
        expiringSoon = connection.SecretExpiresUtc is { } expiry && expiry < DateTime.UtcNow.AddDays(30),
        reach = connection.ReachJson is null ? null : (JsonElement?)JsonDocument.Parse(connection.ReachJson).RootElement
    }));
}).RequireAuthorization();

// ----------------------------------------------------------------------- runs --
app.MapPost("/api/engagements/{engagementId:guid}/runs",
    async (HttpContext context, WorkspaceStore store, Guid engagementId, StartRun request) =>
{
    if (await Denied(context, engagementId, EngagementRoles.Contributor) is { } denied) return denied;

    // A publish is the only mode that writes anywhere, and it may only work from an assessment
    // somebody already approved. Both constraints exist in the database as well; this one is
    // here so the message names the reason rather than a constraint.
    if (request.Mode == "publish" && request.BasedOnRunId is null)
    {
        return Results.BadRequest(new
        {
            error = "A publish works from an existing assessment rather than re-analysing. " +
                    "Re-analysing would mean the thing published is not the thing that was approved."
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

    var stages = await store.GetCompletedStagesAsync(runId, context.RequestAborted);
    return Results.Ok(new { run, completedStages = stages.Keys });
}).RequireAuthorization();

// -------------------------------------------------------------------- approval --
app.MapPost("/api/runs/{runId:guid}/approve",
    async (HttpContext context, WorkspaceStore store, Guid runId, Approve request) =>
{
    var run = await store.GetRunAsync(runId, context.RequestAborted);
    if (run is null) return Results.NotFound();

    // Admin only, and only ever a person. Approving is the gate between a backlog and a
    // client's DevOps project, and a role that can start a run is not the same as one that can
    // write into somebody else's board.
    if (await Denied(context, run.EngagementId, EngagementRoles.Admin) is { } denied) return denied;

    await store.ApproveAsync(runId, request.BacklogHash, request.ItemCount,
        UserId(context.User), DisplayName(context.User), context.RequestAborted);

    return Results.Ok(new
    {
        approved = true,
        note = "Recorded against your name and this exact backlog. A backlog that changes afterwards no longer " +
               "matches and the publish refuses."
    });
}).RequireAuthorization();

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
        notAssessed = notAssessed.Select(entry => new { entry.RuleId, entry.Reason, entry.MissingEvidence }),
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
                ruleName = rule?.Name ?? finding.RuleId,
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
                why = rule?.Why,
                recommendation = rule?.Recommendation,
                falsePositive = rule?.FalsePositive,
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

// --------------------------------------------------------------------- health --
app.MapHealthChecks("/healthz");

app.MapGet("/api/health/detail", async (HttpContext context) =>
{
    var access = await AccessFor(context);
    if (!access.IsGlobalAdmin) return Results.Forbid();

    var health = context.RequestServices.GetRequiredService<SystemHealth>();

    return Results.Ok(await health.RunAsync(context.RequestAborted));
}).RequireAuthorization();

// The single page application owns every route the API does not.
app.MapFallbackToFile("index.html");

app.Run();

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

/// <summary>An approval, bound to the backlog the person was looking at.</summary>
/// <param name="BacklogHash">What they saw.</param>
/// <param name="ItemCount">How many items, printed back at them before they confirmed.</param>
internal sealed record Approve(string BacklogHash, int ItemCount);

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
