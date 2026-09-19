namespace PowerPete.Analyzer.Api;

using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using PowerPete.Analyzer.Data;
using PowerPete.Analyzer.Dataverse;

/// <summary>
/// Connecting an environment by signing in to it.
/// </summary>
/// <remarks>
/// A consultant types the environment address and signs in. There is no app registration to
/// create, no security role to request and no tenant administrator to wait for, because
/// Dataverse's user_impersonation is a permission a person grants for themselves.
///
/// The round trip is: the product creates the connection, sends the browser to Entra scoped
/// to that one environment, takes the code back, exchanges it for a refresh token, proves
/// the token works by asking the environment who it is, and stores the refresh token in Key
/// Vault. The worker redeems that refresh token whenever it gets to the run.
///
/// The state parameter is signed rather than opaque. It carries which connection the code
/// belongs to, and a value the caller can edit is a value that points the callback at
/// somebody else's connection.
/// </remarks>
/// <param name="workspace">Where connections live.</param>
/// <param name="secrets">Where the refresh token goes.</param>
/// <param name="protector">Signs the state so it cannot be rewritten in the address bar.</param>
/// <param name="settings">Which app registration, and where Entra is.</param>
/// <param name="http">For the token endpoint and the identity check.</param>
public sealed class InteractiveSignIn(
    WorkspaceStore workspace,
    ISecretStore secrets,
    IDataProtectionProvider protector,
    InteractiveSignIn.Options settings,
    HttpClient http)
{
    /// <summary>What this deployment's app registration is.</summary>
    /// <param name="Instance">The Entra instance.</param>
    /// <param name="TenantId">The tenant the product itself is registered in.</param>
    /// <param name="ClientId">The app registration.</param>
    /// <param name="ClientSecret">Its secret. Without one the product cannot redeem a code and this mode cannot work.</param>
    public sealed record Options(string Instance, string? TenantId, string? ClientId, string? ClientSecret);

    private const string Purpose = "PowerPete.Analyzer.InteractiveSignIn.v1";

    /// <summary>How long somebody has to finish signing in before the state is refused.</summary>
    /// <remarks>
    /// Ten minutes. Long enough to find the right account and type a second factor, short
    /// enough that a link left in a browser history is not a way back into an environment.
    /// </remarks>
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    /// <summary>Whether this deployment can do an interactive sign-in at all.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(settings.ClientId)
        && !string.IsNullOrWhiteSpace(settings.ClientSecret)
        && secrets.IsConfigured;

    /// <summary>Why it cannot, in a sentence somebody can act on.</summary>
    public string NotConfiguredReason
    {
        get
        {
            if (string.IsNullOrWhiteSpace(settings.ClientId)) return "No Entra app registration is configured on this deployment.";

            if (string.IsNullOrWhiteSpace(settings.ClientSecret))
            {
                return "This deployment has no Entra client secret, so it cannot exchange a sign-in for a token. "
                    + "Set AzureAd__ClientSecret on the container app.";
            }

            return "No Key Vault is configured, so there is nowhere to keep the sign-in. Nothing is ever written to the database.";
        }
    }

    /// <summary>
    /// Where to send the browser to sign in to one connection's environment.
    /// </summary>
    /// <param name="connection">The connection being signed in to.</param>
    /// <param name="redirectUri">The callback, which must match what is registered in Entra.</param>
    public Uri? AuthorizeUrl(Connection connection, string redirectUri)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var environment = DelegatedTokens.NormaliseEnvironment(EnvironmentOf(connection));

        if (environment is null) return null;

        var state = protector.CreateProtector(Purpose).Protect(JsonSerializer.Serialize(new State(
            connection.ConnectionId,
            connection.EngagementId,
            DateTimeOffset.UtcNow.Add(StateLifetime).ToUnixTimeSeconds())));

        // The tenant the environment belongs to is not necessarily the one the product is
        // registered in, and a consultant's own account may be a guest in the client's. So
        // the authorize call goes to "organizations", which lets Entra work out where the
        // account lives rather than the product guessing and failing with an error about an
        // unknown user.
        return DelegatedTokens.AuthorizeUrl(
            settings.Instance,
            "organizations",
            settings.ClientId!,
            redirectUri,
            environment,
            Uri.EscapeDataString(state));
    }

    /// <summary>What came back from Entra, once it has been checked.</summary>
    /// <param name="ConnectionId">Which connection it was for.</param>
    /// <param name="EngagementId">Which engagement, so the caller can send the reader back to it.</param>
    /// <param name="Succeeded">Whether the environment accepted the token.</param>
    /// <param name="Message">What to tell the reader.</param>
    public sealed record Outcome(Guid ConnectionId, Guid EngagementId, bool Succeeded, string Message);

    /// <summary>
    /// Takes the code Entra sent back and turns it into a stored, proven connection.
    /// </summary>
    /// <param name="state">The signed state, exactly as it came back.</param>
    /// <param name="code">The authorization code.</param>
    /// <param name="redirectUri">The same callback the authorize call used.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<Outcome?> CompleteAsync(
        string state,
        string code,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        State? unwrapped;

        try
        {
            unwrapped = JsonSerializer.Deserialize<State>(
                protector.CreateProtector(Purpose).Unprotect(Uri.UnescapeDataString(state)));
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Tampered with, or signed by a container that has since been replaced. Either
            // way it is not a state this deployment issued.
            return null;
        }

        if (unwrapped is null) return null;

        if (DateTimeOffset.FromUnixTimeSeconds(unwrapped.ExpiresAt) < DateTimeOffset.UtcNow)
        {
            return new Outcome(unwrapped.ConnectionId, unwrapped.EngagementId, false,
                "The sign-in took too long and has to be started again.");
        }

        var connection = await workspace.GetConnectionAsync(unwrapped.ConnectionId, cancellationToken).ConfigureAwait(false);

        if (connection is null) return null;

        var environment = DelegatedTokens.NormaliseEnvironment(EnvironmentOf(connection));

        if (environment is null)
        {
            return new Outcome(connection.ConnectionId, connection.EngagementId, false,
                "This connection has no environment address on it.");
        }

        var request = new DelegatedTokens.TokenRequest(
            settings.Instance, "organizations", settings.ClientId!, settings.ClientSecret!, environment);

        TokenSet tokens;

        try
        {
            tokens = await DelegatedTokens.RedeemCodeAsync(http, request, code, redirectUri, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException failure)
        {
            await workspace.RecordConnectionTestAsync(
                connection.ConnectionId, false, null, failure.Message, null, cancellationToken).ConfigureAwait(false);

            return new Outcome(connection.ConnectionId, connection.EngagementId, false, failure.Message);
        }

        if (string.IsNullOrWhiteSpace(tokens.RefreshToken))
        {
            const string noRefresh =
                "Entra returned no refresh token, so this connection would stop working the moment the browser closed. "
                + "The app registration needs offline_access.";

            await workspace.RecordConnectionTestAsync(
                connection.ConnectionId, false, null, noRefresh, null, cancellationToken).ConfigureAwait(false);

            return new Outcome(connection.ConnectionId, connection.EngagementId, false, noRefresh);
        }

        // Proved before it is stored. A token that Entra issued is not the same thing as a
        // user the environment knows: somebody can hold a valid token for an organisation
        // they have no application user in, and the failure would otherwise surface an hour
        // later as an empty estate.
        var identity = await WhoAmIAsync(environment, tokens.AccessToken, cancellationToken).ConfigureAwait(false);

        if (identity.Error is not null)
        {
            await workspace.RecordConnectionTestAsync(
                connection.ConnectionId, false, null, identity.Error, null, cancellationToken).ConfigureAwait(false);

            return new Outcome(connection.ConnectionId, connection.EngagementId, false, identity.Error);
        }

        var prefix = SecretNames.NewPrefix();
        await secrets.SetAsync(SecretNames.For(prefix, "refresh"), tokens.RefreshToken!, cancellationToken).ConfigureAwait(false);

        // Ninety days is what Entra gives a refresh token that is used; the health check
        // warns before it runs out. Recorded rather than assumed so somebody can see it.
        await workspace.SetConnectionSecretAsync(
            connection.ConnectionId, prefix, DateTime.UtcNow.AddDays(90), cancellationToken).ConfigureAwait(false);

        var reach = JsonSerializer.Serialize(new
        {
            metadata = "full",
            solutionZip = "full",
            checker = "full",
            runtime = "full"
        });

        var message = string.Create(CultureInfo.InvariantCulture,
            $"Signed in to {environment} as {identity.UserId}. The run will read this environment as that person.");

        await workspace.RecordConnectionTestAsync(
            connection.ConnectionId, true, identity.UserId, message, reach, cancellationToken).ConfigureAwait(false);

        return new Outcome(connection.ConnectionId, connection.EngagementId, true, message);
    }

    /// <summary>Asks the environment who the token belongs to.</summary>
    /// <param name="environmentUrl">The environment.</param>
    /// <param name="accessToken">The token just issued.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    private async Task<(string? UserId, string? Error)> WhoAmIAsync(
        string environmentUrl,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"{environmentUrl}/api/data/v9.2/WhoAmI"));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;

                return (null, string.Create(CultureInfo.InvariantCulture,
                    $"{environmentUrl} refused the sign-in ({status}). The account signed in with may have no user in that environment."));
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(body);

            var userId = document.RootElement.TryGetProperty("UserId", out var value)
                ? value.GetString()
                : "unknown";

            return (userId, null);
        }
        catch (HttpRequestException failure)
        {
            return (null, $"Could not reach {environmentUrl}. {failure.Message}");
        }
        catch (JsonException)
        {
            return (null, $"{environmentUrl} answered with something that was not a WhoAmI response.");
        }
    }

    /// <summary>The environment address a connection carries.</summary>
    /// <param name="connection">The connection.</param>
    private static string? EnvironmentOf(Connection connection)
    {
        try
        {
            using var document = JsonDocument.Parse(connection.SettingsJson);

            return document.RootElement.TryGetProperty("environmentUrl", out var value)
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record State(Guid ConnectionId, Guid EngagementId, long ExpiresAt);
}
