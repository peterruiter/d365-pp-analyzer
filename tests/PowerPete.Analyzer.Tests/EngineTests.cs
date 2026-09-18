namespace PowerPete.Analyzer.Tests;

using FluentAssertions;
using PowerPete.Analyzer.Analysis;
using Handlers = PowerPete.Analyzer.Analysis.Handlers;
using PowerPete.Analyzer.DevOps;
using PowerPete.Analyzer.Domain;
using PowerPete.Analyzer.Estimation;
using PowerPete.Analyzer.Pipeline;
using Xunit;

/// <summary>Builders, so a test says what it is about rather than what it had to construct.</summary>
internal static class Build
{
    public static DiscoveredComponent Component(
        string typeId,
        string name = "Thing",
        bool managed = false,
        params (string Key, object? Value)[] attributes) =>
        new(Guid.NewGuid(),
            StableKeys.ForComponent(typeId, null, name),
            typeId,
            name,
            name,
            null,
            "TestSolution",
            managed,
            null,
            attributes.ToDictionary(pair => pair.Key, pair => pair.Value));

    public static AnalysisContext Context(
        IEnumerable<DiscoveredComponent>? components = null,
        IEnumerable<ComponentLink>? links = null,
        IEnumerable<EvidenceSource>? reach = null,
        string environmentRole = "unknown") =>
        new([.. components ?? []],
            [.. links ?? []],
            [],
            new Reach(reach ?? [EvidenceSource.Metadata, EvidenceSource.SolutionZip, EvidenceSource.Checker, EvidenceSource.Runtime]),
            environmentRole);

    public static Estimate Estimate(decimal low = 1, decimal high = 4, EstimateLayer layer = EstimateLayer.BandDefault) =>
        Domain.Estimate.Create(low, high, 3, Confidence.Medium, layer, "Because.");
}

/// <summary>
/// The behaviour that stops a partial run looking like a clean estate.
/// </summary>
public sealed class ReachTests
{
    [Fact]
    public void A_rule_needing_runtime_evidence_cannot_run_without_it()
    {
        var reach = new Reach([EvidenceSource.SolutionZip]);
        var rule = RuleCatalogue.Find("quality.flowFailureRate")!;

        reach.CanRun(rule, out var missing).Should().BeFalse();
        missing.Should().Contain("Runtime");
    }

    [Fact]
    public void A_rule_naming_several_sources_needs_only_one_of_them()
    {
        // Most rules name both metadata and solutionZip and work from either. Demanding both
        // would make the offline mode useless for two thirds of the catalogue.
        var reach = new Reach([EvidenceSource.SolutionZip]);
        var rule = RuleCatalogue.Find("lifecycle.dialogPresent")!;

        reach.CanRun(rule, out _).Should().BeTrue();
    }

    [Fact]
    public void A_rule_that_cannot_run_is_reported_as_not_assessed_rather_than_passing()
    {
        var engine = new RuleEngine([new Handlers.DialogPresentHandler()]);
        var context = Build.Context(reach: [EvidenceSource.SolutionZip]);

        var outcome = engine.Run(context);

        outcome.NotAssessed.Should().Contain(entry => entry.RuleId == "quality.flowFailureRate");
        outcome.Findings.Should().BeEmpty();
    }

    [Fact]
    public void A_rule_with_no_handler_is_not_assessed_rather_than_absent()
    {
        var engine = new RuleEngine([new Handlers.DialogPresentHandler()]);

        var outcome = engine.Run(Build.Context());

        outcome.NotAssessed.Should().HaveCountGreaterThan(0);
        outcome.NotAssessed.Select(entry => entry.RuleId).Should().NotContain("lifecycle.dialogPresent");
        (outcome.Ran + outcome.NotAssessed.Count).Should().Be(RuleCatalogue.All.Count,
            "every rule in the catalogue accounts for itself, one way or the other");
    }
}

/// <summary>The two rules that are enforced rather than remembered.</summary>
public sealed class EstimateTests
{
    [Fact]
    public void An_estimate_cannot_be_created_without_a_rationale()
    {
        var create = () => Estimate.Create(1, 4, 3, Confidence.High, EstimateLayer.Model, "   ");

        create.Should().Throw<ArgumentException>()
            .WithMessage("*rationale*");
    }

