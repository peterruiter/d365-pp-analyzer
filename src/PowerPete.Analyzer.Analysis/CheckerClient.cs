namespace PowerPete.Analyzer.Analysis;

using System.Globalization;
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
    /// <summary>
    /// For the result files, which authenticate themselves.
    /// </summary>
    /// <remarks>
    /// Static and shared: it holds no credential, so there is nothing per caller about it.
    /// </remarks>
    private static readonly HttpClient Anonymous = new();

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
    /// <returns>The ruleset, or why it could not be resolved.</returns>
    public async Task<RulesetLookup> ResolveRulesetAsync(string name, CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync(new Uri($"{Base}/ruleset?api-version=2.0"), cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // The status, and what the service said about it.
            //
            // This returned null and the caller reported "the checker service did not
            // return its ruleset list", five times, once per solution. That sentence is
            // true of an outage, of a wrong geography, of an expired credential and of a
            // token for the wrong resource, which is what it actually was, and it
            // distinguishes none of them. A 401 here is a different afternoon from a 503.
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return new RulesetLookup(null, string.Create(CultureInfo.InvariantCulture,
                $"The checker at {geography} answered {(int)response.StatusCode} {response.ReasonPhrase} when asked "
                + $"for its rulesets.{Detail(response, body)}"));
        }

        var rulesets = await response.Content
            .ReadFromJsonAsync<List<RulesetRecord>>(cancellationToken)
            .ConfigureAwait(false);

        var match = rulesets?.FirstOrDefault(ruleset =>
            string.Equals(ruleset.Name, name, StringComparison.OrdinalIgnoreCase));

        if (match is not null) return new RulesetLookup(Guid.Parse(match.Id), null);

        // Answered, and without the one we need. A different failure again: the service is
        // reachable and the credential works.
        var offered = rulesets is null || rulesets.Count == 0
            ? "nothing"
            : string.Join(", ", rulesets.Select(ruleset => ruleset.Name));

        return new RulesetLookup(null,
            $"The checker at {geography} does not offer a ruleset called '{name}'. It offers {offered}.");
    }

    /// <summary>What the service said, where it said anything useful.</summary>
    /// <param name="response">The response.</param>
    /// <param name="body">Its body.</param>
    private static string Detail(HttpResponseMessage response, string body)
    {
        // Unauthorized is worth a sentence of its own, because the cause is almost always
        // the same one and it is not obvious: the checker is its own resource and a token
        // for the environment is not a token for it.
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return " A token for the environment is not a token for the checker: this connection needs "
                 + "PowerApps-Advisor consent. Sign in to the connection again.";
        }

        var trimmed = body.Trim();

        return trimmed.Length == 0 ? string.Empty : $" It said: {(trimmed.Length > 300 ? trimmed[..300] : trimmed)}";
    }

    /// <summary>A ruleset, or why there is not one.</summary>
    /// <param name="Id">The ruleset, when it resolved.</param>
    /// <param name="FailureReason">Why not, when it did not. Carried into the report per rule.</param>
    public sealed record RulesetLookup(Guid? Id, string? FailureReason);

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
            var upload = await UploadAsync(solution, fileName, cancellationToken).ConfigureAwait(false);
            if (upload.SasUri is null) return new CheckerRun(false, [], upload.FailureReason);

            var start = await StartAsync(upload.SasUri, rulesetId, cancellationToken).ConfigureAwait(false);
            if (start.StatusUri is null) return new CheckerRun(false, [], start.FailureReason);

            var finished = await PollAsync(start.StatusUri, timeout, cancellationToken).ConfigureAwait(false);
            if (finished.ResultUris is null) return new CheckerRun(false, [], finished.FailureReason);

            var issues = new List<CheckerIssue>();
            foreach (var uri in finished.ResultUris)
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

    /// <summary>Where an uploaded file ended up, or why it did not.</summary>
    /// <param name="SasUri">The blob the service can read it from.</param>
    /// <param name="FailureReason">Why not.</param>
    private sealed record Upload(string? SasUri, string? FailureReason);

    /// <summary>
    /// Puts the solution where the checker can read it.
    /// </summary>
    /// <remarks>
    /// Three things here were wrong against Microsoft's published contract, and each on its
    /// own is a refusal with no body to explain it.
    ///
    /// The version: everything but rulesets and rules is api-version 1.0, and this asked for
    /// 2.0. The encoding: the service takes multipart form data with a Content-Disposition
    /// naming the file, and this sent a bare octet stream with the name in the query string.
    /// And the response: it is a plain array of URI strings, and this read it as an array of
    /// objects with a sasUri property, so a successful upload would have been read as a
    /// failure anyway.
    /// </remarks>
    /// <param name="solution">The file.</param>
    /// <param name="fileName">What to call it, which the service uses in the result paths.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    private async Task<Upload> UploadAsync(Stream solution, string fileName, CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        using var file = new StreamContent(solution);

        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, fileName, fileName);

        using var response = await client.PostAsync(
            new Uri($"{Base}/upload?api-version=1.0"),
            form,
            cancellationToken).ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // 413 has one cause and one answer, and the answer is not "try again".
            var why = (int)response.StatusCode == 413
                ? " The upload API takes thirty megabytes. A solution larger than that has to be staged in "
                  + "storage this product owns and handed over as a URI, which it does not do yet."
                : Detail(response, body);

            return new Upload(null, string.Create(CultureInfo.InvariantCulture,
                $"The checker at {geography} refused the upload of {fileName}: "
                + $"{(int)response.StatusCode} {response.ReasonPhrase}.{why}"));
        }

        using var document = JsonDocument.Parse(body);

        // An array of strings, as published. Not an array of objects.
        var uri = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray().FirstOrDefault().GetString()
            : null;

        return uri is { Length: > 0 }
            ? new Upload(uri, null)
            : new Upload(null, $"The checker at {geography} accepted {fileName} and returned no location for it.");
    }

    /// <summary>Where to watch an analysis, or why there is nothing to watch.</summary>
    /// <param name="StatusUri">What to poll.</param>
    /// <param name="FailureReason">Why not.</param>
    private sealed record Started(Uri? StatusUri, string? FailureReason);

    /// <summary>
    /// Asks for the analysis.
    /// </summary>
    /// <remarks>
    /// The property is sasUriList. This sent fileUrls, which the service does not read, so
    /// the request was an analysis of nothing and came back as a bad request with no body.
    /// Same api-version mistake as the upload.
    /// </remarks>
    /// <param name="sasUri">Where the file is.</param>
    /// <param name="rulesetId">Which ruleset.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    private async Task<Started> StartAsync(string sasUri, Guid rulesetId, CancellationToken cancellationToken)
    {
        var request = new
        {
            ruleSets = new[] { new { id = rulesetId } },
            sasUriList = new[] { sasUri }
        };

        using var response = await client.PostAsJsonAsync(
            new Uri($"{Base}/analyze?api-version=1.0"),
            request,
            cancellationToken).ConfigureAwait(false);

        // 202 with a Location header. Anything else means it did not start, and the caller
        // turns that into a not assessed record rather than an empty result set.
        if (response.StatusCode == System.Net.HttpStatusCode.Accepted && response.Headers.Location is { } location)
        {
            return new Started(location, null);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return new Started(null, string.Create(CultureInfo.InvariantCulture,
            $"The checker at {geography} would not start an analysis: "
            + $"{(int)response.StatusCode} {response.ReasonPhrase}.{Detail(response, body)}"));
    }

    /// <summary>What an analysis produced, or why nothing.</summary>
    /// <param name="ResultUris">Where to download the reports.</param>
    /// <param name="FailureReason">Why not.</param>
    private sealed record Finished(IReadOnlyList<string>? ResultUris, string? FailureReason);

    /// <summary>
    /// Waits for the analysis.
    /// </summary>
    /// <remarks>
    /// Every unsuccessful answer used to be treated as "not yet" and polled again until the
    /// timeout, so a 403 or a 404 cost twenty minutes and then reported a timeout. They are
    /// terminal and say so now.
    /// </remarks>
    /// <param name="statusUri">What to poll.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    private async Task<Finished> PollAsync(Uri statusUri, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            // Twenty seconds between checks, per Microsoft's own guidance of fifteen to sixty.
            // Polling harder does not make the analysis faster and does get the caller throttled.
            await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);

            using var response = await client.GetAsync(statusUri, cancellationToken).ConfigureAwait(false);

            // Still running. The service keeps answering 202 until it is done.
            if (response.StatusCode == System.Net.HttpStatusCode.Accepted) continue;

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return new Finished(null, string.Create(CultureInfo.InvariantCulture,
                    $"The checker at {geography} stopped answering about this analysis: "
                    + $"{(int)response.StatusCode} {response.ReasonPhrase}.{Detail(response, body)}"));
            }

            using var document = JsonDocument.Parse(body);

            if (document.RootElement.TryGetProperty("resultFileUris", out var uris)
                && uris.ValueKind == JsonValueKind.Array)
            {
                return new Finished(
                    [.. uris.EnumerateArray().Select(uri => uri.GetString()).Where(uri => uri is not null).Select(uri => uri!)],
                    null);
            }

            // Finished, with no files. The service says why in its status, and Failed is a
            // different thing from an analysis that found nothing.
            var status = document.RootElement.TryGetProperty("status", out var state) ? state.GetString() : null;

            if (string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                return new Finished(null, $"The checker at {geography} failed while analysing this solution.");
            }

            if (status is not null && !string.Equals(status, "InProgress", StringComparison.OrdinalIgnoreCase))
            {
                return new Finished(null,
                    $"The checker at {geography} finished with status '{status}' and produced no report.");
            }
        }

        return new Finished(null,
            $"The checker did not finish within {timeout.TotalMinutes:0} minutes. Large solutions take longer; "
            + "the run can be resumed from the checker stage rather than re-extracted.");
    }

    /// <summary>
    /// Downloads one result file and turns it into issues.
    /// </summary>
    /// <remarks>
    /// The result is a zip holding SARIF. Only the fields this product uses are read: the
    /// rule id, the category, the severity, the message and where it happened. The rest of
    /// SARIF is a standard, not a requirement.
    /// </remarks>
    private static async Task<IReadOnlyList<CheckerIssue>> DownloadAsync(string uri, CancellationToken cancellationToken)
    {
        // Downloaded by a client carrying nothing, because the URI carries everything.
        //
        // The result is an Azure blob with a shared access signature in its query string.
        // Azure Storage refuses a request that presents a SAS *and* an Authorization
        // header, with 403 and no explanation, and this was downloaded with the checker's
        // own authenticated client. So the analysis ran, the report was written, and
        // fetching it failed on the credential that had been necessary for every call up
        // to that point.
        using var response = await Anonymous
            .GetAsync(new Uri(uri), HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(string.Create(
                CultureInfo.InvariantCulture,
                $"The checker's report could not be downloaded: {(int)response.StatusCode} {response.ReasonPhrase}. The link the service returns is time limited, so a run resumed long afterwards has to be checked again rather than resumed."));
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
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
