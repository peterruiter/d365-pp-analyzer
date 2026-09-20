namespace PowerPete.Analyzer.Dataverse;

using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using PowerPete.Analyzer.Data;

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
    /// <param name="BlobName">Which uploaded file, for the offline mode. Named uploadedFile in the settings, because that is what the extraction contract calls it and the contract names the fields the wizard collects.</param>
    /// <param name="CheckerGeography">Where the checker runs. Data residency, so never a silent default.</param>
    public sealed record Settings(
        string? TenantId,
        string? ClientId,
        string? EnvironmentUrl,
        string? Organisation,
        string? Project,
        [property: JsonPropertyName("uploadedFile")] string? BlobName,
        string? CheckerGeography);

    /// <summary>How a connection's settings are written and read. Both sides use this.</summary>
    private static readonly JsonSerializerOptions SettingsJson = new(JsonSerializerDefaults.Web);

    /// <summary>Reads a connection's settings.</summary>
    /// <param name="connection">The connection.</param>
    public static Settings Read(Connection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        // Web defaults, so the camel case the wizard writes binds to the Pascal case this
        // record declares. Without them every setting deserialised as null: a service
        // principal connection had no environment URL and failed at the point of reading the
        // estate, which reads as a permissions problem and is a serialiser setting.
        return JsonSerializer.Deserialize<Settings>(connection.SettingsJson, SettingsJson)
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

        // Ten minutes, against a default of one hundred seconds. Every metadata read here
        // answers in under a second, but ExportSolution is the platform packaging a zip on
        // demand and one small solution took seventy two: the default would have failed on
        // the first client whose solution was twice that size, as a timeout that reads like
        // an outage.
        client.Timeout = TimeSpan.FromMinutes(10);

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

    /// <summary>
    /// This product's own app registration, which is what a delegated sign-in was granted to.
    /// </summary>
    /// <remarks>
    /// Read from the environment, like everything else the worker is configured with. It is
    /// the same registration the web application signs people in with: the consent a
    /// consultant gave was given to this product, not to a registration per client.
    /// </remarks>
    private static (string Instance, string ClientId, string ClientSecret) ProductRegistration()
    {
        var clientId = Environment.GetEnvironmentVariable("AzureAd__ClientId");
        var clientSecret = Environment.GetEnvironmentVariable("AzureAd__ClientSecret");

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException(
                "An interactive sign-in needs this product's own Entra client id and secret to redeem its refresh "
                + "token, and the worker has not been given them. Set AzureAd__ClientId and AzureAd__ClientSecret.");
        }

        return (
            Environment.GetEnvironmentVariable("AzureAd__Instance") ?? DelegatedTokens.DefaultInstance,
            clientId,
            clientSecret);
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
                    // A refresh token, obtained when somebody signed in to this environment in
                    // the web application and stored per connection in Key Vault. Every report
                    // produced this way carries the identity it ran as, because a run made as a
                    // system administrator is not evidence that a least privileged integration
                    // could have made it.
                    if (connection.SecretRef is null)
                    {
                        throw new InvalidOperationException(
                            $"Connection '{connection.Name}' is an interactive sign-in that nobody has completed. "
                            + "Open it in the web application and sign in to the environment.");
                    }

                    if (string.IsNullOrWhiteSpace(settings.EnvironmentUrl))
                    {
                        throw new InvalidOperationException(
                            $"Connection '{connection.Name}' has no environment address on it.");
                    }

                    var product = ProductRegistration();
                    var name = SecretNames.For(connection.SecretRef, "refresh");
                    var refreshToken = await secrets.GetAsync(name, cancellationToken).ConfigureAwait(false)
                        ?? throw new InvalidOperationException(
                            $"The sign-in for connection '{connection.Name}' is not in the vault any more. Sign in to the environment again.");

                    var environment = DelegatedTokens.NormaliseEnvironment(settings.EnvironmentUrl)
                        ?? throw new InvalidOperationException(
                            $"Connection '{connection.Name}' has an environment address that is not a usable address.");

                    var tokens = await DelegatedTokens.RefreshAsync(
                        Shared,
                        new DelegatedTokens.TokenRequest(
                            product.Instance, "organizations", product.ClientId, product.ClientSecret, environment),
                        refreshToken,
                        cancellationToken).ConfigureAwait(false);

                    // Entra rotates refresh tokens: the response carries a new one and the old
                    // one stops working. Storing it back is what makes the second run work.
                    // Key Vault keeps the previous value as an older version, so a run that
                    // fails between here and the next one is recoverable.
                    if (!string.IsNullOrWhiteSpace(tokens.RefreshToken) && tokens.RefreshToken != refreshToken)
                    {
                        await secrets.SetAsync(name, tokens.RefreshToken, cancellationToken).ConfigureAwait(false);
                    }

                    return tokens.AccessToken;
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

        // Streamed, not downloaded. The comment that stood here said a blob stream does not
        // seek and so the file had to come into memory first. It does seek: the stream reads
        // ranges on demand, which is precisely what a zip needs, because the directory that
        // says what is in the file is at the end of it.
        //
        // Four megabytes of buffer, so the directory read at the end and the entries read
        // from the front are a handful of requests rather than one per entry.
        return await blob.OpenReadAsync(
            new BlobOpenReadOptions(allowModifications: false) { BufferSize = 4 * 1024 * 1024 },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Opens a blob to write to, replacing whatever is there.
    /// </summary>
    /// <remarks>
    /// Used by the export: a solution comes out of the environment and goes straight here,
    /// so the largest thing the worker holds is a buffer rather than a client's estate.
    /// </remarks>
    /// <param name="containerUri">The container.</param>
    /// <param name="blobName">The blob.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public static async Task<Stream> CreateBlobAsync(
        Uri containerUri, string blobName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(containerUri);

        var container = new BlobContainerClient(containerUri, new DefaultAzureCredential());

        return await container.GetBlobClient(blobName).OpenWriteAsync(
            overwrite: true,
            new BlobOpenWriteOptions { BufferSize = 4 * 1024 * 1024 },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Whether a blob is there.</summary>
    /// <param name="containerUri">The container.</param>
    /// <param name="blobName">The blob.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public static async Task<bool> BlobExistsAsync(
        Uri? containerUri, string blobName, CancellationToken cancellationToken)
    {
        if (containerUri is null) return false;

        var container = new BlobContainerClient(containerUri, new DefaultAzureCredential());

        return await container.GetBlobClient(blobName).ExistsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes every blob under a prefix.
    /// </summary>
    /// <remarks>
    /// Deleting a run deletes its findings, and a client's exported solutions are the most
    /// sensitive thing this product ever writes down. Leaving them behind after somebody
    /// asked for the run to go would be keeping a copy of their estate they did not ask us
    /// to keep.
    /// </remarks>
    /// <param name="containerUri">The container.</param>
    /// <param name="prefix">What the blobs' names start with.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>How many were removed.</returns>
    public static async Task<int> DeleteBlobsAsync(
        Uri? containerUri, string prefix, CancellationToken cancellationToken)
    {
        if (containerUri is null || string.IsNullOrWhiteSpace(prefix)) return 0;

        var container = new BlobContainerClient(containerUri, new DefaultAzureCredential());
        var removed = 0;

        await foreach (var blob in container
            .GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken)
            .ConfigureAwait(false))
        {
            await container.DeleteBlobIfExistsAsync(blob.Name, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            removed++;
        }

        return removed;
    }
}
