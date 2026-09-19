namespace PowerPete.Analyzer.Tests;

using FluentAssertions;
using PowerPete.Analyzer.Analysis;
using PowerPete.Analyzer.Domain;
using PowerPete.Analyzer.Pipeline;
using PowerPete.Analyzer.Pipeline.Stages;
using Xunit;

/// <summary>
/// The two stages the contract declared and nothing implemented.
/// </summary>
/// <remarks>
/// Neither can be exercised by running the product without a client environment to point it
/// at, which is exactly why they went unwritten for so long. Both take everything they need
/// through a delegate, so both can be driven from here.
/// </remarks>
public class StageTests
{
    private static RunState State() => new()
    {
        RunId = Guid.NewGuid(),
        EngagementId = Guid.NewGuid(),
        Mode = "assessment"
    };

    /// <summary>Services with the two callbacks under test and everything else stubbed.</summary>
    private static StageServices Services(
        Func<CancellationToken, Task<IReadOnlyList<ConnectionCheck>>>? check = null,
        Func<CancellationToken, Task<IReadOnlyList<SolutionSummary>>>? list = null) =>
        new(
            OpenSolutionFile: _ => Task.FromResult<Stream?>(null),
            CheckConnections: check ?? (_ => Task.FromResult<IReadOnlyList<ConnectionCheck>>([])),
            ListSolutions: list ?? (_ => Task.FromResult<IReadOnlyList<SolutionSummary>>([])),
            ReadEnvironment: (_, _) => Task.FromResult<EnvironmentRead?>(null),
            RunChecker: (_, _) => Task.FromResult(new CheckerOutcome(false, [], null)),
            Estimator: null!,
            BacklogBuilder: null!,
            Publish: (_, _, _) => Task.FromResult(0),
            Persist: null!,
            FixedCosts: [],
            Bands: EstimateCatalogue.Bands,
            ComplexityRules: [],
            RoadmapPositions: new Dictionary<string, RoadmapPosition>(StringComparer.Ordinal));

    private static ConnectionCheck Working(string name, params EvidenceSource[] reaches) =>
        new(name, "servicePrincipal", true, $"{name}@example.com", "Authenticated.", reaches);

    private static ConnectionCheck Broken(string name, string why) =>
        new(name, "servicePrincipal", false, null, why, []);

    [Fact]
    public async Task Connect_refuses_an_engagement_with_no_source()
    {
        var outcome = await new ConnectStage(Services()).RunAsync(State(), null, CancellationToken.None);

        // Fatal rather than partial. There is nothing to read, so every stage after this
        // would fail too, and failing here says why.
        outcome.Status.Should().Be("failed");
        outcome.Error.Should().Contain("No source is configured");
    }

    [Fact]
    public async Task Connect_fails_when_every_source_is_unreachable_and_names_each()
    {
        var services = Services(check: _ => Task.FromResult<IReadOnlyList<ConnectionCheck>>(
        [
            Broken("Production", "The secret has expired."),
            Broken("Acceptance", "The application user has no security role.")
        ]));

        var outcome = await new ConnectStage(services).RunAsync(State(), null, CancellationToken.None);

        outcome.Status.Should().Be("failed");

        // Both named. An engagement can carry several connections and "the connection
        // failed" does not say which one to go and fix.
        outcome.Error.Should().Contain("Production").And.Contain("The secret has expired.");
        outcome.Error.Should().Contain("Acceptance").And.Contain("no security role");
    }

    [Fact]
    public async Task Connect_carries_on_when_one_of_two_works()
    {
        var services = Services(check: _ => Task.FromResult<IReadOnlyList<ConnectionCheck>>(
        [
            Working("Production", EvidenceSource.Metadata),
            Broken("Acceptance", "Refused.")
        ]));

        var state = State();
        var outcome = await new ConnectStage(services).RunAsync(state, null, CancellationToken.None);

        outcome.Status.Should().Be("partial");
        state.Reachable.Should().Contain(EvidenceSource.Metadata);
    }

    [Fact]
    public async Task Connect_records_who_it_authenticated_as()
    {
        // A run made as a system administrator is not evidence that a least privileged
        // integration could have made it, and the report can only say so if this is kept.
        var services = Services(check: _ => Task.FromResult<IReadOnlyList<ConnectionCheck>>(
            [Working("Production", EvidenceSource.Metadata, EvidenceSource.Checker)]));

        var state = State();
        await new ConnectStage(services).RunAsync(state, null, CancellationToken.None);

        state.Identities.Should().ContainKey("Production");
        state.Identities["Production"].Should().Be("Production@example.com");
        state.Reachable.Should().BeEquivalentTo([EvidenceSource.Metadata, EvidenceSource.Checker]);
    }

    [Fact]
    public async Task SelectSolutions_counts_what_exists_rather_than_what_was_read()
    {
        // The whole point of the stage. Until it existed the total was whatever the
        // extraction happened to read, so it always equalled the analysed count and the
        // caveat about partial coverage could never fire.
        var services = Services(list: _ => Task.FromResult<IReadOnlyList<SolutionSummary>>(
        [
            new SolutionSummary("a", "A", "1.0", false, "nwu", "Northwind", null),
            new SolutionSummary("b", "B", "1.0", false, "nwu", "Northwind", null),
            new SolutionSummary("c", "C", "2.0", true, "isv", "SmartPortal", null)
        ]));

        var state = State();
        var outcome = await new SelectSolutionsStage(services).RunAsync(state, null, CancellationToken.None);

        outcome.Status.Should().Be("succeeded");
        state.SolutionsTotal.Should().Be(3);
        state.Available.Should().HaveCount(3);
    }

    [Fact]
    public async Task SelectSolutions_says_so_when_everything_is_managed()
    {
        var services = Services(list: _ => Task.FromResult<IReadOnlyList<SolutionSummary>>(
            [new SolutionSummary("isv", "SmartPortal", "3.0", true, "isv", "SmartPortal BV", null)]));

        var outcome = await new SelectSolutionsStage(services)
            .RunAsync(State(), null, CancellationToken.None);

        // Partial rather than failed. It might be the wrong environment, and it might be a
        // client who genuinely ships everything managed, and the product cannot tell.
        outcome.Status.Should().Be("partial");
        outcome.Error.Should().Contain("managed");
    }

    [Fact]
    public async Task SelectSolutions_passes_an_offline_run_through()
    {
        // No environment to enumerate. The extract stage takes the solutions out of the file
        // it was handed, and failing here would stop the one mode that needs no credential.
        var state = State();
        var outcome = await new SelectSolutionsStage(Services())
            .RunAsync(state, null, CancellationToken.None);

        outcome.Status.Should().Be("succeeded");
        state.SolutionsTotal.Should().Be(0);
    }
}
