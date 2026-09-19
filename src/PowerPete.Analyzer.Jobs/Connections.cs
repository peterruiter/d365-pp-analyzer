namespace PowerPete.Analyzer.Jobs;

using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using PowerPete.Analyzer.Analysis;
using PowerPete.Analyzer.Data;
using PowerPete.Analyzer.Dataverse;
using PowerPete.Analyzer.DevOps;
using PowerPete.Analyzer.Domain;
using PowerPete.Analyzer.Estimation;
using PowerPete.Analyzer.Pipeline.Stages;

/// <summary>
/// Turns a connection row into something that can call something.
/// </summary>
/// <remarks>
/// The last piece of plumbing in the product, and the one with the most ways to be quietly
/// wrong. Every failure here is reported as a connection failure with the identity it reached,
/// never as an empty estate: a service principal that authenticates and holds no role reads an
/// environment with nothing in it, and that is the report this whole product exists to avoid
/// producing.
/// </remarks>
public sealed class ConnectionFactory(ISecretStore secrets)
{
    private static readonly HttpClient Shared = new();

    /// <summary>What a connection's settings hold. Never a secret; that lives in Key Vault.</summary>
    /// <param name="TenantId">Which tenant.</param>
    /// <param name="ClientId">Which app registration.</param>
    /// <param name="EnvironmentUrl">Which environment.</param>
    /// <param name="Organisation">Which Azure DevOps organisation.</param>
    /// <param name="Project">Which project.</param>
    /// <param name="BlobName">Which uploaded file, for the offline mode.</param>
    /// <param name="CheckerGeography">Where the checker runs. Data residency, so never a silent default.</param>
    public sealed record Settings(
        string? TenantId,
        string? ClientId,
        string? EnvironmentUrl,
        string? Organisation,
        string? Project,
        string? BlobName,
        string? CheckerGeography);

    /// <summary>Reads a connection's settings.</summary>
    /// <param name="connection">The connection.</param>
    public static Settings Read(Connection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        return JsonSerializer.Deserialize<Settings>(connection.SettingsJson)
            ?? new Settings(null, null, null, null, null, null, null);
    }

    /// <summary>
    /// An HTTP client authenticated against a Dataverse environment.
    /// </summary>
    /// <remarks>
    /// Read only is a property of the role the application user holds, not of this code, and
    /// the product cannot enforce it from here. What it can do is never write, which the
    /// Dataverse reader guarantees by having no create, update or delete in it at all, and
    /// record the identity so somebody can check what the role actually allowed.
    /// </remarks>
    /// <param name="connection">The connection.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<HttpClient> ForDataverseAsync(Connection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var settings = Read(connection);

        if (string.IsNullOrWhiteSpace(settings.EnvironmentUrl))
        {
            throw new InvalidOperationException(
                $"Connection '{connection.Name}' has no environment URL. Nothing can be read from it, and a run " +
                "against it would report an estate with nothing in it.");
        }

        var scope = $"{settings.EnvironmentUrl.TrimEnd('/')}/.default";
        var token = await TokenAsync(connection, settings, scope, cancellationToken).ConfigureAwait(false);

        var client = new HttpClient { BaseAddress = new Uri(settings.EnvironmentUrl) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
        client.DefaultRequestHeaders.Add("OData-Version", "4.0");

        // Dataverse counts a page as a page, and asking for more than five thousand rows gets
        // the request rejected rather than truncated.
        client.DefaultRequestHeaders.Add("Prefer", "odata.maxpagesize=1000, odata.include-annotations=\"*\"");

        return client;
    }

    /// <summary>An HTTP client for the Power Apps checker service.</summary>
    /// <param name="connection">The connection whose credential to use.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<HttpClient> ForCheckerAsync(Connection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var settings = Read(connection);
        var token = await TokenAsync(connection, settings, "https://api.advisor.powerapps.com/.default", cancellationToken)
            .ConfigureAwait(false);

        var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // The service needs a caller identifier and rejects the request without one, with a
        // message that does not say so.
        client.DefaultRequestHeaders.Add("x-ms-tenant-id", settings.TenantId ?? string.Empty);
        client.DefaultRequestHeaders.Add("x-ms-correlation-id", Guid.NewGuid().ToString());