    [Fact]
    public void An_estimate_whose_low_is_above_its_high_is_refused()
    {
        var create = () => Estimate.Create(10, 2, 3, Confidence.High, EstimateLayer.Model, "Because.");

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Story_points_stay_on_the_scale()
    {
        var create = () => Estimate.Create(1, 4, 4, Confidence.High, EstimateLayer.Model, "Because.");

        create.Should().Throw<ArgumentException>();
    }
}

/// <summary>Layer precedence and the guards around the model.</summary>
public sealed class EstimatorTests
{
    private static readonly Dictionary<string, EstimateBand> Bands = new(StringComparer.Ordinal)
    {
        ["trivial"] = new("trivial", 0.25m, 1, "A deletion."),
        ["small"] = new("small", 1, 4, "One component in a sitting."),
        ["medium"] = new("medium", 4, 16, "Understood first, then changed."),
        ["large"] = new("large", 16, 60, "A rebuild."),
        ["none"] = new("none", 0, 0, "Informational.")
    };

    private sealed class Fake(ModelEstimate? answer) : IEstimateModel
    {
        public string Name => "fake";

        public Task<ModelEstimate?> EstimateAsync(string prompt, CancellationToken cancellationToken) =>
            Task.FromResult(answer);
    }

    private static Finding AFinding(string ruleId = "quality.flowSize") =>
        Finding.From(RuleCatalogue.Find(ruleId)!, Build.Component("cloudFlow"),
            new Dictionary<string, object?> { ["actions"] = 60 });

    [Fact]
    public async Task An_override_beats_the_model()
    {
        var finding = AFinding();
        var overrides = new List<Override>
        {
            new("finding", null, null, finding.StableKey, 2, 3, 2, "We have done this one before.", "peter")
        };

        var estimator = new Estimator(Bands, overrides,
            new Fake(new ModelEstimate(40, 80, 13, "high", "The model's view.", [])));

        var result = await estimator.EstimateAsync(finding, null);

        result.Estimate.Layer.Should().Be(EstimateLayer.EngagementOverride);
        result.Estimate.LowHours.Should().Be(2);
        result.Provenance.Should().BeNull("no model was called, so there is nothing to record");
    }

    [Fact]
    public async Task The_narrowest_override_wins()
    {
        var finding = AFinding();
        var component = Build.Component("cloudFlow");

        var overrides = new List<Override>
        {
            new("rule", "quality.flowSize", null, null, 20, 30, 8, "Rule wide.", "peter"),
            new("finding", null, null, finding.StableKey, 2, 3, 2, "This one.", "peter")
        };

        var result = await new Estimator(Bands, overrides).EstimateAsync(finding, component);

        result.Estimate.Rationale.Should().Be("This one.");
    }

    [Fact]
    public async Task A_range_the_model_cannot_defend_falls_back_to_the_band()
    {
        // High over low of more than eight is the model saying it does not know, in a format
        // that looks like an answer.
        var estimator = new Estimator(Bands, [], new Fake(new ModelEstimate(1, 200, 8, "high", "Anything.", [])));

        var result = await estimator.EstimateAsync(AFinding(), null);

        result.Estimate.Layer.Should().Be(EstimateLayer.BandDefault);
        result.Estimate.FlaggedReason.Should().Contain("rejected");
        result.Provenance!.Accepted.Should().BeFalse();
    }

    [Fact]
    public async Task An_answer_with_no_rationale_is_rejected()
    {
        var estimator = new Estimator(Bands, [], new Fake(new ModelEstimate(4, 8, 5, "high", "  ", [])));

        var result = await estimator.EstimateAsync(AFinding(), null);

        result.Estimate.Layer.Should().Be(EstimateLayer.BandDefault);
        result.Provenance!.RejectionReason.Should().Contain("rationale");
    }

    [Fact]
    public async Task An_answer_well_outside_the_band_is_flagged_rather_than_discarded()
    {
        // A finding genuinely can be four times its band. A consultant reading the flag beats
        // the product silently overruling a number somebody will have to defend.
        var estimator = new Estimator(Bands, [], new Fake(new ModelEstimate(30, 90, 13, "medium", "It is a big one.", [])));

        var result = await estimator.EstimateAsync(AFinding(), null);

        result.Estimate.Layer.Should().Be(EstimateLayer.Model);
        result.Estimate.FlaggedReason.Should().NotBeNull();
    }

