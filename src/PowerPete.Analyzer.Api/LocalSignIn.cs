namespace PowerPete.Analyzer.Api;

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

/// <summary>
/// Signs everybody in as one local person, when there is no Entra to sign in against.
/// </summary>
/// <remarks>
/// The same rule the sibling products use: Entra is configured or it is not, and where it is
/// not, the thing running is somebody's laptop. The deployment always supplies a tenant and a
/// client id, so this cannot engage in a container, and the pipeline that publishes one would
/// have to be changed for it to.
///
/// It exists because the alternative is that nobody can run the screens at all. Every endpoint
/// in this product requires authorisation, the tenant that owns the registration is not the
/// tenant most machines are joined to, and the result was a product whose entire authenticated
/// surface could only be exercised by deploying it.
///
/// The identity it issues is the configured initial global administrator, so the access layer
/// needs no special case: the same mechanism that admits the first real person on a fresh
/// deployment admits this one, through the same code path, with the same database rows. A
/// local run that granted itself access some other way would be exercising a pipeline that
/// does not exist anywhere else.
/// </remarks>
/// <param name="options">Scheme options.</param>
/// <param name="logger">Logging.</param>
/// <param name="encoder">URL encoding.</param>
/// <param name="who">Who to sign in as.</param>
public sealed class LocalSignInHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    LocalSignInHandler.Identity who)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>The name of the scheme, used where a scheme has to be named.</summary>
    public const string SchemeName = "LocalDevelopment";

    /// <summary>Who a local run is signed in as.</summary>
    /// <param name="UserId">The user principal name, which the access layer keys on.</param>
    /// <param name="DisplayName">What the header shows.</param>
    public sealed record Identity(string UserId, string DisplayName);

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // preferred_username, because that is the claim the product reads. Issuing a
        // different one would authenticate somebody the access layer has never heard of,
        // and every screen would render empty rather than failing in a way anybody could
        // diagnose.
        var identity = new ClaimsIdentity(
            [
                new Claim("preferred_username", who.UserId),
                new Claim(ClaimTypes.Upn, who.UserId),
                new Claim("name", who.DisplayName),
                new Claim(ClaimTypes.Name, who.DisplayName)
            ],
            SchemeName);

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