        return client;
    }

    /// <summary>An HTTP client for Azure DevOps.</summary>
    /// <param name="connection">The Azure DevOps connection.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<HttpClient> ForDevOpsAsync(Connection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var client = new HttpClient();

        if (connection.SecretRef is not null && connection.SecretRef.Contains("pat", StringComparison.OrdinalIgnoreCase))
        {
            // A personal access token, because most clients will hand one over before they
            // will register an application. Its expiry is on the connection row and is warned
            // about, so a publish does not fail on a Friday for a reason nobody saw coming.
            var pat = await secrets.GetAsync(connection.SecretRef, cancellationToken).ConfigureAwait(false);
            var encoded = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{pat}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", encoded);

            return client;
        }

        var settings = Read(connection);
        var token = await TokenAsync(connection, settings, "499b84ac-1321-427f-aa17-267ca6975798/.default", cancellationToken)
            .ConfigureAwait(false);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<string> TokenAsync(Connection connection, Settings settings, string scope, CancellationToken cancellationToken)
    {
        var context = new TokenRequestContext([scope]);

        switch (connection.Mode)
        {
            case "servicePrincipal":
                {
                    if (connection.SecretRef is null)
                    {
                        throw new InvalidOperationException(
                            $"Connection '{connection.Name}' is a service principal with no secret reference.");
                    }

                    var secret = await secrets.GetAsync(connection.SecretRef, cancellationToken).ConfigureAwait(false);
                    var credential = new ClientSecretCredential(settings.TenantId, settings.ClientId, secret);
                    return (await credential.GetTokenAsync(context, cancellationToken).ConfigureAwait(false)).Token;
                }

            case "delegated":
                {
                    // A refresh token stored per engagement and revoked when it closes. Every
                    // report produced this way carries the identity it ran as, because a run
                    // made as a system administrator is not evidence that a least privileged
                    // integration could have made it.
                    throw new NotSupportedException(
                        "Delegated sign-in needs the interactive flow the web application owns, and there is no web " +
                        "application yet. Use a service principal, or the offline solution file mode.");
                }

            default:
                throw new InvalidOperationException(
                    $"Connection '{connection.Name}' is mode '{connection.Mode}', which cannot produce a token.");
        }
    }

    /// <summary>Opens an uploaded solution file.</summary>
    /// <remarks>
    /// Returns null rather than throwing when there is no file. An engagement can legitimately
    /// have only a live connection, and the extract stage decides what that means rather than
    /// this method deciding for it.
    /// </remarks>
    /// <param name="containerUri">Where uploads live.</param>
    /// <param name="blobName">Which file.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public static async Task<Stream?> OpenUploadAsync(Uri? containerUri, string? blobName, CancellationToken cancellationToken)
    {
        if (containerUri is null || string.IsNullOrWhiteSpace(blobName)) return null;

        var container = new BlobContainerClient(containerUri, new DefaultAzureCredential());
        var blob = container.GetBlobClient(blobName);

        if (!await blob.ExistsAsync(cancellationToken).ConfigureAwait(false)) return null;

        // Into memory rather than streamed. The zip reader seeks, a blob stream does not, and
        // a solution export is measured in megabytes rather than gigabytes.
        var buffer = new MemoryStream();
        await blob.DownloadToAsync(buffer, cancellationToken).ConfigureAwait(false);
        buffer.Position = 0;

        return buffer;
    }
}

