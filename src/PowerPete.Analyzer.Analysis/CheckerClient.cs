namespace PowerPete.Analyzer.Analysis;

using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PowerPete.Analyzer.Domain;

/// <summary>
/// Calls the Power Apps checker service.
/// </summary>
/// <remarks>
/// This product does not reimplement static analysis of JavaScript, plugins and canvas apps.
/// Microsoft maintains those rules, they change without notice, and a second copy here would
/// be worse on the day it was written and stale a month later.
///
/// The flow is upload, analyse, poll, download. Analysis takes a minute at best and five or
/// more on a large solution, which is why the checker is a stage of its own with its own
/// checkpoint rather than a call inside the analyse stage.
/// </remarks>
public sealed class CheckerClient
{
    private readonly HttpClient client;
    private readonly string geography;

    /// <summary>Builds a client.</summary>
    /// <param name="client">Authenticated with a token for the checker service.</param>
    /// <param name="geography">The geographical endpoint, for example europe. Data residency, so it is not a default anybody should accept silently.</param>
    public CheckerClient(HttpClient client, string geography)
    {
        this.client = client;
        this.geography = geography;
    }

    private string Base => $"https://{geography}.api.advisor.powerapps.com/api";

    /// <summary>
    /// Resolves the ruleset identifier by name.
    /// </summary>
    /// <remarks>
    /// Looked up rather than hard coded, so rules Microsoft adds to the Solution Checker set
    /// arrive without a release here. The AppSource ruleset is available and is not the
    /// default: it is stricter in ways that produce noise on a client estate, which trains
    /// people to ignore the category.
    /// </remarks>
    /// <param name="name">Usually "Solution Checker".</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<Guid?> ResolveRulesetAsync(string name, CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync(new Uri($"{Base}/ruleset?api-version=2.0"), cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var rulesets = await response.Content
            .ReadFromJsonAsync<List<RulesetRecord>>(cancellationToken)
            .ConfigureAwait(false);

        var match = rulesets?.FirstOrDefault(ruleset =>
            string.Equals(ruleset.Name, name, StringComparison.OrdinalIgnoreCase));

