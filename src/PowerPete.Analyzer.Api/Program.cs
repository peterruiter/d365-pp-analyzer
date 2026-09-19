using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.Globalization;
using PowerPete.Analyzer.Export;
using PowerPete.Analyzer.Export.Pdf;
using PowerPete.Analyzer.Api;
using PowerPete.Analyzer.Data;
using PowerPete.Analyzer.Domain;

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

string[] ExtractionModesList() => ["servicePrincipal", "delegated", "offlineZip", "azureDevOps"];

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
builder.Services.AddSingleton<ReportComposer>();

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

        // Derived from the mode rather than stored. Azure DevOps is the one place this
        // product writes, so it is the one connection that is a target, and a column nobody
        // sets is a column that eventually says a Dataverse connection writes somewhere.
        direction = string.Equals(connection.Mode, "azureDevOps", StringComparison.Ordinal) ? "target" : "source",
        connection.LastTestMessage,
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

// The steps, and where each one is done. Built once rather than on every request to a screen
// somebody opens at the start of every session.
string[] stepIds = ["connect", "discover", "review", "approve", "publish"];
string[] stepWorkspaces = ["Connections", "Runs", "Findings", "Backlog", "Backlog"];

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
app.MapGet("/api/extraction-modes", () =>
{
    var path = Path.Combine(AppContext.BaseDirectory, "build", "contracts", "extraction-sources.json");

    if (!File.Exists(path))
    {
        return Results.Problem(
            "The extraction sources contract is not in this image. The connection wizard is generated from it.",
            statusCode: StatusCodes.Status500InternalServerError);
    }

    using var document = JsonDocument.Parse(File.ReadAllText(path));

    return Results.Ok(document.RootElement.GetProperty("modes").EnumerateArray().Select(mode => new
    {
        id = mode.GetProperty("id").GetString(),
        name = mode.GetProperty("name").GetString(),
        status = mode.GetProperty("status").GetString(),
        summary = mode.GetProperty("summary").GetString(),
        settings = mode.GetProperty("auth").GetProperty("settings")
            .EnumerateArray().Select(setting => setting.GetString()).ToList(),
        needsSecret = mode.GetProperty("auth").GetProperty("secretRef").ValueKind != JsonValueKind.Null,
        reaches = mode.GetProperty("reaches").EnumerateObject()
            .ToDictionary(entry => entry.Name, entry => entry.Value.GetString())
    }));
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

    decimal? lowCodeShare = breakdown.TryGetProperty("LowCodeShare", out var share)
        && share.ValueKind == JsonValueKind.Number
        ? share.GetDecimal()
        : null;

    var componentTypes = breakdown.TryGetProperty("ByDomain", out var domains)
        && domains.ValueKind == JsonValueKind.Object
        ? domains.EnumerateObject().Count()
        : 0;

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
                detail = finding.ComponentName ?? finding.SolutionName ?? finding.RuleId,
                consequence = finding.RuleId
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

    var approved = false;
    if (scored is not null)
    {
        approved = await workspace.GetApprovedHashAsync(scored.Value, context.RequestAborted) is not null;
    }

    var published = runs.Any(run =>
        string.Equals(run.Mode, "publish", StringComparison.Ordinal)
        && string.Equals(run.Status, "succeeded", StringComparison.Ordinal));

    // Exactly one step is current: the first thing that is not done. Everything after it is
    // blocked, because offering a button for work that cannot start yet is how somebody ends
    // up publishing a backlog from an analysis that never ran.
    var done = new[] { connected, analysed, analysed, approved, published };

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

// ----------------------------------------------------------------------- auth --
// Anonymous on purpose: it is the first call the web application makes and it is what tells
// the shell whether to draw a sign-in button or a product. Nothing here is privileged. When
// nobody is signed in it says so and says nothing else.
app.MapGet("/api/auth/status", async (HttpContext context) =>
{
    var configured = !string.IsNullOrWhiteSpace(builder.Configuration["AzureAd:ClientId"]);
    var authenticated = context.User.Identity?.IsAuthenticated == true;

    if (!authenticated)
    {
        return Results.Ok(new
        {
            authConfigured = configured,
            authenticated = false,
            registered = false,
            adminContact = builder.Configuration["AdminContactEmail"] ?? string.Empty
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
        adminContact = builder.Configuration["AdminContactEmail"] ?? string.Empty,
        language = preferences.Language,
        theme = preferences.Theme
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

    return Results.Ok(await health.RunAsync(context.RequestAborted));
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
