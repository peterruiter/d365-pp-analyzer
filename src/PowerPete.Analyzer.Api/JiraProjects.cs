namespace PowerPete.Analyzer.Api;

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

/// <summary>
/// The projects a Jira site holds, and what each one calls its issue types.
/// </summary>
/// <remarks>
/// The same reasoning as the Azure DevOps listing: a typed project key fails identically
/// whether the project does not exist or the account cannot see it, and a list that came
/// back through the same credential that will publish cannot contain either.
///
/// The issue types come with it because they are not knowable in advance. A team managed
/// project and a company managed one offer different ones, and neither reliably has Epic,
/// so the publisher is told what this project actually has rather than assuming.
/// </remarks>
public sealed class JiraProjects(IHttpClientFactory factory)
{
    /// <summary>One project, as the picker shows it.</summary>
    /// <param name="Id">Its identifier.</param>
    /// <param name="Name">What it is called, which is what a person recognises.</param>
    /// <param name="Key">Its key, which is what the API wants.</param>
    public sealed record Project(string Id, string Name, string Key);

    /// <summary>What the call produced.</summary>
    /// <param name="Projects">What came back.</param>
    /// <param name="Error">Why nothing did, in a sentence somebody can act on.</param>
    public sealed record Result(IReadOnlyList<Project> Projects, string? Error);

    /// <summary>
    /// A client authenticated against a Jira site.
    /// </summary>
    /// <remarks>
    /// Basic, with the account email and an API token. Atlassian tokens are not bearer
    /// tokens and do not work in an Authorization: Bearer header, which is the first thing
    /// everybody tries.
    /// </remarks>
    /// <param name="email">The account the token was issued to.</param>
    /// <param name="token">The API token.</param>
    public HttpClient Authenticated(string email, string token)
    {
        var client = factory.CreateClient("jira");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:{token}")));

        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return client;
    }

    /// <summary>Lists the projects.</summary>
    /// <param name="siteUrl">The site, for example https://contoso.atlassian.net.</param>
    /// <param name="email">The account the token belongs to.</param>
    /// <param name="token">The API token.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<Result> ListAsync(
        string siteUrl,
        string email,
        string token,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(siteUrl)) return new Result([], "This connection has no site address on it.");

        if (string.IsNullOrWhiteSpace(email))
        {
            return new Result([], "This connection has no account email on it. An Atlassian API token only works "
                + "with the address it was issued to.");
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return new Result([], "This connection has no API token. Edit it and add one.");
        }

        using var client = Authenticated(email, token);

        var url = $"{siteUrl.TrimEnd('/')}/rest/api/3/project/search?maxResults=100&orderBy=name";

        try
        {
            using var response = await client.GetAsync(new Uri(url), cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return new Result([], (int)response.StatusCode switch
                {
                    401 => "Atlassian refused the credential. An API token is paired with the email it was issued "
                        + "to, and the two have to match; a token on its own, or with the wrong address, arrives here.",
                    403 => "That account cannot browse projects on this site.",
                    404 => $"No Jira site answered at {siteUrl}. The address is the one you open in a browser, "
                        + "usually ending in atlassian.net.",
                    _ => $"Jira answered {(int)response.StatusCode} {response.ReasonPhrase}."
                });
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty("values", out var values))
            {
                return new Result([], "That address answered, but not with a list of projects. Check it is the site "
                    + "address rather than a board or an issue inside one.");
            }

            var projects = values.EnumerateArray()
                .Select(project => new Project(
                    project.GetProperty("id").GetString() ?? string.Empty,
                    project.GetProperty("name").GetString() ?? string.Empty,
                    project.GetProperty("key").GetString() ?? string.Empty))
                .Where(project => project.Key.Length > 0)
                .ToList();

            return new Result(projects, null);
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new Result([], $"Jira could not be reached: {failure.Message}");
        }
    }

    /// <summary>
    /// What one project calls its issue types, keyed by lower case name.
    /// </summary>
    /// <param name="client">An authenticated client.</param>
    /// <param name="siteUrl">The site.</param>
    /// <param name="projectKey">The project.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public static async Task<IReadOnlyDictionary<string, string>> IssueTypesAsync(
        HttpClient client,
        string siteUrl,
        string projectKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        var url = $"{siteUrl.TrimEnd('/')}/rest/api/3/project/{Uri.EscapeDataString(projectKey)}";

        using var response = await client.GetAsync(new Uri(url), cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!document.RootElement.TryGetProperty("issueTypes", out var types))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var found = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var type in types.EnumerateArray())
        {
            // Subtasks are skipped. Nothing this product produces is one, and a subtask
            // created without a parent is refused by Jira rather than created loose.
            if (type.TryGetProperty("subtask", out var subtask) && subtask.GetBoolean()) continue;

            var name = type.GetProperty("name").GetString();
            var id = type.GetProperty("id").GetString();

            if (name is { Length: > 0 } && id is { Length: > 0 })
            {
                found.TryAdd(name.ToLowerInvariant(), id);
            }
        }

        return found;
    }
}