        return match is null ? null : Guid.Parse(match.Id);
    }

    private sealed record RulesetRecord(string Id, string Name);

    /// <summary>What an analysis produced, or why it did not.</summary>
    /// <param name="Succeeded">Whether results came back.</param>
    /// <param name="Issues">What the checker reported.</param>
    /// <param name="FailureReason">Why not, on failure. Carried into the not assessed record for every checker backed rule.</param>
    public sealed record CheckerRun(bool Succeeded, IReadOnlyList<CheckerIssue> Issues, string? FailureReason);

    /// <summary>
    /// Uploads a solution, runs the checker and waits for the result.
    /// </summary>
    /// <remarks>
    /// Never throws on a checker failure. A checker outage is not a reason to lose the rest of
    /// an analysis, and it is absolutely a reason to say so loudly in the report: three rules
    /// go unassessed and a report that quietly omits them looks complete.
    /// </remarks>
    /// <param name="solution">The solution file.</param>
    /// <param name="fileName">Its name, which the service uses in the result paths.</param>
    /// <param name="rulesetId">Which ruleset.</param>
    /// <param name="timeout">How long to wait before giving up.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<CheckerRun> AnalyseAsync(
        Stream solution,
        string fileName,
        Guid rulesetId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solution);

        try
        {
            var sasUri = await UploadAsync(solution, fileName, cancellationToken).ConfigureAwait(false);
            if (sasUri is null) return new CheckerRun(false, [], "The upload was refused by the checker service.");

            var statusUri = await StartAsync(sasUri, rulesetId, cancellationToken).ConfigureAwait(false);
            if (statusUri is null) return new CheckerRun(false, [], "The checker service refused to start an analysis.");

            var resultUris = await PollAsync(statusUri, timeout, cancellationToken).ConfigureAwait(false);
            if (resultUris is null)
            {
                return new CheckerRun(false, [],
                    $"The checker did not finish within {timeout.TotalMinutes:0} minutes. Large solutions take longer; " +
                    "the run can be resumed from the checker stage rather than re-extracted.");
            }

            var issues = new List<CheckerIssue>();
            foreach (var uri in resultUris)
            {
                issues.AddRange(await DownloadAsync(uri, cancellationToken).ConfigureAwait(false));
            }

            return new CheckerRun(true, issues, null);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidDataException or TaskCanceledException)
        {
            return new CheckerRun(false, [], $"The checker call failed: {exception.Message}");
        }
    }

    private async Task<string?> UploadAsync(Stream solution, string fileName, CancellationToken cancellationToken)
    {
        using var content = new StreamContent(solution);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var response = await client.PostAsync(
            new Uri($"{Base}/upload?api-version=2.0&fileName={Uri.EscapeDataString(fileName)}"),
            content,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

        return document.RootElement.EnumerateArray().FirstOrDefault().TryGetProperty("sasUri", out var uri)
            ? uri.GetString()
            : null;
    }

    private async Task<Uri?> StartAsync(string sasUri, Guid rulesetId, CancellationToken cancellationToken)
    {
        var request = new
        {
            ruleSets = new[] { new { id = rulesetId } },
            fileUrls = new[] { sasUri }
        };

        using var response = await client.PostAsJsonAsync(
            new Uri($"{Base}/analyze?api-version=2.0"),
            request,
            cancellationToken).ConfigureAwait(false);

        // 202 with a Location header. Anything else means it did not start, and the caller
        // turns that into a not assessed record rather than an empty result set.
        return response.StatusCode == System.Net.HttpStatusCode.Accepted ? response.Headers.Location : null;
    }

    private async Task<IReadOnlyList<string>?> PollAsync(Uri statusUri, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            // Twenty seconds between checks, per Microsoft's own guidance of fifteen to sixty.
            // Polling harder does not make the analysis faster and does get the caller throttled.
            await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);

            using var response = await client.GetAsync(statusUri, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) continue;

            // Still running. The service keeps answering 202 until it is done.
            if (response.StatusCode == System.Net.HttpStatusCode.Accepted) continue;

            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

            if (!document.RootElement.TryGetProperty("resultFileUris", out var uris)) continue;

            return [.. uris.EnumerateArray().Select(uri => uri.GetString()).Where(uri => uri is not null).Select(uri => uri!)];
        }

        return null;
    }

    /// <summary>
    /// Downloads one result file and turns it into issues.
    /// </summary>
    /// <remarks>
    /// The result is a zip holding SARIF. Only the fields this product uses are read: the
    /// rule id, the category, the severity, the message and where it happened. The rest of
    /// SARIF is a standard, not a requirement.
    /// </remarks>
    private async Task<IReadOnlyList<CheckerIssue>> DownloadAsync(string uri, CancellationToken cancellationToken)
    {
        using var stream = await client.GetStreamAsync(new Uri(uri), cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        buffer.Position = 0;

        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);
        var issues = new List<CheckerIssue>();

        foreach (var entry in archive.Entries.Where(entry => entry.Name.EndsWith(".sarif", StringComparison.OrdinalIgnoreCase)))
        {
            using var entryStream = entry.Open();
            using var document = await JsonDocument.ParseAsync(entryStream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty("runs", out var runs)) continue;

            foreach (var run in runs.EnumerateArray())
            {
                var rules = ReadRuleMetadata(run);

                if (!run.TryGetProperty("results", out var results)) continue;

                foreach (var result in results.EnumerateArray())
                {
                    var ruleId = result.TryGetProperty("ruleId", out var id) ? id.GetString() ?? "unknown" : "unknown";
                    var metadata = rules.TryGetValue(ruleId, out var found)
                        ? found
                        : (Category: "unknown", Severity: "medium");

                    var location = result.TryGetProperty("locations", out var locations)
                        ? locations.EnumerateArray().FirstOrDefault()
                        : default;

                    issues.Add(new CheckerIssue(
                        ruleId,
                        metadata.Category,
                        result.TryGetProperty("level", out var level) ? level.GetString() ?? metadata.Severity : metadata.Severity,
                        result.TryGetProperty("message", out var message) && message.TryGetProperty("text", out var text)
                            ? text.GetString() ?? string.Empty
                            : string.Empty,
                        null,
                        FilePath(location),
                        Line(location)));
                }
            }
        }

        return issues;
    }

    private static Dictionary<string, (string Category, string Severity)> ReadRuleMetadata(JsonElement run)
    {
        var rules = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);

        if (!run.TryGetProperty("tool", out var tool)
            || !tool.TryGetProperty("driver", out var driver)
            || !driver.TryGetProperty("rules", out var declared))
        {
            return rules;
        }

        foreach (var rule in declared.EnumerateArray())
        {
            var id = rule.TryGetProperty("id", out var value) ? value.GetString() : null;
            if (id is null) continue;

            var properties = rule.TryGetProperty("properties", out var props) ? props : default;

            rules[id] = (
                properties.ValueKind == JsonValueKind.Object && properties.TryGetProperty("category", out var category)
                    ? category.GetString() ?? "unknown"
                    : "unknown",
                properties.ValueKind == JsonValueKind.Object && properties.TryGetProperty("severity", out var severity)
                    ? severity.GetString() ?? "medium"
                    : "medium");
        }

        return rules;
    }

    private static string? FilePath(JsonElement location) =>
        location.ValueKind == JsonValueKind.Object
        && location.TryGetProperty("physicalLocation", out var physical)
        && physical.TryGetProperty("artifactLocation", out var artifact)
        && artifact.TryGetProperty("uri", out var uri)
            ? uri.GetString()
            : null;

    private static int? Line(JsonElement location) =>
        location.ValueKind == JsonValueKind.Object
        && location.TryGetProperty("physicalLocation", out var physical)
        && physical.TryGetProperty("region", out var region)
        && region.TryGetProperty("startLine", out var line)
            ? line.GetInt32()
            : null;
}