    [Fact]
    public async Task With_no_model_configured_every_finding_gets_its_band()
    {
        var result = await new Estimator(Bands, []).EstimateAsync(AFinding(), null);

        result.Estimate.Layer.Should().Be(EstimateLayer.BandDefault);
        result.Estimate.Confidence.Should().Be(Confidence.Low);
        result.Estimate.Rationale.Should().NotBeNullOrWhiteSpace();
    }
}

/// <summary>What the handlers do and, more importantly, what they refuse to do.</summary>
public sealed class HandlerTests
{
    [Fact]
    public void A_flow_whose_definition_was_never_read_produces_no_error_handling_finding()
    {
        // The alternative is telling a client forty flows have no error handling when nobody
        // opened one of them, which is a finding they disprove in front of you.
        var context = Build.Context([Build.Component("cloudFlow", "Unread")]);

        new Handlers.FlowNoErrorHandlingHandler().Run(context).Should().BeEmpty();
    }

    [Fact]
    public void A_flow_with_actions_and_no_failure_path_is_found()
    {
        var context = Build.Context([
            Build.Component("cloudFlow", "Big", false, ("actionCount", 12), ("hasErrorHandling", false))
        ]);

        new Handlers.FlowNoErrorHandlingHandler().Run(context).Should().ContainSingle();
    }

    [Fact]
    public void A_draft_classic_workflow_is_not_reported_as_carrying_live_logic()
    {
        var context = Build.Context([
            Build.Component("classicWorkflowBackground", "Draft", false, ("statecode", "0"))
        ]);

        new Handlers.ClassicWorkflowInUseHandler().Run(context).Should().BeEmpty();
    }

    [Fact]
    public void Unmanaged_in_production_does_not_fire_against_an_environment_nobody_classified()
    {
        // Guessing production from an environment name would put a critical finding on half
        // the sandboxes in the world, and a report whose top finding is wrong gets put down.
        var components = new[] { Build.Component("table", "Account") };

        new Handlers.UnmanagedInProductionHandler().Run(Build.Context(components)).Should().BeEmpty();
        new Handlers.UnmanagedInProductionHandler().Run(Build.Context(components, environmentRole: "production"))
            .Should().ContainSingle();
    }

    [Fact]
    public void A_security_role_with_no_assignment_data_is_not_called_unassigned()
    {
        var context = Build.Context([Build.Component("securityRole", "Custom Role")]);

        new Handlers.SecurityRoleSprawlHandler().Run(context).Should().BeEmpty(
            "nobody read the assignments, so there is no evidence either way");
    }

    [Fact]
    public void A_column_referenced_only_by_its_table_is_orphaned_and_the_finding_says_what_it_cannot_see()
    {
        var table = Build.Component("table", "account");
        var column = Build.Component("column", "account.new_thing", false, ("table", "account"), ("isCustom", true));
        var link = new ComponentLink(column.StableKey, table.StableKey, "belongsTo");

        var findings = new Handlers.OrphanedColumnHandler().Run(Build.Context([table, column], [link])).ToList();

        findings.Should().ContainSingle();
        findings[0].Evidence.Should().ContainKey("whatThisCannotSee");
    }

    [Fact]
    public void A_finding_cannot_be_created_without_evidence()
    {
        var create = () => Finding.From(RuleCatalogue.All[0], null, new Dictionary<string, object?>());

        create.Should().Throw<ArgumentException>().WithMessage("*evidence*");
    }
}

/// <summary>The backlog, and the two places it refuses rather than degrading.</summary>
public sealed class BacklogTests
{
    private static readonly Dictionary<string, Criterion> OneCriterion = new(StringComparer.Ordinal)
    {
        ["quality.flowSize"] = new("The flow {componentName}", "It has been split", "No flow exceeds forty actions", "Run the chain end to end.")
    };

    private static (Finding, Estimate, DiscoveredComponent?) Entry(string ruleId = "quality.flowSize")
    {
        var component = Build.Component("cloudFlow", "Big Flow");
        var finding = Finding.From(RuleCatalogue.Find(ruleId)!, component,
            new Dictionary<string, object?> { ["actions"] = 60 });

        return (finding, Build.Estimate(), component);
    }

