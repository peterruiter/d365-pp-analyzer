namespace PowerPete.Analyzer.Api;

using System.Text.Json;
using PowerPete.Analyzer.DevOps;

/// <summary>
/// The repositories an owner holds, as the publish picker shows them.
/// </summary>
/// <remarks>
/// So the person publishing picks a repository from a list rather than typing its name, for
/// the same reason the Azure DevOps project is a list: GitHub answers 404 both for a
/// repository that does not exist and for a private one the token cannot see, and from here
/// the two replies are identical. A list that came back through the same token that will do
/// the publishing cannot contain either.
///
/// The owner sits on the connection and the repository is chosen per publish, which is the
/// shape Azure DevOps already has. One organisation holds many repositories and the backlog
/// from one engagement does not always land in the same one.
/// </remarks>
public sealed class GitHubRepositories(IHttpClientFactory factory)
{
    /// <summary>How many pages of a hundred to read before stopping.</summary>
    /// <remarks>
    /// Five hundred repositories, which is where the Azure DevOps project list stops too. An
    /// owner with more than that has a naming problem a picker cannot solve, and reading
    /// every page of a large organisation would hold the screen for a minute.
    /// </remarks>
    private const int MaxPages = 5;

    /// <summary>One repository, as the picker shows it.</summary>
    /// <param name="Id">Its identifier.</param>
    /// <param name="Name">Its name within the owner.</param>
    /// <param name="FullName">owner/name, which is what a person recognises.</param>
    /// <param name="Description">What it is for, where anybody wrote one.</param>
    public sealed record Repository(string Id, string Name, string FullName, string? Description);

    /// <summary>What the call produced.</summary>
    /// <param name="Repositories">What came back, minus the ones a backlog cannot land in.</param>
    /// <param name="Hidden">How many were left out because issues are off or they are archived.</param>
    /// <param name="Error">Why nothing came back, in a sentence somebody can act on.</param>
    public sealed record Result(IReadOnlyList<Repository> Repositories, int Hidden, string? Error);

    /// <summary>
    /// Lists them.
    /// </summary>
    /// <remarks>
    /// The organisation endpoint first, then the token's own repositories. An owner is either
    /// an organisation or a person and the two are different endpoints, but the second has to
    /// be the authenticated one rather than /users/{owner}/repos: that lists only public
    /// repositories, so a consultant's own private repository would be missing from the
    /// picker while being perfectly publishable.
    /// </remarks>
    /// <param name="owner">The organisation or user.</param>
    /// <param name="token">A personal access token with issues write.</param>
    /// <param name="api">The API base, for GitHub Enterprise Server. Defaults to github.com's.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<Result> ListAsync(
        string owner,
        string token,
        string? api,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(owner))
        {
            return new Result([], 0, "This connection has no owner on it. That is the organisation or user "
                + "the repositories belong to.");
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return new Result([], 0, "This connection has no personal access token. Edit it and add one.");
        }

        using var client = factory.CreateClient("github");
        GitHubPublisher.Authenticate(client, token);

        // As in the publisher: blank means github.com, because the wizard tells somebody
        // to leave this one empty and empty is not the only way a box ends up meaning it.
        var root = (string.IsNullOrWhiteSpace(api) ? GitHubPublisher.PublicApi : api.Trim()).TrimEnd('/');
        var trimmed = owner.Trim().Trim('/');

        try
        {
            var (found, error) = await ReadAsync(
                client, $"{root}/orgs/{Uri.EscapeDataString(trimmed)}/repos?type=all&sort=full_name&per_page=100",
                cancellationToken).ConfigureAwait(false);

            // Not an organisation, which is not an error. A personal account answers 404 here
            // and its repositories are on the authenticated user's own list.
            if (error == 404)
            {
                (found, error) = await ReadAsync(
                    client, $"{root}/user/repos?affiliation=owner,collaborator,organization_member&sort=full_name&per_page=100",
                    cancellationToken).ConfigureAwait(false);
            }

            if (error is { } status)
            {
                return new Result([], 0, status switch
                {
                    401 => "GitHub refused the token. One that has expired, and one without repository "
                        + "access, both arrive here. Edit the connection and paste a new one.",
                    403 => "GitHub refused the request. A fine grained token has to name this owner's "
                        + "repositories explicitly, and a rate limited one also answers 403.",
                    404 => $"Nothing answered for {trimmed}. That is the organisation or user name as it "
                        + "appears in the address, not the full repository address.",
                    _ => $"GitHub answered {status.ToString(System.Globalization.CultureInfo.InvariantCulture)}."
                });
            }

            // Only the ones a backlog can actually land in. Publishing into a repository with
            // issues switched off answers 410 on the first item, and an archived repository
            // refuses every write, so offering either is offering a guaranteed failure.
            var usable = found
                .Where(entry => entry.Owner.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var open = usable.Where(entry => entry.HasIssues && !entry.Archived).ToList();

            return new Result(
                [.. open
                    .Select(entry => new Repository(entry.Id, entry.Name, entry.FullName, entry.Description))
                    .OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)],
                usable.Count - open.Count,
                null);
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new Result([], 0, $"GitHub could not be reached: {failure.Message}");
        }
    }

    /// <summary>One repository as the API gives it, before anything is decided about it.</summary>
    private sealed record Entry(
        string Id, string Name, string FullName, string Owner, string? Description, bool HasIssues, bool Archived);

    /// <summary>
    /// Reads every page of a listing, or the status that stopped it.
    /// </summary>
    /// <remarks>
    /// Paged by number rather than by following the Link header. Both work; this one does not
    /// need a parser for a header format that exists nowhere else in this product.
    /// </remarks>
    private static async Task<(List<Entry> Found, int? Error)> ReadAsync(
        HttpClient client, string url, CancellationToken cancellationToken)
    {
        var found = new List<Entry>();

        for (var page = 1; page <= MaxPages; page++)
        {
            using var response = await client.GetAsync(
                new Uri($"{url}&page={page.ToString(System.Globalization.CultureInfo.InvariantCulture)}"),
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode) return ([], (int)response.StatusCode);

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (document.RootElement.ValueKind != JsonValueKind.Array) return (found, null);

            var before = found.Count;

            foreach (var entry in document.RootElement.EnumerateArray())
            {
                var name = Text(entry, "name");

                if (name.Length == 0) continue;

                found.Add(new Entry(
                    entry.TryGetProperty("id", out var id) ? id.ToString() : name,
                    name,
                    Text(entry, "full_name"),
                    entry.TryGetProperty("owner", out var owner) ? Text(owner, "login") : string.Empty,
                    entry.TryGetProperty("description", out var description) ? description.GetString() : null,
                    !entry.TryGetProperty("has_issues", out var issues) || issues.ValueKind != JsonValueKind.False,
                    entry.TryGetProperty("archived", out var archived) && archived.ValueKind == JsonValueKind.True));
            }

            // A short page is the last one. Asking for the next would be one wasted call per
            // listing, every time anybody opens the picker.
            if (found.Count - before < 100) break;
        }

        return (found, null);
    }

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() ?? string.Empty : string.Empty;
}
