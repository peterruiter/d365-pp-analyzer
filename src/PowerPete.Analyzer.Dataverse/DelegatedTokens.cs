namespace PowerPete.Analyzer.Dataverse;

using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

/// <summary>
/// An access token for a Dataverse environment, obtained as the person who signed in.
/// </summary>
/// <param name="AccessToken">What the reader puts on the wire.</param>
/// <param name="RefreshToken">What gets stored so the worker can do this again in an hour.</param>
/// <param name="ExpiresOn">When the access token stops working.</param>
public sealed record TokenSet(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresOn);

/// <summary>
/// The interactive sign-in half of the delegated mode.
/// </summary>
/// <remarks>
/// A consultant types the environment address and signs in; the product then reads that
/// environment as them. No app registration to create, no security role to request, no
/// tenant administrator in the loop, because Dataverse's user_impersonation is a permission
/// a person can consent to for themselves.
///
/// What makes this work for a product whose analysis runs in a background worker is the
/// refresh token. The browser does the sign-in, the refresh token goes to Key Vault, and the
/// worker redeems it hours later when it gets round to the run. Without that the only thing
/// a delegated sign-in could analyse is whatever finishes before the browser tab closes.
///
/// Every token is scoped to one environment. A refresh token issued for one organisation
/// cannot be redeemed for another, which is the property that keeps an engagement's
/// credential from reaching a different client's estate.
/// </remarks>
public static class DelegatedTokens
{
    /// <summary>Where Entra lives. Overridable for a sovereign cloud.</summary>
    public const string DefaultInstance = "https://login.microsoftonline.com/";

    /// <summary>
    /// The environment address, in the form a scope can be built from.
    /// </summary>
    /// <remarks>
    /// A consultant will paste whatever is in their address bar, which is frequently the
    /// maker portal path, a trailing slash, or the host with no scheme at all. The scope has
    /// to be exactly the origin, so this takes the origin and discards the rest rather than
    /// failing at the authorize step with an error about an unknown resource.
    /// </remarks>
    /// <param name="value">Whatever was typed.</param>
    /// <returns>The origin, with no trailing slash, or null when it is not a usable address.</returns>
    public static string? NormaliseEnvironment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var trimmed = value.Trim();