    [Fact]
    public void A_rule_with_no_acceptance_criterion_refuses_to_build_rather_than_publishing_boilerplate()
    {
        var builder = new BacklogBuilder(Guid.NewGuid(), "Client", new Dictionary<string, Criterion>());

        var build = () => builder.Build([Entry()], "Run 1");

        build.Should().Throw<InvalidOperationException>().WithMessage("*acceptance criterion*");
    }

    [Fact]
    public void The_component_name_is_substituted_into_the_criterion()
    {
        var builder = new BacklogBuilder(Guid.NewGuid(), "Client", OneCriterion);

        var items = builder.Build([Entry()], "Run 1");

        items.Should().Contain(item => item.AcceptanceCriteria.Contains("Big Flow", StringComparison.Ordinal));
        items.Should().NotContain(item => item.AcceptanceCriteria.Contains("{componentName}", StringComparison.Ordinal));
    }

    [Fact]
    public void The_key_is_stable_across_two_builds_of_the_same_finding()
    {
        // What makes a second publish update rather than duplicate. If this test ever goes
        // red, a re-publish silently doubles somebody's backlog.
        var engagement = Guid.NewGuid();
        var entry = Entry();

        var first = new BacklogBuilder(engagement, "Client", OneCriterion).Build([entry], "Run 1");
        var second = new BacklogBuilder(engagement, "Client", OneCriterion).Build([entry], "Run 2");

        first.Select(item => item.Key).Should().Equal(second.Select(item => item.Key));
    }

    [Fact]
    public void Every_item_carries_the_estimate_rationale_into_its_description()
    {
        var items = new BacklogBuilder(Guid.NewGuid(), "Client", OneCriterion).Build([Entry()], "Run 1");

        items.Where(item => item.Type is not "epic" and not "feature")
            .Should().OnlyContain(item => item.DescriptionHtml.Contains("Because.", StringComparison.Ordinal));
    }
}

/// <summary>The ratio, and the caveats that stop it being read as a clean bill of health.</summary>
public sealed class ScorerTests
{
    [Fact]
    public void Configuration_is_counted_and_kept_out_of_the_ratio()
    {
        var components = Enumerable.Range(0, 100).Select(index => Build.Component("column", $"c{index}"))
            .Append(Build.Component("cloudFlow", "Flow"))
            .Append(Build.Component("pluginAssembly", "Plugin"))
            .ToList();

        var score = Scorer.Score(components, [], [], [], 1, 1);

        score.LowCodeShare.Should().Be(0.5m, "one flow and one plugin are counted, a hundred columns are not");
        score.ByCraft["config"].Should().Be(100);
    }

    [Fact]
    public void The_ratio_definition_travels_with_the_number()
    {
        var score = Scorer.Score([Build.Component("cloudFlow")], [], [], [], 1, 1);

        score.RatioDefinition.Should().NotBeNullOrWhiteSpace(
            "the number gets quoted in a room without the sentence that defines it");
    }

    [Fact]
    public void Unassessed_rules_produce_a_caveat_at_the_top_of_the_report()
    {
        var score = Scorer.Score(
            [Build.Component("cloudFlow")],
            [],
            [new NotAssessed("quality.flowFailureRate", "No runtime evidence.", "Runtime")],
            [],
            1,
            1);

        score.Caveats.Should().Contain(caveat => caveat.Contains("could not run", StringComparison.Ordinal));
    }

    [Fact]
    public void Analysing_a_subset_of_solutions_produces_a_caveat()
    {
        var score = Scorer.Score([Build.Component("cloudFlow")], [], [], [], 4, 19);

        score.Caveats.Should().Contain(caveat => caveat.Contains("4 of 19", StringComparison.Ordinal));
    }

    [Fact]
    public void Fixed_costs_are_reported_separately_from_the_finding_total()
    {
        var findings = new List<(Finding, Estimate)>
        {
            (Finding.From(RuleCatalogue.Find("quality.flowSize")!, Build.Component("cloudFlow"),
                new Dictionary<string, object?> { ["actions"] = 60 }), Build.Estimate(4, 8))
        };

        var score = Scorer.Score([], findings, [], [new FixedCost("access", "Access", 4, 24)], 1, 1);

        score.TotalLowHours.Should().Be(4);
        score.FixedCostLowHours.Should().Be(4);
        score.FixedCostHighHours.Should().Be(24);
    }
}

/// <summary>The two chart dimensions, and the honesty rule inside each.</summary>
public sealed class ComplexityAndRoadmapTests
{
    private static ComplexityRater Rater() => new(
    [
        new ComplexityRule("cloudFlow", "actionCount",
            [new ComplexityBand(10, Complexity.Simple), new ComplexityBand(40, Complexity.Medium), new ComplexityBand(null, Complexity.Complex)]),
        new ComplexityRule("report", null, [new ComplexityBand(null, Complexity.Complex)])
    ]);

