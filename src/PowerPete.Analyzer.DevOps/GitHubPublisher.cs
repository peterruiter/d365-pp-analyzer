namespace PowerPete.Analyzer.DevOps;

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

/// <summary>
/// The same backlog, into a GitHub repository's issues.
/// </summary>
/// <remarks>
/// A third target rather than a rewrite of the first two. Azure DevOps, Jira and GitHub
/// disagree about almost everything at the wire and agree about the two things that decide
/// whether this is usable: an item has a parent, and an item this product created before has
/// to be found again rather than created twice. So the rules are the same and only the calls
/// differ.
///
/// The deterministic key is a label, the way it is a tag in Azure DevOps and a label in Jira.
/// GitHub creates a label that does not exist when an issue is created with it, so nothing
/// has to be set up in the repository first, and a label is filterable on the issues endpoint
/// without going through search. That last part matters: GitHub's search index lags behind
/// writes by seconds to minutes, so finding an existing issue through search would create a
/// second copy of the whole backlog for anybody who published twice in a row. The issues
/// endpoint with a label filter reads the repository itself and has no such lag.
///
/// GitHub has no work item types, so ours become labels too. It does have sub-issues, which
/// is a real hierarchy rather than a convention, and that is what the parent link uses. A
/// repository or a plan where sub-issues are unavailable leaves the item flat rather than
/// failing the publish, for the same reason Jira does: a board with a missing link is
/// recoverable and a publish that stopped on the ninth of nine items is not.
///
/// Nothing is ever closed or reopened here either. Somebody closed an issue for a reason and
/// this product does not know what it was.
/// </remarks>
public sealed class GitHubPublisher
{
    /// <summary>GitHub's own API. An Enterprise Server installation has its own.</summary>
    public const string PublicApi = "https://api.github.com";

    private readonly HttpClient client;
    private readonly string api;
    private readonly string owner;
    private readonly string repository;

    /// <summary>Builds a publisher.</summary>
    /// <param name="client">
    /// Authenticated against GitHub, with a user agent on it. The caller owns the credential.
    /// GitHub answers 403 to a request with no User-Agent header, which reads as a token
    /// problem and is not one, so <see cref="Authenticate"/> exists to get both right in one
    /// place.
    /// </param>
    /// <param name="owner">The organisation or user the repository belongs to.</param>
    /// <param name="repository">The repository the backlog lands in.</param>
    /// <param name="api">The API base. Defaults to github.com's.</param>
    public GitHubPublisher(HttpClient client, string owner, string repository, string? api = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(repository);

        this.client = client;
        this.owner = owner.Trim().Trim('/');
        this.repository = repository.Trim().Trim('/');
        this.api = (api is { Length: > 0 } given ? given : PublicApi).TrimEnd('/');
    }

    /// <summary>
    /// Puts the headers on a client that GitHub requires of every caller.
    /// </summary>
    /// <remarks>
    /// The user agent is not optional and its absence is not reported as itself: GitHub
    /// answers 403 Forbidden, which sends somebody to check the token they just pasted. The
    /// API version header pins the shape of what comes back, so a change GitHub makes to its
    /// default does not quietly change what this reads.
    /// </remarks>
    /// <param name="client">The client.</param>
    /// <param name="token">A personal access token with issues write on the repository.</param>
    public static void Authenticate(HttpClient client, string token)
    {
        ArgumentNullException.ThrowIfNull(client);

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PowerPlatformAnalyzer");
    }

    /// <summary>What a publish did.</summary>
    /// <param name="Key">The deterministic key.</param>
    /// <param name="Number">The issue number GitHub assigned, which is what people quote.</param>
    /// <param name="Url">Where it is.</param>
    /// <param name="Action">created, updated or skipped.</param>
    public sealed record Published(string Key, int Number, string Url, string Action);

    /// <summary>An issue that already exists, by the two identifiers GitHub uses for it.</summary>
    /// <remarks>
    /// Both, because they are not interchangeable and the sub-issue endpoint is the reason.
    /// The number is what appears in the URL and what a person quotes; the id is global and
    /// is what a parent has to be given to adopt a child. Sending a number where an id
    /// belongs adopts an unrelated issue in another repository, which is the worst failure
    /// available here and is silent.
    /// </remarks>
    /// <param name="Number">The per repository number.</param>
    /// <param name="Id">The global identifier.</param>
    private sealed record Issue(int Number, long Id);

