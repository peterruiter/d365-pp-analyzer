namespace PowerPete.Analyzer.Tests;

using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using PowerPete.Analyzer.Api;
using PowerPete.Analyzer.Data;
using Xunit;

/// <summary>
/// The half of the interactive sign-in that does not need Entra.
/// </summary>
/// <remarks>
/// The round trip itself cannot be tested here: it needs an app registration, a consultant
/// with an account in the client's tenant, and a browser. What can be tested is the part
/// that decides whether to believe what comes back, and that part is the one worth testing,
/// because a callback that accepts a state it did not issue is a connection somebody else
/// can point at an environment of their choosing.
///
/// The state is the only thing carrying the connection's identity across the round trip. It
/// goes out through the browser and comes back through the browser, so it is signed, it
/// expires, and neither of those had a test.
/// </remarks>
public class InteractiveSignInTests
{
    private static readonly Connection Example = new(
        Guid.Parse("11111111-1111-4111-8111-111111111111"),
        Guid.Parse("22222222-2222-4222-8222-222222222222"),
        "delegated",
        "Contoso production",
        "production",
        """{"environmentUrl":"https://contoso.crm4.dynamics.com"}""",
        null, null, null, null, null, null, null);

    private static InteractiveSignIn Build(IDataProtectionProvider? protection = null) =>

        // A store that is never reached. Every path below is refused at the state check,
        // which happens before anything looks at a connection, and a test that needed a
        // database to prove a signature would not be proving the signature.
        new(new WorkspaceStore("Server=unreachable;Database=none;"),
            new StubSecrets(),
            protection ?? DataProtectionProvider.Create(nameof(InteractiveSignInTests)),
            new InteractiveSignIn.Options("https://login.microsoftonline.com", "tenant", "client", "secret"),
            new HttpClient());

    [Fact]
    public void Carries_the_connection_across_the_round_trip_in_something_signed()
    {
        var url = Build().AuthorizeUrl(Example, "https://example.test/api/connections/callback");

        url.Should().NotBeNull("the connection has an environment on it");

        var state = System.Web.HttpUtility.ParseQueryString(url!.Query)["state"];

        state.Should().NotBeNullOrWhiteSpace();

        // The identifiers are what the callback acts on, so neither may be readable or
        // writable by whoever is holding the browser between the two calls.
        state.Should().NotContain(Example.ConnectionId.ToString(), "the state is protected, not encoded");
        state.Should().NotContain(Example.EngagementId.ToString(), "the state is protected, not encoded");
    }

    [Fact]
    public async Task Refuses_a_state_it_did_not_issue()
    {
        var url = Build().AuthorizeUrl(Example, "https://example.test/api/connections/callback");
        var state = System.Web.HttpUtility.ParseQueryString(url!.Query)["state"]!;

        // A different application name is a different key, which is what a second
        // deployment or a rotated key ring looks like from here.
        var somebodyElse = Build(DataProtectionProvider.Create("SomethingElse"));

        var outcome = await somebodyElse.CompleteAsync(
            state, "code", "https://example.test/api/connections/callback", CancellationToken.None);

        outcome.Should().BeNull("a state this deployment did not sign is not a state it acts on");
    }

    [Fact]
    public async Task Refuses_a_state_somebody_edited()
    {
        var url = Build().AuthorizeUrl(Example, "https://example.test/api/connections/callback");
        var state = System.Web.HttpUtility.ParseQueryString(url!.Query)["state"]!;

        // One character. The point is that the failure is a refusal rather than an
        // exception escaping into a 500 with a stack trace on it.
        var edited = state[..^2] + (state[^2] == 'A' ? 'B' : 'A') + state[^1];

        var outcome = await Build().CompleteAsync(
            edited, "code", "https://example.test/api/connections/callback", CancellationToken.None);

        outcome.Should().BeNull();
    }

    [Fact]
    public async Task Accepts_the_state_it_issued_itself()
    {
        // The control for the three refusals above. Each of them would also pass if
        // CompleteAsync simply always returned null, so one test has to show that a state
        // this deployment signed gets past the check rather than being refused with
        // everything else.
        //
        // Getting past it means looking the connection up, and the store points at a
        // server that is not there, so the proof is that it tries: a refusal returns null
        // without touching anything, and an acceptance fails reaching the database.
        var signIn = Build();
        var url = signIn.AuthorizeUrl(Example, "https://example.test/api/connections/callback");
        var state = System.Web.HttpUtility.ParseQueryString(url!.Query)["state"]!;

        var attempt = async () => await signIn.CompleteAsync(
            state, "code", "https://example.test/api/connections/callback", CancellationToken.None);

        await attempt.Should().ThrowAsync<Exception>(
            "a state it signed is unwrapped, and the next thing it does is read the connection");
    }

    [Fact]
    public void Will_not_start_without_an_environment_to_sign_in_to()
    {
        var empty = Example with { SettingsJson = "{}" };

        Build().AuthorizeUrl(empty, "https://example.test/api/connections/callback")
            .Should().BeNull("sending somebody to Entra for an environment nobody named wastes their sign-in");
    }

    [Fact]
    public void Asks_Entra_to_find_the_account_rather_than_guessing_the_tenant()
    {
        var url = Build().AuthorizeUrl(Example, "https://example.test/api/connections/callback");

        // The environment's tenant is not necessarily the one the product is registered in,
        // and a consultant is frequently a guest in the client's. Guessing produces an error
        // about an unknown user, which reads as the person's fault.
        url!.AbsoluteUri.Should().Contain("/organizations/", "the tenant is Entra's to work out");
    }

    private sealed class StubSecrets : ISecretStore
    {
        public bool IsConfigured => true;

        public Task<string> SetAsync(string name, string value, CancellationToken cancellationToken) =>
            Task.FromResult(name);

        public Task<string?> GetAsync(string reference, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public Task DeleteAsync(string reference, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
