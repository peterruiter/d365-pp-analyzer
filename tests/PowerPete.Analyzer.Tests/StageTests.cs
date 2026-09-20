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

    /// <summary>Services with the callbacks under test and everything else stubbed.</summary>
    /// <param name="check">What the connections report.</param>
    /// <param name="list">What the environment holds.</param>
    /// <param name="selection">
    /// What somebody chose. Null is the default and the interesting one: it is what makes
    /// the select stage stop and ask rather than read everything.
    /// </param>
    private static StageServices Services(
        Func<CancellationToken, Task<IReadOnlyList<ConnectionCheck>>>? check = null,
        Func<CancellationToken, Task<IReadOnlyList<SolutionSummary>>>? list = null,
        ChosenScope? selection = null) =>
        new(
            OpenSolutionFiles: (_, _) => Task.FromResult<IReadOnlyList<SolutionFile>>([]),
            CheckConnections: check ?? (_ => Task.FromResult<IReadOnlyList<ConnectionCheck>>([])),
            ListSolutions: list ?? (_ => Task.FromResult<IReadOnlyList<SolutionSummary>>([])),
            ReadEnvironment: (_, _, _) => Task.FromResult<EnvironmentRead?>(null),
            RecordSolutions: (_, _, _) => Task.CompletedTask,
            ReadSelection: (_, _) => Task.FromResult(selection),
            RunChecker: (_, _) => Task.FromResult(new CheckerOutcome(false, [], null)),
            Estimator: null!,
            BacklogBuilder: null!,
            Publish: (_, _, _, _) => Task.FromResult(0),
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
        // Answered, because an unanswered run stops at this stage now. What is under test
        // here is the count of what exists, which has to be right whether or not anybody
        // chose all of it.
        var services = Services(
            list: _ => Task.FromResult<IReadOnlyList<SolutionSummary>>(
            [
                new SolutionSummary("a", "A", "1.0", false, "nwu", "Northwind", null),
                new SolutionSummary("b", "B", "1.0", false, "nwu", "Northwind", null),
                new SolutionSummary("c", "C", "2.0", true, "isv", "SmartPortal", null)
            ]),
            selection: Chose("a", "b", "c"));

        var state = State();
        var outcome = await new SelectSolutionsStage(services).RunAsync(state, null, CancellationToken.None);

        outcome.Status.Should().Be("succeeded");
        state.SolutionsTotal.Should().Be(3);
        state.Available.Should().HaveCount(3);
        state.Chosen.Should().HaveCount(3);
    }

    [Fact]
    public async Task SelectSolutions_says_so_when_everything_is_managed()
    {
        var services = Services(
            list: _ => Task.FromResult<IReadOnlyList<SolutionSummary>>(
                [new SolutionSummary("isv", "SmartPortal", "3.0", true, "isv", "SmartPortal BV", null)]),
            selection: Chose("isv"));

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

    [Fact]
    public async Task SelectSolutions_stops_and_asks_when_nobody_has_chosen()
    {
        // The gate. Everything after this stage reads the environment, and reading it before
        // anybody has said which solutions to read is the hour this exists to save.
        var services = Services(
            list: _ => Task.FromResult<IReadOnlyList<SolutionSummary>>(
            [
                new SolutionSummary("nwu_core", "Core", "1.0", false, "nwu", "Northwind", 140),
                new SolutionSummary("msdyn_Sales", "Sales", "9.0", true, "msdyn", "Microsoft Dynamics 365", 4000)
            ]),
            selection: null);

        var state = State();
        var outcome = await new SelectSolutionsStage(services).RunAsync(state, null, CancellationToken.None);

        outcome.Status.Should().Be("awaitingSelection");

        // The sentence a person reads. It has to say how many are theirs, because "19
        // solutions found" in an environment where 17 are Microsoft's reads as a lot of work
        // and is not.
        outcome.Error.Should().Contain("2 solutions found");
        outcome.Error.Should().Contain("1 of them not Microsoft's");

        // Recorded before the pause, so a run nobody ever resumes still says what was there.
        state.Available.Should().HaveCount(2);
        state.Chosen.Should().BeEmpty();
    }

    [Fact]
    public async Task SelectSolutions_says_an_estate_nobody_chose_is_unread_rather_than_clean()
    {
        // The single most dangerous report this product could produce is one that says a
        // client has no technical debt when the truth is that nobody read their environment.
        var services = Services(
            list: _ => Task.FromResult<IReadOnlyList<SolutionSummary>>(
                [new SolutionSummary("nwu_core", "Core", "1.0", false, "nwu", "Northwind", 140)]),
            selection: Chose());

        var outcome = await new SelectSolutionsStage(services)
            .RunAsync(State(), null, CancellationToken.None);

        outcome.Status.Should().Be("partial");
        outcome.Error.Should().Contain("unread estate rather than a clean one");
    }

    [Fact]
    public async Task SelectSolutions_carries_the_chosen_checks_onto_the_run()
    {
        // Resolved against the mode rather than stored resolved, so a mode whose default
        // changes does not keep applying the old one to a run that never had an opinion.
        var services = Services(
            list: _ => Task.FromResult<IReadOnlyList<SolutionSummary>>(
                [new SolutionSummary("nwu_core", "Core", "1.0", false, "nwu", "Northwind", 140)]),
            selection: new ChosenScope(
                ["nwu_core"],
                SolutionChecker: false,
                ModelEstimates: null,
                EnvironmentHealth: null,
                ExportSolutions: null));

        var state = State();
        await new SelectSolutionsStage(services).RunAsync(state, null, CancellationToken.None);

        state.Checks.SolutionChecker.Should().BeFalse("it was turned off on the picker");
        state.Checks.ModelEstimates.Should().BeTrue("nobody said otherwise, and an assessment estimates with a model");
    }

    [Fact]
    public void A_quick_scan_runs_neither_the_checker_nor_the_model()
    {
        // The two things that make an assessment slow are the two a quick scan does without,
        // which is what makes it the mode for the meeting where somebody asks how bad it is.
        var quick = RunChecks.ForMode("quickScan");

        quick.SolutionChecker.Should().BeFalse();
        quick.ModelEstimates.Should().BeFalse();
        quick.EnvironmentHealth.Should().BeTrue();

        var assessment = RunChecks.ForMode("assessment");

        assessment.SolutionChecker.Should().BeTrue();
        assessment.ModelEstimates.Should().BeTrue();
    }

    [Fact]
    public async Task The_checker_reports_not_assessed_rather_than_passing_when_it_is_turned_off()
    {
        // Turned off is not the same as clean. Every rule whose evidence is a checker result
        // has to say it was not assessed, or turning the slowest check off quietly improves
        // the client's score.
        var state = State();
        state.Checks = state.Checks with { SolutionChecker = false };

        var outcome = await new CheckerStage(Services()).RunAsync(state, null, CancellationToken.None);

        outcome.Status.Should().Be("partial");
        outcome.Error.Should().Contain("not assessed");
    }

    [Fact]
    public async Task SelectSolutions_does_not_lose_the_answer_when_the_run_resumes()
    {
        // What actually happened on the first real environment.
        //
        // Eleven solutions were ticked out of 959. The selection was recorded, the run
        // resumed, and this stage listed the environment again and re-recorded it, which
        // set every tick back to unanswered. The stage then read an empty selection, an
        // empty selection means no scope, and the run read all 959 solutions and reported
        // that none had been chosen.
        //
        // The fix is in the store, which merges and never writes the tick. What this holds
        // is the property that matters to the stage: recording the list again must not be
        // able to change what somebody chose.
        var recorded = new List<string>();
        var chosen = Chose("nwu_core");

        var services = Services(
            list: _ => Task.FromResult<IReadOnlyList<SolutionSummary>>(
            [
                new SolutionSummary("nwu_core", "Core", "1.0", false, "nwu", "Northwind", 140),
                new SolutionSummary("msdyn_Sales", "Sales", "9.0", true, "msdyn", "Microsoft", 4000)
            ]),
            selection: chosen);

        // The stage records the list and then reads the answer. Whatever order those happen
        // in, the answer has to come back as it was given.
        var watched = services with
        {
            RecordSolutions = (_, solutions, _) =>
            {
                recorded.AddRange(solutions.Select(solution => solution.UniqueName));
                return Task.CompletedTask;
            }
        };

        var state = State();
        var outcome = await new SelectSolutionsStage(watched).RunAsync(state, null, CancellationToken.None);

        recorded.Should().HaveCount(2, "the whole environment is recorded, chosen or not");

        outcome.Status.Should().NotBe(
            "awaitingSelection",
            "the question was already answered, so resuming must not ask it again");

        state.Chosen.Should().Equal(
            ["nwu_core"],
            "the tick survives the list being recorded again, which is what a resume does");
    }

    [Fact]
    public async Task SelectSolutions_adds_nothing_the_worker_already_seeded()
    {
        // The worker puts the selection into the state before the pipeline starts, because a
        // resumed run skips this stage and extract needs the scope regardless. On a first
        // pass this stage runs as well, and both writing the same names would double the
        // scope and the count this stage reports back.
        //
        // The bug that made the seeding necessary: RunState is built fresh on every pass and
        // only the stages that actually run fill it in, so a retry from extract left Chosen
        // empty, an empty scope means no scope, and a run of eleven solutions read 92,059
        // components and reported findings against Microsoft's managed ones.
        var services = Services(
            list: _ => Task.FromResult<IReadOnlyList<SolutionSummary>>(
                [new SolutionSummary("nwu_core", "Core", "1.0", false, "nwu", "Northwind", 140)]),
            selection: Chose("nwu_core"));

        var state = State();
        state.Chosen.Add("nwu_core");

        await new SelectSolutionsStage(services).RunAsync(state, null, CancellationToken.None);

        state.Chosen.Should().Equal(["nwu_core"], "the worker had already seeded it");
    }

    /// <summary>An answer to the picker, with the mode's defaults left alone.</summary>
    /// <param name="solutions">What was ticked.</param>
    private static ChosenScope Chose(params string[] solutions) =>
        new(solutions, SolutionChecker: null, ModelEstimates: null, EnvironmentHealth: null, ExportSolutions: null);
}