    /// <summary>
    /// Publishes a backlog.
    /// </summary>
    /// <param name="items">The backlog.</param>
    /// <param name="dryRun">When true, returns what would be created and calls nothing.</param>
    /// <param name="confirmedCount">The count the person confirmed, when there are more than two hundred items.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<IReadOnlyList<Published>> PublishAsync(
        IReadOnlyList<BacklogItem> items,
        bool dryRun,
        int? confirmedCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count > 200 && confirmedCount != items.Count)
        {
            throw new InvalidOperationException(
                $"This would create {items.Count} issues in {owner}/{repository}. Confirm the count before it runs.");
        }

        if (dryRun)
        {
            return [.. items.Select(item => new Published(item.Key, 0, string.Empty, "skipped"))];
        }

        var published = new List<Published>();
        var created = new Dictionary<string, Issue>(StringComparer.Ordinal);

        // Parents first, so a child can be adopted as soon as it exists.
        foreach (var item in items.OrderBy(item => item.ParentKey is null ? 0 : item.Type == "feature" ? 1 : 2))
        {
            var existing = await FindAsync(item.Key, cancellationToken).ConfigureAwait(false);

            var (result, issue) = existing is null
                ? await CreateAsync(item, cancellationToken).ConfigureAwait(false)
                : await UpdateAsync(item, existing, cancellationToken).ConfigureAwait(false);

            created[item.Key] = issue;
            published.Add(result);

            if (item.ParentKey is not null && created.TryGetValue(item.ParentKey, out var parent))
            {
                await AdoptAsync(parent.Number, issue.Id, cancellationToken).ConfigureAwait(false);
            }
        }

        return published;
    }

    /// <summary>The issue carrying this key, or null where nothing does.</summary>
    /// <remarks>
    /// The issues endpoint filtered by label, not the search endpoint. Search is indexed
    /// asynchronously and lags a write by seconds to minutes, so a second publish inside
    /// that window would find nothing and duplicate the entire backlog.
    ///
    /// state=all, because an issue somebody closed still exists and publishing a second copy
    /// of it beside the closed one is the failure this whole mechanism is here to prevent.
    /// </remarks>
    private async Task<Issue?> FindAsync(string key, CancellationToken cancellationToken)
    {
        var url = $"{api}/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}"
            + $"/issues?labels={Uri.EscapeDataString(key)}&state=all&per_page=1";

        using var response = await client.GetAsync(new Uri(url), cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

        if (document.RootElement.ValueKind != JsonValueKind.Array) return null;

        var first = document.RootElement.EnumerateArray().FirstOrDefault();

        if (first.ValueKind != JsonValueKind.Object) return null;

        // A pull request is an issue on this endpoint and carries the same labels. One of
        // ours cannot be a pull request, so anything with that property is somebody else's.
        if (first.TryGetProperty("pull_request", out _)) return null;

        return Read(first);
    }

    private async Task<(Published Result, Issue Issue)> CreateAsync(BacklogItem item, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            new Uri($"{api}/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}/issues"),
            new { title = Title(item.Title), body = Body(item), labels = Labels(item) },
            cancellationToken).ConfigureAwait(false);

        await ThrowIfRefusedAsync(response, item, "create", cancellationToken).ConfigureAwait(false);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

        var issue = Read(document.RootElement);

        return (new Published(item.Key, issue.Number, Url(document.RootElement, issue.Number), "created"), issue);
    }

    /// <summary>
    /// Updates an issue that already exists.
    /// </summary>
    /// <remarks>
    /// The fields this product owns, and nothing else. Not the state, not the assignee and
    /// not the milestone: a person put those there and a re-publish is not a reason to undo
    /// it. The labels are sent whole because GitHub replaces the set rather than merging it,
    /// so anything a team added by hand would be removed. Theirs are read first and kept.
    /// </remarks>
    private async Task<(Published Result, Issue Issue)> UpdateAsync(
        BacklogItem item, Issue existing, CancellationToken cancellationToken)
    {
        var theirs = await LabelsOnAsync(existing.Number, cancellationToken).ConfigureAwait(false);

        var url = $"{api}/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}"
            + $"/issues/{existing.Number.ToString(CultureInfo.InvariantCulture)}";

        using var request = new HttpRequestMessage(HttpMethod.Patch, new Uri(url))
        {
            Content = JsonContent.Create(new
            {
                title = Title(item.Title),
                body = Body(item),
                labels = Labels(item).Union(theirs, StringComparer.OrdinalIgnoreCase).ToArray()
            })
        };

        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

        await ThrowIfRefusedAsync(response, item, "update", cancellationToken).ConfigureAwait(false);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

        return (new Published(item.Key, existing.Number, Url(document.RootElement, existing.Number), "updated"), existing);
    }

    /// <summary>The labels already on an issue, so an update does not strip a team's own.</summary>
    private async Task<IReadOnlyList<string>> LabelsOnAsync(int number, CancellationToken cancellationToken)
    {
        var url = $"{api}/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}"
            + $"/issues/{number.ToString(CultureInfo.InvariantCulture)}/labels?per_page=100";

        using var response = await client.GetAsync(new Uri(url), cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return [];

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

        if (document.RootElement.ValueKind != JsonValueKind.Array) return [];

        return [.. document.RootElement.EnumerateArray()
            .Select(label => label.TryGetProperty("name", out var name) ? name.GetString() : null)
            .Where(name => name is { Length: > 0 })
            .Select(name => name!)];
    }

    /// <summary>
    /// Makes one issue a sub-issue of another, where the repository will take one.
    /// </summary>
    /// <remarks>
    /// Never fatal. Sub-issues are not on every plan or every Enterprise Server version, and
    /// re-publishing an item that is already somebody's child answers 422. Both leave the
    /// item where it is rather than stopping a publish half way through, which is the same
    /// call Jira's publisher makes about a hierarchy level a project does not have.
    /// </remarks>
    private async Task AdoptAsync(int parentNumber, long childId, CancellationToken cancellationToken)
    {
        var url = $"{api}/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}"
            + $"/issues/{parentNumber.ToString(CultureInfo.InvariantCulture)}/sub_issues";

        try
        {
            using var response = await client.PostAsJsonAsync(
                new Uri(url), new { sub_issue_id = childId }, cancellationToken).ConfigureAwait(false);

            // Read and dropped. There is nothing to do about a refusal here that is better
            // than leaving the issue flat, and the issue itself is already published.
            _ = response.StatusCode;
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException)
        {
            // As above. The backlog is in the repository either way.
        }
    }

    /// <summary>The labels this product puts on an issue.</summary>
    /// <remarks>
    /// The deterministic key, so the next publish finds this issue rather than making a
    /// second one. The type, because GitHub has no work item types and a backlog where an
    /// epic is indistinguishable from a task is not a backlog. Then the builder's own tags.
    /// </remarks>
    private static string[] Labels(BacklogItem item) =>
        [.. new[] { item.Key, item.Type }
            .Concat(item.Tags)
            .Select(Label)
            .Where(label => label.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>A GitHub label, which may hold spaces and may not exceed fifty characters.</summary>
    private static string Label(string value)
    {
        var flat = DescriptionHtml.Flatten(value);

        return flat.Length <= 50 ? flat : flat[..50].TrimEnd();
    }

    /// <summary>A GitHub issue title, which is one line and has a length limit.</summary>
    private static string Title(string title)
    {
        var flat = DescriptionHtml.Flatten(title);

        return flat.Length <= 250 ? flat : flat[..247] + "...";
    }

    /// <summary>
    /// The issue body, as markdown.
    /// </summary>
    /// <remarks>
    /// Markdown rather than the builder's HTML. GitHub does render a subset of HTML, and
    /// relying on that would mean the body is fine until the builder emits a tag outside the
    /// subset, at which point it renders as source in a client's repository.
    ///
    /// The estimate is written into the body because a GitHub issue has nowhere else to put
    /// it. Hours are what this product exists to produce and an issue that arrives without
    /// them has lost the part somebody was paid for.
    /// </remarks>
    private static string Body(BacklogItem item)
    {
        var text = new StringBuilder();

        foreach (var (tag, line) in DescriptionHtml.Blocks(item.DescriptionHtml))
        {
            if (line.Length == 0) continue;

            text.Append(tag switch
            {
                "h1" or "h2" or "h3" or "h4" or "h5" or "h6" => $"### {line}\n\n",
                "li" => $"- {line}\n",
                _ => $"{line}\n\n"
            });
        }

        foreach (var (heading, body) in new[]
        {
            ("Acceptance criteria", item.AcceptanceCriteria),
            ("How to prove it", item.TestRequirement)
        })
        {
            if (string.IsNullOrWhiteSpace(body)) continue;

            text.Append(CultureInfo.InvariantCulture, $"\n### {heading}\n\n");

            foreach (var (tag, line) in DescriptionHtml.Blocks(body))
            {
                if (line.Length == 0) continue;

                text.Append(tag == "li" ? $"- {line}\n" : $"{line}\n\n");
            }
        }

        if (Estimate(item) is { Length: > 0 } estimate)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n### Estimate\n\n{estimate}\n");
        }

        return text.Length == 0 ? item.Title : text.ToString().TrimEnd();
    }

    /// <summary>What this was priced at, in one line, or nothing where it was not priced.</summary>
    private static string Estimate(BacklogItem item)
    {
        var parts = new List<string>();

        if (item.LowHours is { } low && item.HighHours is { } high)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{low:0.#}–{high:0.#} hours"));
        }

        if (item.StoryPoints is { } points and > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{points} points"));
        }

        return string.Join(" · ", parts);
    }

    private static Issue Read(JsonElement issue) =>
        new(issue.TryGetProperty("number", out var number) ? number.GetInt32() : 0,
            issue.TryGetProperty("id", out var id) ? id.GetInt64() : 0);

    private string Url(JsonElement issue, int number) =>
        issue.TryGetProperty("html_url", out var link) && link.GetString() is { Length: > 0 } href
            ? href
            : $"https://github.com/{owner}/{repository}/issues/{number.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Turns a refusal into a sentence naming what was refused and why.
    /// </summary>
    /// <remarks>
    /// GitHub puts the reason in the body and the status alone does not carry it. The three
    /// picked out here are the ones a consultant can actually do something about: a token
    /// without issues write, a repository with issues switched off, and one that is archived.
    /// All three answer 403 or 410 and none of them is a problem with the backlog.
    /// </remarks>
    private async Task ThrowIfRefusedAsync(
        HttpResponseMessage response, BacklogItem item, string verb, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var reason = response.StatusCode switch
        {
            HttpStatusCode.Gone =>
                $"Issues are turned off for {owner}/{repository}. Somebody has to enable them in the "
                + "repository's settings before a backlog can land there.",
            HttpStatusCode.Forbidden =>
                $"GitHub refused the write to {owner}/{repository}. A token without Issues: write, and an "
                + $"archived repository, both arrive here. ({Readable(body)})",
            HttpStatusCode.NotFound =>
                $"No repository answered at {owner}/{repository}. A private repository the token cannot see "
                + "returns the same 404 as one that does not exist.",
            _ => Readable(body)
        };

        throw new InvalidOperationException($"GitHub refused to {verb} \"{item.Title}\": {reason}");
    }

    /// <summary>GitHub's error body, or the raw body where it is not one.</summary>
    private static string Readable(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            var message = document.RootElement.TryGetProperty("message", out var text)
                ? text.GetString() ?? string.Empty
                : string.Empty;

            var detail = new List<string>();

            if (document.RootElement.TryGetProperty("errors", out var errors)
                && errors.ValueKind == JsonValueKind.Array)
            {
                detail.AddRange(errors.EnumerateArray()
                    .Select(error => error.TryGetProperty("message", out var one)
                        ? one.GetString()
                        : error.TryGetProperty("field", out var field) ? field.GetString() : null)
                    .Where(one => one is { Length: > 0 })
                    .Select(one => one!));
            }

            var joined = string.Join("; ", new[] { message }.Concat(detail).Where(part => part.Length > 0));

            return joined.Length > 0 ? joined : body;
        }
        catch (JsonException)
        {
            return body;
        }
    }
}