        if (!trimmed.Contains("://", StringComparison.Ordinal)) trimmed = "https://" + trimmed;

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps) return null;
        if (string.IsNullOrWhiteSpace(uri.Host) || !uri.Host.Contains('.', StringComparison.Ordinal)) return null;

        return $"https://{uri.Host}";
    }

    /// <summary>
    /// Where to send the browser so somebody can sign in to one environment.
    /// </summary>
    /// <remarks>
    /// The scope names the environment rather than a static resource, which is what makes the
    /// consent screen say which organisation is being read. offline_access is what asks for
    /// the refresh token; without it the sign-in works, the run an hour later does not, and
    /// nothing in between says why.
    /// </remarks>
    /// <param name="instance">The Entra instance.</param>
    /// <param name="tenantId">The tenant to sign in against, or "organizations" for any.</param>
    /// <param name="clientId">This product's app registration.</param>
    /// <param name="redirectUri">Where Entra sends the code back.</param>
    /// <param name="environmentUrl">The normalised environment origin.</param>
    /// <param name="state">Opaque value returned unchanged, carrying which connection this is.</param>
    public static Uri AuthorizeUrl(
        string instance,
        string tenantId,
        string clientId,
        string redirectUri,
        string environmentUrl,
        string state)
    {
        var query = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["client_id"] = clientId,
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri,
            ["response_mode"] = "query",
            ["scope"] = $"{environmentUrl}/user_impersonation offline_access",
            ["state"] = state,

            // Always ask. A consultant connecting a second client's environment while signed
            // in as themselves must get the account picker, or the product quietly reads the
            // new environment as whoever the browser happened to remember.
            ["prompt"] = "select_account"
        };

        var authority = instance.TrimEnd('/');

        // Built by hand rather than with the web stack's query helper. This project has no
        // dependency on ASP.NET and the worker references it too; one static method is not
        // worth giving a background process the web framework.
        var encoded = string.Join('&', query
            .Where(pair => pair.Value is not null)
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}"));

        return new Uri($"{authority}/{tenantId}/oauth2/v2.0/authorize?{encoded}");
    }

    /// <summary>Turns the code Entra sent back into a usable pair of tokens.</summary>
    /// <param name="http">The client to call the token endpoint with.</param>
    /// <param name="settings">Where and as whom.</param>
    /// <param name="code">The authorization code.</param>
    /// <param name="redirectUri">The same one the authorize call used, which Entra checks.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public static Task<TokenSet> RedeemCodeAsync(
        HttpClient http,
        TokenRequest settings,
        string code,
        string redirectUri,
        CancellationToken cancellationToken) =>
        PostAsync(http, settings, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri
        }, cancellationToken);

    /// <summary>
    /// Trades a stored refresh token for a fresh access token.
    /// </summary>
    /// <remarks>
    /// Entra rotates refresh tokens: the response carries a new one and the old one stops
    /// working. A caller that does not store what comes back gets exactly one more run out of
    /// this connection and then a failure that looks like a revoked consent.
    /// </remarks>
    /// <param name="http">The client to call the token endpoint with.</param>
    /// <param name="settings">Where and as whom.</param>
    /// <param name="refreshToken">What was stored last time.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public static Task<TokenSet> RefreshAsync(
        HttpClient http,
        TokenRequest settings,
        string refreshToken,
        CancellationToken cancellationToken) =>
        PostAsync(http, settings, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        }, cancellationToken);

    /// <summary>What every token call needs.</summary>
    /// <param name="Instance">The Entra instance.</param>
    /// <param name="TenantId">Which tenant.</param>
    /// <param name="ClientId">This product's app registration.</param>
    /// <param name="ClientSecret">Its secret. The product is a confidential client and Entra will refuse without it.</param>
    /// <param name="EnvironmentUrl">The normalised environment origin, which decides the scope.</param>
    public sealed record TokenRequest(
        string Instance,
        string TenantId,
        string ClientId,
        string ClientSecret,
        string EnvironmentUrl);

    private static async Task<TokenSet> PostAsync(
        HttpClient http,
        TokenRequest settings,
        Dictionary<string, string> grant,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(settings);

        grant["client_id"] = settings.ClientId;
        grant["client_secret"] = settings.ClientSecret;
        grant["scope"] = $"{settings.EnvironmentUrl}/user_impersonation offline_access";

        var authority = settings.Instance.TrimEnd('/');
        var endpoint = $"{authority}/{settings.TenantId}/oauth2/v2.0/token";

        using var content = new FormUrlEncodedContent(grant);
        using var response = await http.PostAsync(new Uri(endpoint), content, cancellationToken).ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // Entra's own description, not a status code. It says things like "the user or
            // administrator has not consented" and "AADSTS700082: the refresh token has
            // expired", each of which tells somebody exactly what to do next.
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
                $"Entra refused the token request ({(int)response.StatusCode}). {Describe(body)}"));
        }

        var token = System.Text.Json.JsonSerializer.Deserialize<TokenResponse>(body)
            ?? throw new InvalidOperationException("Entra returned a token response that could not be read.");

        if (string.IsNullOrWhiteSpace(token.AccessToken))
        {
            throw new InvalidOperationException("Entra returned no access token.");
        }

        return new TokenSet(
            token.AccessToken,
            token.RefreshToken,
            DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn <= 0 ? 3600 : token.ExpiresIn));
    }

    /// <summary>The human readable half of an Entra error, where there is one.</summary>
    /// <param name="body">The response body.</param>
    private static string Describe(string body)
    {
        try
        {
            var failure = System.Text.Json.JsonSerializer.Deserialize<TokenFailure>(body);

            if (!string.IsNullOrWhiteSpace(failure?.ErrorDescription))
            {
                // One line. The description carries a correlation id and a timestamp on their
                // own lines, which are useful in a log and noise in a message on a screen.
                return failure.ErrorDescription.Split('\n')[0].Trim();
            }

            if (!string.IsNullOrWhiteSpace(failure?.Error)) return failure.Error;
        }
        catch (System.Text.Json.JsonException)
        {
            // Not JSON. Fall through to the body itself, truncated.
        }

        return body.Length > 400 ? body[..400] : body;
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record TokenFailure(
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("error_description")] string? ErrorDescription);
}
