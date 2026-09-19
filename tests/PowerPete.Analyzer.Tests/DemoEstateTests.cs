namespace PowerPete.Analyzer.Tests;

using FluentAssertions;
using PowerPete.Analyzer.Domain;
using PowerPete.Analyzer.Pipeline;
using Xunit;

/// <summary>
/// The demonstration engagement, which is the first thing most people ever see of this product.
/// </summary>
/// <remarks>
/// It is built by running the real rule engine over a synthetic estate, which is what makes it
/// worth showing and also what makes it fragile: a rule whose trigger moves stops firing here
/// without anything failing, and the demonstration quietly gets thinner. Nobody notices until
/// somebody is standing in front of a client wondering why the security section is empty.
///
/// These assertions are floors, not exact numbers. An exact count would be a test that fails
/// every time somebody adds a rule, which trains people to update the number without reading
/// why it moved.
/// </remarks>
public class DemoEstateTests
{
    private static readonly DemoEstate.Result Estate = DemoEstate.Build();

    [Fact]
    public void Builds_an_estate_large_enough_to_demonstrate()
    {
        Estate.Components.Should().HaveCountGreaterThan(300,
            "a demonstration of an estate analyser has to look like an estate");

        Estate.Solutions.Should().HaveCountGreaterThan(1);
        Estate.Components.Select(component => component.TypeId).Distinct().Should().HaveCountGreaterThan(25);
    }

    [Fact]
    public void Finds_enough_to_fill_every_screen()
    {
        Estate.Findings.Should().HaveCountGreaterThan(60);
        Estate.Backlog.Should().HaveCountGreaterThan(40);
        Estate.Roadmap.Should().HaveCountGreaterThan(20);
        Estate.Customisation.Should().NotBeEmpty();
    }

    [Fact]
    public void Covers_every_category_in_the_catalogue()
    {
        // A category with no findings is a section of the report that renders empty, and the
        // reader concludes the estate is clean there rather than that the demonstration is thin.
        var found = Estate.Findings
            .Select(entry => entry.Finding.Rule?.Category)
            .Where(category => category is not null)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var declared = RuleCatalogue.All.Select(rule => rule.Category).Distinct(StringComparer.Ordinal);

        found.Should().BeEquivalentTo(declared);
    }

    [Fact]
    public void Fires_most_of_the_catalogue()
    {
        var fired = Estate.Findings.Select(entry => entry.Finding.RuleId).Distinct(StringComparer.Ordinal).Count();

        // Not all of them. Two rules need a second environment or a total absence of error
        // handling, and an estate contrived to trip every single rule stops looking like an
        // estate. Most of them is the bar.
        fired.Should().BeGreaterThan((int)(RuleCatalogue.All.Count * 0.8));
    }

    [Fact]
    public void Shows_findings_at_every_severity()
    {
        var severities = Estate.Findings.Select(entry => entry.Finding.Severity).Distinct().ToList();

        severities.Should().Contain(Severity.Critical);
        severities.Should().Contain(Severity.High);
        severities.Should().Contain(Severity.Medium);
        severities.Should().Contain(Severity.Low);
    }

    [Fact]
    public void Estimates_add_up_to_something_a_client_would_recognise()
    {
        Estate.Score.TotalLowHours.Should().BeGreaterThan(0);
        Estate.Score.TotalHighHours.Should().BeGreaterThan(Estate.Score.TotalLowHours);

        // The ratio is the number that gets quoted, so a demonstration where it is null or
        // absurd is worse than no demonstration.
        Estate.Score.LowCodeShare.Should().NotBeNull();
        Estate.Score.LowCodeShare!.Value.Should().BeInRange(0.2m, 0.95m);
    }

    [Fact]
    public void Is_the_same_estate_every_time_it_is_built()
    {
        // The backlog hash is what an approval binds to. If two containers running the same
        // image build different demonstrations, the approval seeded by one detaches the
        // moment the other restarts, and the backlog screen starts claiming it changed.
        var again = DemoEstate.Build();

        again.BacklogHash.Should().Be(Estate.BacklogHash);
        again.Components.Select(component => component.StableKey)
            .Should().Equal(Estate.Components.Select(component => component.StableKey));
    }

    [Fact]
    public void Uses_the_engagement_identifier_the_access_store_grants_everybody()
    {
        // Held in two places on purpose: the data layer does not reference the pipeline. This
        // is the test that keeps them the same value, and without it the demonstration would
        // exist and be invisible to everybody who is not a global administrator.
        DemoEstate.EngagementId.Should().Be(Data.AccessStore.DemoEngagementId);
    }

    [Fact]
    public void Never_claims_a_rule_passed_that_it_could_not_check()
    {
        // Whatever could not run has to be named. The count may be zero here, because this
        // estate is read through a connection that reaches everything; what must never happen
        // is a rule that neither fired nor appeared in the not assessed list without a handler
        // having looked at it.
        var accountedFor = Estate.Findings.Select(entry => entry.Finding.RuleId)
            .Concat(Estate.NotAssessed.Select(entry => entry.RuleId))
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var silent = RuleCatalogue.All
            .Select(rule => rule.Id)
            .Where(id => !accountedFor.Contains(id))
            .ToList();

        // The ones that legitimately find nothing in this estate, named rather than counted,
        // so adding a rule that silently finds nothing fails here and has to be considered.
        silent.Should().BeSubsetOf(["operability.noEnvironmentSeparation", "operability.noFailureAlerting"]);
    }
}