/// <summary>
/// Assembles everything a run needs from its connections.
/// </summary>
/// <remarks>
/// The composition root proper. Every argument it produces is a closure over one connection,
/// so a stage never sees a credential and never chooses one.
/// </remarks>
public sealed class StageServicesFactory(
    ConnectionFactory connections,
    AnalysisStore analysis,
    WorkspaceStore workspace,
    WorkerSettings settings)
{
    /// <summary>Builds the services for one run.</summary>
    /// <param name="engagementId">Which engagement.</param>
    /// <param name="engagementName">Its name, for the work item tags.</param>
    /// <param name="source">What to read. Null for an engagement with no live connection.</param>
    /// <param name="target">Where to publish. Null on everything but a publish run.</param>
    /// <param name="criteria">The acceptance criteria contract.</param>
    /// <param name="backlogLanguage">
    /// The language the work items are written in, which is the language of the team who will
    /// pick them up and is not always the language of the report.
    /// </param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<StageServices> BuildAsync(
        Guid engagementId,
        string engagementName,
        Connection? source,
        Connection? target,
        IReadOnlyDictionary<string, Criterion> criteria,
        string? backlogLanguage,
        CancellationToken cancellationToken)
    {
        var uploads = settings.UploadContainerUri is null ? null : new Uri(settings.UploadContainerUri);
        var blobName = source is null ? null : ConnectionFactory.Read(source).BlobName;

        var overrides = await analysis.GetOverridesAsync(engagementId, cancellationToken).ConfigureAwait(false);

        var estimator = BuildEstimator(
            [.. overrides.Select(entry => new Override(
                entry.Scope, entry.RuleId, entry.ComponentTypeId, entry.FindingKey,
                entry.Low, entry.High, entry.StoryPoints, entry.Rationale, entry.SetBy))]);

        return new StageServices(
            OpenSolutionFile: token => ConnectionFactory.OpenUploadAsync(uploads, blobName, token),

            ReadEnvironment: async (includeRuntime, token) =>
            {
                if (source is null || source.Mode == "offlineZip") return null;

                var client = await connections.ForDataverseAsync(source, token).ConfigureAwait(false);
                var reader = new DataverseReader(client);

                var test = await reader.TestAsync(token).ConfigureAwait(false);

                // Recorded on the connection before anything is read. A connection that
                // authenticates and holds no role reads an environment with nothing in it, and
                // this row is how somebody spots that before a client does.
                await workspace.RecordConnectionTestAsync(
                    source.ConnectionId, test.Succeeded, test.Identity, test.Message, null, token).ConfigureAwait(false);

                if (!test.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"The connection '{source.Name}' did not authenticate: {test.Message}");
                }

                var result = await reader.ReadAsync([], includeRuntime, token).ConfigureAwait(false);

                return new EnvironmentRead(
                    result.Components,
                    result.Links,
                    [.. result.Reads.Select(read => (read.ComponentTypeId, read.Succeeded, read.RecordCount, read.FailureReason))],
                    result.Identity,
                    // Asked for is not the same as reached. Run history is not in the Dataverse
                    // Web API at all, so this stays false until that reader exists, and the
                    // rules needing it report as not assessed.
                    ReachedRuntime: false);
            },

            RunChecker: async (file, token) =>
            {
                if (source is null)
                {
                    return new CheckerOutcome(false, [], "The checker needs a connection to authenticate with.");
                }

                var geography = ConnectionFactory.Read(source).CheckerGeography;

                if (string.IsNullOrWhiteSpace(geography))
                {
                    // Never defaulted. Where the checker runs is a data residency decision and
                    // guessing it for a client is not this product's to make.
                    return new CheckerOutcome(false, [],
                        "No checker geography is set on this connection. Where the solution is uploaded for analysis " +
                        "is a data residency decision and is not defaulted.");
                }

                var client = await connections.ForCheckerAsync(source, token).ConfigureAwait(false);
                var checker = new CheckerClient(client, geography);

                var ruleset = await checker.ResolveRulesetAsync("Solution Checker", token).ConfigureAwait(false);

                if (ruleset is null)
                {
                    return new CheckerOutcome(false, [], "The checker service did not return its ruleset list.");
                }

                var run = await checker.AnalyseAsync(file, "solution.zip", ruleset.Value, TimeSpan.FromMinutes(20), token)
                    .ConfigureAwait(false);

                return new CheckerOutcome(run.Succeeded, run.Issues, run.FailureReason);
            },

            Estimator: estimator,
            BacklogBuilder: new BacklogBuilder(engagementId, engagementName, criteria, backlogLanguage),

            Publish: async (items, approvedHash, token) =>
            {
                if (target is null)
                {
                    throw new InvalidOperationException("A publish run reached the publish stage with no target connection.");
                }

                var devOpsSettings = ConnectionFactory.Read(target);
                var client = await connections.ForDevOpsAsync(target, token).ConfigureAwait(false);

                var publisher = new WorkItemPublisher(client,
                    devOpsSettings.Organisation ?? throw new InvalidOperationException("No Azure DevOps organisation is set."),
                    devOpsSettings.Project ?? throw new InvalidOperationException("No Azure DevOps project is set."));

                var published = await publisher.PublishAsync(items, approvedHash, approvedHash,
                    dryRun: false, confirmedCount: items.Count, token).ConfigureAwait(false);

                await analysis.WritePublishedAsync(Guid.Empty,
                    [.. published.Select(entry => (Guid.Empty, devOpsSettings.Organisation!, devOpsSettings.Project!,
                        entry.WorkItemId, entry.Url, entry.Action))],
                    token).ConfigureAwait(false);

                return published.Count;
            },

            Persist: new StorePersistence(analysis, workspace),
            FixedCosts: [.. EstimateCatalogue.FixedCosts.Select(cost => new FixedCost(cost.Id, cost.Name, cost.Low, cost.High))],
            Bands: EstimateCatalogue.Bands,
            ComplexityRules: ComplexityRule.FromContract(),
            RoadmapPositions: RuleCatalogue.Roadmap.ToDictionary(
                entry => entry.Key,
                entry => new RoadmapPosition(entry.Value.Row, entry.Value.Column, entry.Value.Band),
                StringComparer.Ordinal));
    }

    /// <summary>
    /// The estimator, with a model when one is configured and without one when it is not.
    /// </summary>
    /// <remarks>
    /// Degrades visibly rather than refusing to start. An engagement with no model still
    /// produces a report; every estimate says it is a band default and the score carries a
    /// caveat counting them.
    /// </remarks>
    private Estimator BuildEstimator(IReadOnlyList<Override> overrides)
    {
        if (string.IsNullOrWhiteSpace(settings.OpenAiEndpoint) || string.IsNullOrWhiteSpace(settings.OpenAiDeployment))
        {
            return new Estimator(EstimateCatalogue.Bands, overrides);
        }

        return new Estimator(
            EstimateCatalogue.Bands,
            overrides,
            new AzureOpenAiModel(new Uri(settings.OpenAiEndpoint), settings.OpenAiDeployment));
    }
}
