namespace PowerPete.Analyzer.Tests;

using FluentAssertions;
using PowerPete.Analyzer.Analysis;
using PowerPete.Analyzer.Analysis.Handlers;
using PowerPete.Analyzer.Domain;
using Xunit;

/// <summary>
/// The Dynamics 365 Contact Center rules.
/// </summary>
/// <remarks>
/// The property worth testing is not that a rule fires on the bad configuration, which any
/// version of it would. It is that a rule does not fire on configuration this product could
/// not read. A queue whose member count did not come back is not a queue with nobody in it,
/// and a capacity profile whose holders could not be counted is not one nobody holds. Both
/// would be critical or high findings, and both would be wrong, and the people reading them
/// run contact centres for a living and would know at once.
///
/// So most of these are the null case, beside the zero case, for the same rule.
/// </remarks>
public class ContactCenterTests
{
    private static DiscoveredComponent Queue(params (string, object?)[] overrides)
    {
        // A healthy queue, so each test changes only the thing it is about.
        var values = new Dictionary<string, object?>
        {
            ["isActive"] = true,
            ["assignsNothing"] = false,
            ["hasOperatingHours"] = true,
            ["hasPreQueueOverflow"] = true,
            ["hasInQueueOverflow"] = true,
            ["serviceLevelSeconds"] = 60,
            ["memberCount"] = 5,
        };

        foreach (var (key, value) in overrides) values[key] = value;

        return Build.Component("ccQueue", "Billing", false, [.. values.Select(pair => (pair.Key, pair.Value))]);
    }

    private static DiscoveredComponent Profile(params (string, object?)[] overrides)
    {
        var values = new Dictionary<string, object?>
        {
            ["isActive"] = true,
            ["blocksAssignment"] = false,
            ["resetsDaily"] = false,
            ["agentCount"] = 4,
            ["requiredBy"] = "Web chat",
            ["requiredByCount"] = 1,
        };

        foreach (var (key, value) in overrides) values[key] = value;

        return Build.Component("ccCapacityProfile", "Specialists", false, [.. values.Select(pair => (pair.Key, pair.Value))]);
    }

    private static List<Finding> Run(IRuleHandler handler, params DiscoveredComponent[] components) =>
        [.. handler.Run(Build.Context(components))];

    // ------------------------------------------------------------- members ---

    [Fact]
    public void A_queue_with_nobody_in_it_is_reported()
    {
        Run(new QueueNoMembersHandler(), Queue(("memberCount", 0))).Should().ContainSingle();
    }

    [Fact]
    public void A_queue_whose_members_could_not_be_counted_is_not()
    {
        // The difference between "nobody" and "unknown" is the entire rule. Reading unknown
        // as nobody would put a critical finding on every queue the moment the count failed
        // to come back, for a reason that has nothing to do with the queue.
        Run(new QueueNoMembersHandler(), Queue(("memberCount", null))).Should().BeEmpty();
    }

    [Fact]
    public void An_inactive_empty_queue_is_tidy_rather_than_broken()
    {
        Run(new QueueNoMembersHandler(), Queue(("memberCount", 0), ("isActive", false))).Should().BeEmpty();
    }

    // ----------------------------------------------------- capacity profiles ---

    [Fact]
    public void A_required_profile_nobody_holds_is_reported()
    {
        var findings = Run(new CapacityProfileUnheldHandler(), Profile(("agentCount", 0)));

        findings.Should().ContainSingle();
        findings[0].Evidence["requiredBy"].Should().Be("Web chat",
            "the finding has to name the workstream that can never be served, or nobody can act on it");
    }

    [Theory]
    [InlineData(null, 1, "holders could not be counted")]
    [InlineData(0, null, "requirers could not be read")]
    [InlineData(0, 0, "nothing requires it, which is unused rather than broken")]
    public void A_profile_is_not_reported_unless_both_halves_are_known_and_bad(
        int? agents, int? requiredBy, string because)
    {
        Run(new CapacityProfileUnheldHandler(),
                Profile(("agentCount", agents), ("requiredByCount", requiredBy)))
            .Should().BeEmpty(because);
    }

    // ------------------------------------------------------------- noise ---

    [Fact]
    public void An_empty_queue_is_reported_once_rather_than_five_times()
    {
        // A queue with nobody in it usually has no overflow, no hours and no service level
        // either, because nobody finished it. Listing all five buries the one that matters
        // under four that do not matter until the first is fixed.
        var empty = Queue(
            ("memberCount", 0),
            ("assignsNothing", true),
            ("hasOperatingHours", false),
            ("hasPreQueueOverflow", false),
            ("hasInQueueOverflow", false),
            ("serviceLevelSeconds", null));

        IRuleHandler[] all =
        [
            new QueueNoMembersHandler(), new QueueNoAssignmentHandler(), new QueueNoOverflowHandler(),
            new QueueNoOperatingHoursHandler(), new QueueNoServiceLevelHandler()
        ];

        var fired = all.SelectMany(handler => handler.Run(Build.Context([empty]))).Select(finding => finding.RuleId).ToList();

        fired.Should().Equal("contactCenter.queueNoMembers");
    }

    [Fact]
    public void A_healthy_queue_produces_nothing()
    {
        IRuleHandler[] all =
        [
            new QueueNoMembersHandler(), new QueueNoAssignmentHandler(), new QueueNoOverflowHandler(),
            new QueueNoOperatingHoursHandler(), new QueueNoServiceLevelHandler()
        ];

        all.SelectMany(handler => handler.Run(Build.Context([Queue()]))).Should().BeEmpty();
    }

    // --------------------------------------------------------- the others ---

    [Fact]
    public void Overflow_needs_both_rulesets_missing()
    {
        Run(new QueueNoOverflowHandler(), Queue(("hasPreQueueOverflow", false), ("hasInQueueOverflow", false)))
            .Should().ContainSingle();

        Run(new QueueNoOverflowHandler(), Queue(("hasPreQueueOverflow", true), ("hasInQueueOverflow", false)))
            .Should().BeEmpty("one overflow rule set is overflow handling");
    }

    [Fact]
    public void A_service_level_of_zero_is_a_setting_not_a_gap()
    {
        Run(new QueueNoServiceLevelHandler(), Queue(("serviceLevelSeconds", null))).Should().ContainSingle();
        Run(new QueueNoServiceLevelHandler(), Queue(("serviceLevelSeconds", 0))).Should().BeEmpty();
    }

    [Fact]
    public void A_workstream_with_no_default_queue_is_reported_and_one_unknown_is_not()
    {
        var missing = Build.Component("ccWorkstream", "Chat", false, ("isActive", true), ("hasDefaultQueue", false));
        var unknown = Build.Component("ccWorkstream", "Chat", false, ("isActive", true), ("hasDefaultQueue", null));

        Run(new WorkstreamNoDefaultQueueHandler(), missing).Should().ContainSingle();
        Run(new WorkstreamNoDefaultQueueHandler(), unknown).Should().BeEmpty();
    }

    [Fact]
    public void A_daily_block_needs_both_the_block_and_the_daily_reset()
    {
        Run(new CapacityProfileDailyBlockHandler(), Profile(("blocksAssignment", true), ("resetsDaily", true)))
            .Should().ContainSingle();

        Run(new CapacityProfileDailyBlockHandler(), Profile(("blocksAssignment", true), ("resetsDaily", false)))
            .Should().BeEmpty("an immediate reset is a concurrency limit, which is the usual intent");
    }
}