    [Fact]
    public void A_component_whose_measure_is_missing_is_unrated_rather_than_simple()
    {
        // The whole reason Unrated exists. Four hundred simple components plus forty unread
        // ones is a different chart from four hundred and forty simple ones, and the second
        // one is the one that gets quoted.
        Rater().Rate(Build.Component("cloudFlow", "Unread")).Should().Be(Complexity.Unrated);
    }

    [Fact]
    public void A_component_type_with_no_rule_is_simple()
    {
        Rater().Rate(Build.Component("table", "Account")).Should().Be(Complexity.Simple);
    }

    [Fact]
    public void A_flat_rule_needs_no_measure()
    {
        Rater().Rate(Build.Component("report", "Quarterly")).Should().Be(Complexity.Complex);
    }

    [Fact]
    public void The_measured_bands_apply_in_order()
    {
        Rater().Rate(Build.Component("cloudFlow", "Small", false, ("actionCount", 4))).Should().Be(Complexity.Simple);
        Rater().Rate(Build.Component("cloudFlow", "Mid", false, ("actionCount", 25))).Should().Be(Complexity.Medium);
        Rater().Rate(Build.Component("cloudFlow", "Big", false, ("actionCount", 90))).Should().Be(Complexity.Complex);
    }

    [Fact]
    public void Unrated_is_its_own_column_in_the_chart()
    {
        var rows = Rater().ByCustomisation(
        [
            Build.Component("cloudFlow", "A", false, ("actionCount", 4)),
            Build.Component("cloudFlow", "B")
        ]);

        var flows = rows.Single();
        flows.Simple.Should().Be(1);
        flows.Unrated.Should().Be(1);
        flows.Total.Should().Be(2);
    }

    [Fact]
    public void Every_rule_in_the_catalogue_can_be_placed_on_the_roadmap()
    {
        var positions = Contracts.Read("rule-catalogue").Array("rules")
            .ToDictionary(
                rule => rule.Str("id"),
                rule => new RoadmapPosition(
                    rule.GetProperty("roadmap").Str("row"),
                    rule.GetProperty("roadmap").Str("column"),
                    rule.GetProperty("roadmap").Str("band")),
                StringComparer.Ordinal);

        var findings = RuleCatalogue.All
            .Select(rule => (
                Finding.From(rule, null, new Dictionary<string, object?> { ["x"] = 1 }),
                Build.Estimate()))
            .ToList();

        var result = new RoadmapBuilder(positions).Build(findings);

        result.Unplaced.Should().BeEmpty("a finding with nowhere to go disappears from the one slide a client keeps");
        result.Items.Should().HaveCount(RuleCatalogue.All.Count);
    }

    [Fact]
    public void Most_findings_land_in_unclutter_and_the_profile_says_so()
    {
        // Not an assertion about this estate. It is an assertion about the shape of a technical
        // debt assessment, and a roadmap where everything lands in innovate has been drawn to
        // please somebody.
        var bands = Contracts.Read("rule-catalogue").Array("rules")
            .Select(rule => rule.GetProperty("roadmap").Str("band"))
            .ToList();

        bands.Should().NotContain("innovate",
            "an analyser finds debt, not new capability, and a rule claiming otherwise is selling");
    }
}

/// <summary>The pipeline, and the three things that make it more than a for loop.</summary>
public sealed class PipelineTests
{
    private sealed class Journal : IRunJournal
    {
        public Dictionary<string, string> Stages { get; } = new(StringComparer.Ordinal);
        public List<string> RunStatuses { get; } = [];
        public Dictionary<string, string?> Completed { get; } = new(StringComparer.Ordinal);

        public Task SetRunStatusAsync(Guid runId, string status, string? failure, CancellationToken cancellationToken)
        {
            RunStatuses.Add(status);
            return Task.CompletedTask;
        }

