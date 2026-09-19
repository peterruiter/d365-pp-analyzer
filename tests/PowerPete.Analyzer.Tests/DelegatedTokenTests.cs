namespace PowerPete.Analyzer.Tests;

using FluentAssertions;
using PowerPete.Analyzer.Dataverse;
using Xunit;

/// <summary>
/// The environment address somebody types, and the scope built from it.
/// </summary>
/// <remarks>
/// This is the one field in the interactive sign-in, and every wrong shape of it fails the
/// same way: Entra refuses the authorize call with a message about an unknown resource,
/// which tells a consultant nothing about the trailing slash they pasted.
/// </remarks>
public class DelegatedTokenTests
{
    [Theory]
    [InlineData("https://contoso.crm4.dynamics.com", "https://contoso.crm4.dynamics.com")]
    [InlineData("https://contoso.crm4.dynamics.com/", "https://contoso.crm4.dynamics.com")]
    [InlineData("  https://contoso.crm4.dynamics.com  ", "https://contoso.crm4.dynamics.com")]
    [InlineData("contoso.crm4.dynamics.com", "https://contoso.crm4.dynamics.com")]
    [InlineData("CONTOSO.crm4.dynamics.com", "https://contoso.crm4.dynamics.com")]
    public void Accepts_the_shapes_somebody_will_actually_paste(string typed, string expected)
    {
        DelegatedTokens.NormaliseEnvironment(typed).Should().Be(expected);
    }

    [Theory]
    // The maker portal, which is what is in the address bar when somebody is looking at the
    // environment and reaches for the URL.
    [InlineData("https://contoso.crm4.dynamics.com/main.aspx?appid=123", "https://contoso.crm4.dynamics.com")]
    [InlineData("https://contoso.crm4.dynamics.com/api/data/v9.2/WhoAmI", "https://contoso.crm4.dynamics.com")]
    public void Takes_the_origin_and_discards_the_path(string typed, string expected)
    {
        DelegatedTokens.NormaliseEnvironment(typed).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    // Refused rather than upgraded. A token scoped to a plaintext origin is a token sent
    // over plaintext, and quietly rewriting what somebody typed hides that they meant it.
    [InlineData("http://contoso.crm4.dynamics.com")]
    // No dot means a host name, not an environment, and it would produce a scope that looks
    // valid and resolves to nothing.
    [InlineData("localhost")]
    public void Refuses_what_cannot_be_an_environment(string? typed)
    {
        DelegatedTokens.NormaliseEnvironment(typed).Should().BeNull();
    }

    [Fact]
    public void Asks_for_the_environment_and_for_a_refresh_token()
    {
        var url = DelegatedTokens.AuthorizeUrl(
            DelegatedTokens.DefaultInstance,
            "organizations",
            "11111111-1111-1111-1111-111111111111",
            "https://product.example/api/connections/callback",
            "https://contoso.crm4.dynamics.com",
            "state-value").ToString();

        // The scope names the environment, which is what makes the consent screen say which
        // organisation is about to be read.
        url.Should().Contain(Uri.EscapeDataString("https://contoso.crm4.dynamics.com/user_impersonation"));

        // Without offline_access the sign-in works and the run an hour later does not.
        url.Should().Contain(Uri.EscapeDataString("offline_access"));

        url.Should().Contain("response_type=code");
        url.Should().Contain("state=state-value");

        // The account picker, every time. A consultant connecting a second client while
        // signed in as themselves must not silently reuse the browser's last account.
        url.Should().Contain("prompt=select_account");
    }

    [Fact]
    public void Signs_in_against_organizations_rather_than_one_tenant()
    {
        // The environment being read frequently belongs to the client's tenant, not to the
        // one this product is registered in, and the consultant may be a guest there.
        var url = DelegatedTokens.AuthorizeUrl(
            DelegatedTokens.DefaultInstance,
            "organizations",
            "11111111-1111-1111-1111-111111111111",
            "https://product.example/api/connections/callback",
            "https://contoso.crm4.dynamics.com",
            "state").ToString();

        url.Should().StartWith("https://login.microsoftonline.com/organizations/oauth2/v2.0/authorize?");
    }
}
