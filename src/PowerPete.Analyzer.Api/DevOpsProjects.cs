namespace PowerPete.Analyzer.Api;

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

/// <summary>
/// The projects an Azure DevOps organisation holds.
/// </summary>
/// <remarks>
/// So the person publishing picks a project from a list rather than typing its name. A typed
/// project name is the one field in this whole flow that fails silently in the worst way:
/// Azure DevOps answers 404 for a project that does not exist and for one the token cannot
/// see, and the two are indistinguishable from here. A list that came back from the same
/// token that will do the publishing cannot contain either.
///
/// One organisation commonly holds several projects and the backlog from one engagement does
/// not always land in the same one, which is why the project is chosen per publish rather
/// than stored on the connection.
/// </remarks>
public sealed class DevOpsProjects(IHttpClientFactory factory)
{
    /// <summary>One project, as the picker shows it.</summary>
    /// <param name="Id">Its identifier.</param>
    /// <param name="Name">What it is called.</param>
    /// <param name="Description">What it is for, where anybody wrote one.</param>
    public sealed record Project(string Id, string Name, string? Description);

    /// <summary>What the call produced.</summary>
    /// <param name="Projects">What came back.</param>
    /// <param name="Error">Why nothing did, in a sentence somebody can act on.</param>
    public sealed record Result(IReadOnlyList<Project> Projects, string? Error);

    /// <summary>
    /// Lists them.
    /// </summary>
    /// <param name="organisationUrl">The organisation, for example https://dev.azure.com/contoso.</param>
    /// <param name="token">A personal access token with work item read.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<Result> ListAsync(
        string organisationUrl,
        string token,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(organisationUrl))
        {
            return new Result([], "This connection has no organisation address on it.");
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return new Result([], "This connection has no personal access token. Edit it and add one.");
        }

        using var client = factory.CreateClient("devops");

        // Basic with an empty user name, which is what Azure DevOps expects for a PAT.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($":{token}")));

        var url = $"{organisationUrl.TrimEnd('/')}/_apis/projects?api-version=7.1&$top=500";

        try
        {
            using var response = await client.GetAsync(new Uri(url), cancellationToken).ConfigureAwait(false);

            // Said in terms of the thing somebody can fix. A 401 here is a token that has
            // expired or was never scoped to work items, and "Unauthorized" on its own has
            // sent more than one person to check the wrong thing.
            if (!response.IsSuccessStatusCode)
            {
                return new Result([], (int)response.StatusCode switch
                {
                    401 or 203 => "Azure DevOps refused the token. A personal access token that has expired, or "
                        + "one without Work Items read, both arrive here. Edit the connection and paste a new one.",
                    404 => $"No organisation answered at {organisationUrl}. The address is the one you open in a "
                        + "browser, ending in the organisation name.",
                    _ => $"Azure DevOps answered {(int)response.StatusCode} {response.ReasonPhrase}."
                });
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty("value", out var value))
            {
                // A sign-in page rather than an API answer, which is what an organisation
                // URL with a path on the end of it returns.
                return new Result([], "That address answered, but not with a list of projects. Check it is the "
                    + "organisation address and not a project or a repository inside one.");
            }

            var projects = value.EnumerateArray()
                .Select(project => new Project(
                    project.GetProperty("id").GetString() ?? string.Empty,
                    project.GetProperty("name").GetString() ?? string.Empty,
                    project.TryGetProperty("description", out var description) ? description.GetString() : null))
                .Where(project => project.Name.Length > 0)
                .OrderBy(project => project.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            return new Result(projects, null);
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new Result([], $"Azure DevOps could not be reached: {failure.Message}");
        }
    }
}
