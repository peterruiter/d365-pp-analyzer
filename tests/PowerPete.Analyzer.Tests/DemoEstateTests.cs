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

    /// <summary>
    /// What the demonstration looked like at each seed version, as a fingerprint.
    /// </summary>
    /// <remarks>
    /// Add a line here when SeedVersion moves, with the fingerprint the failure prints. The
    /// older lines stay as the record of what each version was.
    /// </remarks>
    private static readonly Dictionary<int, string> ShapeAtVersion = new()
    {
        // 6: the contact centre, which version 5 should have been and was not.
        [6] = "E90CA3422FE1C686", [7] = "C0060620DBA24262", [8] = "A1040023F58F998A",
    };

    /// <summary>
    /// Every component and every attribute, in a stable order, hashed.
    /// </summary>
    /// <remarks>
    /// Attributes as well as components, because changing a queue's member count changes
    /// what the demonstration finds, and that needs a rebuild as surely as adding a queue.
    /// </remarks>
    private static string Fingerprint(DemoEstate.Result estate)
    {
        var text = new System.Text.StringBuilder();

        foreach (var component in estate.Components.OrderBy(component => component.StableKey, StringComparer.Ordinal))
        {
            text.Append(component.StableKey).Append('|').Append(component.TypeId).Append('|').Append(component.DisplayName);

            foreach (var attribute in component.Attributes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                text.Append('|').Append(attribute.Key).Append('=')
                    .Append(Convert.ToString(attribute.Value, System.Globalization.CultureInfo.InvariantCulture));
            }

            text.Append('\n');
        }

        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text.ToString()));
        return Convert.ToHexString(hash)[..16];
    }

    [Fact]
    public void The_seed_version_moves_whenever_the_demonstration_does()
    {
        // The seeder rebuilds the demonstration only when SeedVersion changes. The contact
        // centre was added without that change, so the estate anybody actually opened kept
        // no contact centre while this whole file passed against the code.
        var shape = Fingerprint(Estate);

        ShapeAtVersion.Should().ContainKey(DemoEstate.SeedVersion,
            $"SeedVersion is {DemoEstate.SeedVersion} and nothing records what that version looks like. "
            + $"Add [{DemoEstate.SeedVersion}] = \"{shape}\" to ShapeAtVersion.");

        ShapeAtVersion[DemoEstate.SeedVersion].Should().Be(shape,
            $"the demonstration has changed shape and SeedVersion is still {DemoEstate.SeedVersion}, so the "
            + "deployed demonstration will not be rebuilt and nobody will see the change. Bump SeedVersion, "
            + $"and record [{DemoEstate.SeedVersion + 1}] = \"{shape}\" in ShapeAtVersion.");
    }

    [Fact]
    public void The_low_code_donut_agrees_with_the_number_written_in_it()
    {
        // A picture that disagrees with its own caption. The report drew the ratio as a
        // donut whose slices came from ByCraft and whose centre came from LowCodeShare,
        // and those have different denominators: ByCraft counts every typed component and
        // the share is taken over the ones that count toward the ratio. Twelve pro code
        // components sat in the slices and not in the number, so the demonstration report
        // showed a circle reading 56 percent with 66 percent printed in the middle of it.
        //
        // Nothing failed. It rendered, it looked right, and it was going on a public
        // website before somebody read the legend.
        var counted = Estate.Score.CountedByCraft.Values.Sum();

        counted.Should().BeGreaterThan(0, "the demonstration estate has components that count toward the ratio");

        var fromSlices = Math.Round(
            (decimal)Estate.Score.CountedByCraft.GetValueOrDefault("lowCode") / counted, 3);

        fromSlices.Should().Be(
            Estate.Score.LowCodeShare,
            "the slices of the donut and the figure in the centre of it have to be the same number");

        // The other half of the same mistake: anything outside the ratio must not be in
        // the breakdown the circle is drawn from.
        Estate.Score.CountedByCraft.Keys.Should().NotContain(
            "config", "configuration is counted separately and never inside the ratio");
    }

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
    public void Gives_every_finding_a_key_of_its_own()
    {
        // The database holds a unique constraint on run and stable key, so two findings
        // sharing one key do not produce a duplicate row. They fail the whole run, at the
        // point of saving rather than the point of making them, and the message names a
        // constraint rather than the rule that emitted twice.
        //
        // Rules with no component are where this bites: prefix sprawl fires per solution,
        // orphaned columns per table, inconsistent naming per component type. Each of those
        // keyed on the rule alone until this estate found it.
        var duplicates = Estate.Findings
            .GroupBy(entry => entry.Finding.StableKey, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key} ({group.Count()})")
            .ToList();

        duplicates.Should().BeEmpty();
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
