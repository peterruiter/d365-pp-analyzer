namespace PowerPete.Analyzer.DevOps;

using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// The same backlog, into a Jira board.
/// </summary>
/// <remarks>
/// A second target rather than a rewrite of the first. Azure DevOps and Jira disagree about
/// almost everything at the wire and agree about the only two things that matter here: an
/// item has a parent, and an item this product created before has to be found again rather
/// than created twice. So the rules are the same and only the calls differ.
///
/// The deterministic key is a label, the way it is a tag in Azure DevOps. A label is the one
/// field present on every Jira project regardless of how somebody configured it, needs no
/// custom field to be created first, and can be searched with JQL. Its cost is that Jira
/// labels cannot contain spaces, which the keys do not.
///
/// Nothing is ever closed or reopened here either. Somebody closed an issue for a reason and
/// this product does not know what it was.
/// </remarks>
public sealed partial class JiraPublisher
{
    private readonly HttpClient client;
    private readonly string site;
    private readonly string projectKey;
    private readonly IReadOnlyDictionary<string, string> issueTypes;

    /// <summary>Builds a publisher.</summary>
    /// <param name="client">Authenticated against the Jira site. The caller owns the credential.</param>
    /// <param name="site">The site, for example https://contoso.atlassian.net.</param>
    /// <param name="projectKey">The project key the backlog lands in.</param>
    /// <param name="issueTypes">
    /// The issue types this project offers, keyed by lower case name. Read from the project
    /// rather than assumed: a team managed project and a company managed one do not offer
    /// the same ones, and neither reliably has Epic.
    /// </param>
    public JiraPublisher(
        HttpClient client,
        string site,
        string projectKey,
        IReadOnlyDictionary<string, string> issueTypes)
    {
        this.client = client;
        this.site = site.TrimEnd('/');
        this.projectKey = projectKey;
        this.issueTypes = issueTypes;
    }