/// <summary>
/// Maps checker vocabulary onto this product's.
/// </summary>
/// <remarks>
/// One report should not have two vocabularies. The mapping lives here so it can be read and
/// argued with, and the original category and rule identifier travel with every finding so
/// nothing is lost in the translation.
/// </remarks>
public static class CheckerMapping
{
    private static readonly Dictionary<string, string> Categories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Performance"] = "performance",
        ["Security"] = "security",
        ["Usage"] = "quality",
        ["Upgrade"] = "lifecycle",
        ["Maintainability"] = "quality",
        ["Design"] = "architecture",
        ["Accessibility"] = "quality",
        ["Reliability"] = "quality"
    };

    private static readonly Dictionary<string, Severity> Severities = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Critical"] = Domain.Severity.Critical,
        ["error"] = Domain.Severity.High,
        ["High"] = Domain.Severity.High,
        ["warning"] = Domain.Severity.Medium,
        ["Medium"] = Domain.Severity.Medium,
        ["Low"] = Domain.Severity.Low,
        ["note"] = Domain.Severity.Low,
        ["Informational"] = Domain.Severity.Info
    };

    /// <summary>This product's category for a checker category.</summary>
    /// <param name="checkerCategory">Whatever the checker said.</param>
    public static string Category(string checkerCategory) =>
        Categories.TryGetValue(checkerCategory, out var mapped) ? mapped : "quality";

    /// <summary>This product's severity for a checker severity.</summary>
    /// <param name="checkerSeverity">Whatever the checker said.</param>
    public static Severity Severity(string checkerSeverity) =>
        Severities.TryGetValue(checkerSeverity, out var mapped) ? mapped : Domain.Severity.Medium;
}