        public Task SetStageAsync(Guid runId, string stageId, string status, string? failure, string? checkpoint, CancellationToken cancellationToken)
        {
            Stages[stageId] = status;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, string?>> GetCompletedStagesAsync(Guid runId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, string?>>(Completed);
    }

    private sealed class Stage(string id, StageOutcome outcome, string mode = "assessment") : IStage
    {
        public string Id => id;
        public int Calls { get; private set; }
        public bool RunsIn(string runMode) => runMode == mode;

        public Task<StageOutcome> RunAsync(RunState state, string? checkpoint, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(outcome);
        }
    }

    private sealed class Throwing(string id) : IStage
    {
        public string Id => id;
        public bool RunsIn(string mode) => true;
        public Task<StageOutcome> RunAsync(RunState state, string? checkpoint, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("the network went away");
    }

    private static RunState State(string mode = "assessment") =>
        new() { RunId = Guid.NewGuid(), EngagementId = Guid.NewGuid(), Mode = mode };

    [Fact]
    public async Task A_stage_that_already_succeeded_is_not_run_again()
    {
        // A run that died during a twenty minute checker job must not start again from the
        // extraction.
        var journal = new Journal();
        journal.Completed["extract"] = null;
        var extract = new Stage("extract", StageOutcome.Succeeded());

        await new PipelineRunner([extract], journal, new Dictionary<string, bool>()).RunAsync(State(), default);

        extract.Calls.Should().Be(0);
    }

    [Fact]
    public async Task A_partial_stage_makes_the_whole_run_partial()
    {
        // A green tick over a three quarter extraction is how a client gets told their estate
        // is clean when nobody finished looking at it.
        var journal = new Journal();

        var outcome = await new PipelineRunner(
            [new Stage("extract", StageOutcome.Partial("four entities were refused"))],
            journal,
            new Dictionary<string, bool>()).RunAsync(State(), default);

        outcome.Status.Should().Be("partial");
        journal.RunStatuses.Should().EndWith(["partial"]);
    }

    [Fact]
    public async Task A_fatal_stage_stops_the_run_and_a_survivable_one_does_not()
    {
        var fatal = new Dictionary<string, bool>(StringComparer.Ordinal) { ["connect"] = true, ["extract"] = false };

        var stopped = await new PipelineRunner(
            [new Stage("connect", StageOutcome.Failed("no")), new Stage("analyse", StageOutcome.Succeeded())],
            new Journal(), fatal).RunAsync(State(), default);

        stopped.Status.Should().Be("failed");

        var carried = await new PipelineRunner(
            [new Stage("extract", StageOutcome.Failed("some entities")), new Stage("analyse", StageOutcome.Succeeded())],
            new Journal(), fatal).RunAsync(State(), default);

        carried.Status.Should().Be("partial");
        carried.StagesRun.Should().Be(1);
    }

    [Fact]
    public async Task A_stage_that_throws_is_recorded_rather_than_losing_the_run()
    {
        var journal = new Journal();

        var outcome = await new PipelineRunner([new Throwing("extract")], journal, new Dictionary<string, bool>())
            .RunAsync(State(), default);

        journal.Stages["extract"].Should().Be("failed");
        outcome.Status.Should().Be("partial");
    }

    [Fact]
    public async Task A_stage_that_does_not_run_in_this_mode_is_skipped_not_failed()
    {
        var journal = new Journal();

        await new PipelineRunner([new Stage("checker", StageOutcome.Succeeded(), mode: "assessment")],
            journal, new Dictionary<string, bool>()).RunAsync(State("quickScan"), default);

        journal.Stages["checker"].Should().Be("skipped");
    }

    [Fact]
    public void Reordering_a_backlog_does_not_change_its_hash_and_editing_one_does()
    {
        var a = ("k1", "Title A", "Given x", 1m, 4m, (int?)3);
        var b = ("k2", "Title B", "Given y", 2m, 8m, (int?)5);

        BacklogHash.Of([a, b]).Should().Be(BacklogHash.Of([b, a]),
            "somebody sorting the screen differently has not approved something different");

        BacklogHash.Of([a, b]).Should().NotBe(BacklogHash.Of([a with { Item4 = 9m }, b]),
            "an estimate changing after approval is exactly what the gate exists to catch");
    }
}