    /// <summary>What a publish did.</summary>
    /// <param name="Key">The deterministic key.</param>
    /// <param name="IssueKey">The key Jira assigned, for example ABC-123.</param>
    /// <param name="Url">Where it is.</param>
    /// <param name="Action">created, updated or skipped.</param>
    public sealed record Published(string Key, string IssueKey, string Url, string Action);

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
                $"This would create {items.Count} issues in {projectKey}. Confirm the count before it runs.");
        }

        if (dryRun)
        {
            return [.. items.Select(item => new Published(item.Key, string.Empty, string.Empty, "skipped"))];
        }

        var published = new List<Published>();
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);

        // Parents first, so a child can name its parent as it is created.
        foreach (var item in items.OrderBy(item => item.ParentKey is null ? 0 : item.Type == "feature" ? 1 : 2))
        {
            var existing = await FindAsync(item.Key, cancellationToken).ConfigureAwait(false);

            var result = existing is null
                ? await CreateAsync(item, keys, cancellationToken).ConfigureAwait(false)
                : await UpdateAsync(item, existing, cancellationToken).ConfigureAwait(false);

            keys[item.Key] = result.IssueKey;
            published.Add(result);
        }

        return published;
    }

    /// <summary>The issue carrying this key, or null where nothing does.</summary>
    /// <remarks>
    /// Searched by label and scoped to the project. The same backlog published into two
    /// projects is two sets of issues, so a key found in another project is not this one.
    /// </remarks>
    private async Task<string?> FindAsync(string key, CancellationToken cancellationToken)
    {
        var jql = $"project = \"{projectKey}\" AND labels = \"{key}\" ORDER BY created ASC";
        var url = $"{site}/rest/api/3/search/jql?jql={Uri.EscapeDataString(jql)}&maxResults=1&fields=key";

        using var response = await client.GetAsync(new Uri(url), cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

        if (!document.RootElement.TryGetProperty("issues", out var issues)) return null;

        var first = issues.EnumerateArray().FirstOrDefault();

        return first.ValueKind == JsonValueKind.Object ? first.GetProperty("key").GetString() : null;
    }

    private async Task<Published> CreateAsync(
        BacklogItem item,
        Dictionary<string, string> keys,
        CancellationToken cancellationToken)
    {
        var fields = Fields(item);

        // The parent, where Jira will take one. Epic to story works on both project styles;
        // a level Jira does not have is left flat rather than refused, because an item with
        // no parent in the board is recoverable and a failed publish half way through is
        // considerably less so.
        if (item.ParentKey is not null && keys.TryGetValue(item.ParentKey, out var parentKey))
        {
            fields["parent"] = new { key = parentKey };
        }

        using var response = await client.PostAsJsonAsync(
            new Uri($"{site}/rest/api/3/issue"),
            new { fields },
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // Jira answers a rejected field with a body naming it, and that body is the
            // only thing that says which field. Swallowing it leaves somebody guessing at
            // a 400.
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            throw new InvalidOperationException(
                $"Jira refused to create \"{item.Title}\": {Readable(body, response.StatusCode.ToString())}");
        }

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

        var issueKey = document.RootElement.GetProperty("key").GetString() ?? string.Empty;

        return new Published(item.Key, issueKey, $"{site}/browse/{issueKey}", "created");
    }

    /// <summary>
    /// Updates an issue that already exists.
    /// </summary>
    /// <remarks>
    /// The fields this product owns, and nothing else. Not the status, not the assignee and
    /// not the sprint: a person put those there and a re-publish is not a reason to undo it.
    /// </remarks>
    private async Task<Published> UpdateAsync(BacklogItem item, string issueKey, CancellationToken cancellationToken)
    {
        var fields = Fields(item);

        // The project and the type are set once. Jira refuses a type change through this
        // endpoint anyway, and sending them makes a rejected update look like a rejected
        // field rather than a rejected move.
        fields.Remove("project");
        fields.Remove("issuetype");

        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri($"{site}/rest/api/3/issue/{issueKey}"))
        {
            Content = JsonContent.Create(new { fields })
        };

        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            throw new InvalidOperationException(
                $"Jira refused to update {issueKey}: {Readable(body, response.StatusCode.ToString())}");
        }

        return new Published(item.Key, issueKey, $"{site}/browse/{issueKey}", "updated");
    }

    private Dictionary<string, object> Fields(BacklogItem item) => new(StringComparer.Ordinal)
    {
        ["project"] = new { key = projectKey },
        ["issuetype"] = new { id = TypeId(item.Type) },
        ["summary"] = Summary(item.Title),
        ["description"] = Document(item),

        // The deterministic key among the labels, which is how a re-publish finds this
        // issue rather than making a second one.
        ["labels"] = item.Tags.Append(item.Key).Select(Label).Distinct(StringComparer.Ordinal).ToArray()
    };

    /// <summary>
    /// The issue type this project actually offers for one of ours.
    /// </summary>
    /// <remarks>
    /// Ours are epic, feature, story, bug and task. Jira has no feature by default and a
    /// team managed project may have no epic either, so each falls back down a chain rather
    /// than failing: a feature that lands as a story is a board somebody can work with, and
    /// a publish that refuses because a project has no Feature type is not.
    /// </remarks>
    private string TypeId(string type)
    {
        string[] chain = type switch
        {
            "epic" => ["epic", "story", "task"],
            "feature" => ["story", "task"],
            "story" => ["story", "task"],
            "bug" => ["bug", "task"],
            _ => ["task", "story"]
        };

        foreach (var candidate in chain)
        {
            if (issueTypes.TryGetValue(candidate, out var id)) return id;
        }

        // Nothing matched, so whatever the project has first. A project always has at least
        // one issue type, and a publish into the wrong type is fixable in the board.
        return issueTypes.Values.FirstOrDefault()
            ?? throw new InvalidOperationException($"The project {projectKey} offers no issue types.");
    }

    /// <summary>A Jira summary, which is a single line and has a length limit.</summary>
    /// <param name="title">Ours, which can be longer.</param>
    private static string Summary(string title)
    {
        var flat = WhitespaceRun().Replace(title, " ").Trim();

        return flat.Length <= 250 ? flat : flat[..247] + "...";
    }

    /// <summary>A Jira label, which cannot contain whitespace.</summary>
    /// <param name="value">The tag or key.</param>
    private static string Label(string value) => WhitespaceRun().Replace(value.Trim(), "-");

    /// <summary>
    /// The description, in the format Jira's current API wants.
    /// </summary>
    /// <remarks>
    /// Atlassian Document Format rather than HTML or markdown. The backlog builder emits
    /// HTML because that is what the Azure DevOps description field takes, and changing it
    /// would change what gets published there, so it is converted here instead.
    ///
    /// Only the shapes the builder emits are understood: headings, paragraphs, lists and
    /// bold. That is not a general HTML converter and is not trying to be one; it is a
    /// converter for the six sections this product writes, and anything else degrades to
    /// its text rather than being dropped.
    /// </remarks>
    /// <param name="item">The item.</param>
    private static object Document(BacklogItem item)
    {
        var content = new List<object>();

        foreach (var (tag, text) in Blocks(item.DescriptionHtml))
        {
            if (text.Length == 0) continue;

            content.Add(tag switch
            {
                "h3" => new
                {
                    type = "heading",
                    attrs = new { level = 3 },
                    content = new[] { new { type = "text", text } }
                },
                "li" => new
                {
                    type = "paragraph",
                    content = new[] { new { type = "text", text = "• " + text } }
                },
                _ => new
                {
                    type = "paragraph",
                    content = new[] { new { type = "text", text } }
                }
            });
        }

        // The acceptance criteria and the test, which are separate fields on our side and
        // have nowhere of their own on a default Jira issue. Appended rather than dropped:
        // they are the half of a work item that says when it is finished.
        foreach (var (heading, body) in new[]
        {
            ("Acceptance criteria", item.AcceptanceCriteria),
            ("How to prove it", item.TestRequirement)
        })
        {
            if (string.IsNullOrWhiteSpace(body)) continue;

            content.Add(new
            {
                type = "heading",
                attrs = new { level = 3 },
                content = new[] { new { type = "text", text = heading } }
            });

            foreach (var (_, text) in Blocks(body))
            {
                if (text.Length == 0) continue;

                content.Add(new
                {
                    type = "paragraph",
                    content = new[] { new { type = "text", text } }
                });
            }
        }

        if (content.Count == 0)
        {
            content.Add(new { type = "paragraph", content = new[] { new { type = "text", text = item.Title } } });
        }

        return new { type = "doc", version = 1, content };
    }

    /// <summary>The block elements in some of our own HTML, as a tag and its text.</summary>
    /// <remarks>
    /// Shared with the GitHub publisher rather than owned here. Both have to read the same
    /// six sections out of the same markup, and two readers agree on the day they are
    /// written and disagree the first time the builder emits a tag only one of them knows.
    /// </remarks>
    /// <param name="html">The description or the criteria.</param>
    private static IEnumerable<(string Tag, string Text)> Blocks(string? html) =>
        DescriptionHtml.Blocks(html);

    /// <summary>Jira's error body, or the status where it is not one.</summary>
    /// <param name="body">What came back.</param>
    /// <param name="fallback">The status.</param>
    private static string Readable(string body, string fallback)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            var messages = new List<string>();

            if (document.RootElement.TryGetProperty("errorMessages", out var list))
            {
                messages.AddRange(list.EnumerateArray().Select(entry => entry.GetString() ?? string.Empty));
            }

            if (document.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                messages.AddRange(errors.EnumerateObject()
                    .Select(property => string.Create(
                        CultureInfo.InvariantCulture, $"{property.Name}: {property.Value.GetString()}")));
            }

            var joined = string.Join("; ", messages.Where(message => message.Length > 0));

            return joined.Length > 0 ? joined : fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    [GeneratedRegex(@"\s+", RegexOptions.None, 5000)]
    private static partial Regex WhitespaceRun();
}
