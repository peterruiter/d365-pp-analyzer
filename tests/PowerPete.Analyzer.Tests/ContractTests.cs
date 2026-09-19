namespace PowerPete.Analyzer.Tests;

using System.Text.Json;
using FluentAssertions;
using PowerPete.Analyzer.Domain;
using Xunit;

/// <summary>
/// Reads the contracts from disk.
/// </summary>
/// <remarks>
/// The same files the generators read, rather than the generated C#. A test over the
/// generated code proves the generator ran; a test over the contract proves the contract is
/// right, and the contract is what somebody edits at four in the afternoon.
/// </remarks>
internal static class Contracts
{
    public static JsonElement Read(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "build", "contracts")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests have to be able to find build/contracts from wherever the runner put them");

        var path = Path.Combine(directory!.FullName, "build", "contracts", $"{name}.json");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
    }

    public static IEnumerable<JsonElement> Array(this JsonElement element, string property) =>
        element.GetProperty(property).EnumerateArray();

    public static string Str(this JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() ?? string.Empty : string.Empty;
}

/// <summary>
/// The checks that stop a contract edit breaking the product silently.
/// </summary>
/// <remarks>
/// A broken cross reference does not fail a build. It fails at run time as a rule that
/// matches nothing, which looks exactly like an estate with no findings, which is the one
/// outcome this product must never produce.
/// </remarks>
public sealed class ContractTests
{
    [Fact]
    public void Every_rule_applies_to_component_types_that_exist()
    {
        var components = Contracts.Read("component-model").Array("componentTypes")
            .Select(type => type.Str("id")).ToHashSet(StringComparer.Ordinal);

        foreach (var rule in Contracts.Read("rule-catalogue").Array("rules"))
        {
            foreach (var applies in rule.Array("appliesTo").Select(value => value.GetString()!))
            {
                components.Should().Contain(applies,
                    $"rule '{rule.Str("id")}' names it, and a rule pointing at a component type that does not exist matches nothing and reports nothing");
            }
        }
    }

    [Fact]
    public void Every_rule_has_a_detection_description()
    {
        foreach (var rule in Contracts.Read("rule-catalogue").Array("rules"))
        {
            rule.Str("detection").Should().NotBeNullOrWhiteSpace(
                $"rule '{rule.Str("id")}' is in the catalogue, and a catalogue that lists aspirations reads identically to one that lists capabilities");
        }
    }

    [Fact]
    public void Every_actionable_rule_has_an_acceptance_criterion_with_a_separate_test()
    {
        var criteria = Contracts.Read("acceptance-criteria").GetProperty("criteria");

        var actionable = Contracts.Read("rule-catalogue").Array("rules")
            .Where(rule => rule.Str("workItemType") != "none")
            .ToList();

        actionable.Should().NotBeEmpty();

        foreach (var rule in actionable)
        {
            criteria.TryGetProperty(rule.Str("id"), out var criterion).Should().BeTrue(
                $"rule '{rule.Str("id")}' produces a {rule.Str("workItemType")} and there is deliberately no generic fallback");

            foreach (var field in new[] { "given", "when", "then", "testRequirement" })
            {
                criterion.Str(field).Should().NotBeNullOrWhiteSpace(
                    $"'{rule.Str("id")}' needs a {field}");
            }
        }
    }

    [Fact]
    public void Only_the_publish_stage_writes_to_a_target()
    {
        var writers = Contracts.Read("analysis-stages").Array("stages")
            .Where(stage => stage.Str("writes") == "target")
            .Select(stage => stage.Str("id"))
            .ToList();

        writers.Should().Equal(["publish"],
            "a discovery that is safe to run in a first conversation is the whole value of this product, " +
            "and it stops being safe the moment a second stage can write somewhere");
    }

    [Fact]
    public void Every_lifecycle_claim_that_is_not_current_says_where_it_came_from()
    {
        foreach (var component in Contracts.Read("component-model").Array("componentTypes"))
        {
            if (component.Str("lifecycle") == "current") continue;

            component.TryGetProperty("lifecycleVerification", out _).Should().BeTrue(
                $"'{component.Str("id")}' claims to be {component.Str("lifecycle")}, and a deprecation claim nobody can " +
                "source is one that gets disproved in front of a client");
        }
    }

    [Fact]
    public void Every_modernisation_entry_makes_the_case_for_doing_nothing()
    {
        foreach (var entry in Contracts.Read("modernisation-map").Array("entries"))
        {
            entry.Str("leaveItAlone").Should().NotBeNullOrWhiteSpace(
                $"'{entry.Str("id")}' offers replacements, and a report where every finding leads to work is one a client stops believing");
        }
    }

    [Fact]
    public void Every_extraction_mode_states_its_reach_for_every_evidence_source()
    {
        var sources = Contracts.Read("component-model").GetProperty("evidenceSources")
            .EnumerateObject()
            .Where(property => property.Name != "description")
            .Select(property => property.Name);

        foreach (var mode in Contracts.Read("extraction-sources").Array("modes"))
        {
            var reaches = mode.GetProperty("reaches");

            foreach (var source in sources)
            {
                reaches.TryGetProperty(source, out _).Should().BeTrue(
                    $"mode '{mode.Str("id")}' says nothing about {source}, and an unstated reach becomes a silently skipped rule");
            }
        }
    }

    [Fact]
    public void The_estimate_model_has_no_way_to_express_a_point_estimate()
    {
        var bands = Contracts.Read("estimate-model").GetProperty("bands");

        foreach (var band in bands.EnumerateObject().Where(property => property.Name != "description"))
        {
            band.Value.TryGetProperty("low", out _).Should().BeTrue();
            band.Value.TryGetProperty("high", out _).Should().BeTrue();
            band.Value.Str("rationale").Should().NotBeNullOrWhiteSpace(
                "a band's rationale is used verbatim when a finding falls back to it");
        }
    }

    [Fact]
    public void The_generated_catalogue_and_the_contract_agree()
    {
        // Catches the case where somebody edits a contract and does not regenerate. The
        // generated code is what runs, and a stale catalogue means findings against rules
        // nobody can explain.
        var contractIds = Contracts.Read("rule-catalogue").Array("rules")
            .Select(rule => rule.Str("id")).OrderBy(id => id, StringComparer.Ordinal);

        RuleCatalogue.All.Select(rule => rule.Id).OrderBy(id => id, StringComparer.Ordinal)
            .Should().Equal(contractIds, "run ./build/Invoke-CodeGen.ps1 after changing a contract");
    }
}

/// <summary>
/// The generated catalogues against the contracts that produced them.
/// </summary>
/// <remarks>
/// These exist because a literal that also appears in a contract is a bug waiting to produce
/// two different numbers for the same estate. The command line carried its own band table
/// until 0.8.0; these tests are what stops the next one.
/// </remarks>
public sealed class GeneratedCatalogueTests
{
    [Fact]
    public void The_band_table_matches_the_contract()
    {
        var contract = Contracts.Read("estimate-model").GetProperty("bands")
            .EnumerateObject()
            .Where(band => band.Name != "description")
            .ToList();

        EstimateCatalogue.Bands.Should().HaveCount(contract.Count);

        foreach (var band in contract)
        {
            EstimateCatalogue.Bands.Should().ContainKey(band.Name);

            var generated = EstimateCatalogue.Bands[band.Name];
            generated.Low.Should().Be(band.Value.GetProperty("low").GetDecimal());
            generated.High.Should().Be(band.Value.GetProperty("high").GetDecimal());
            generated.Rationale.Should().NotBeNullOrWhiteSpace(
                "a band's rationale is used verbatim when a finding falls back to it");
        }
    }

    [Fact]
    public void Every_rule_carries_a_roadmap_position_into_the_generated_catalogue()
    {
        RuleCatalogue.Roadmap.Should().HaveCount(RuleCatalogue.All.Count);

        foreach (var rule in RuleCatalogue.All)
        {
            rule.RoadmapRow.Should().BeOneOf("business", "process");
            rule.RoadmapColumn.Should().BeOneOf("architecture", "technology");
            rule.RoadmapBand.Should().BeOneOf("unclutter", "accelerate", "innovate");
        }
    }

    [Fact]
    public void Every_complexity_rule_names_a_component_type_that_exists()
    {
        foreach (var rule in EstimateCatalogue.ComplexityRules)
        {
            ComponentCatalogue.Find(rule.ComponentTypeId).Should().NotBeNull(
                $"the complexity rule for '{rule.ComponentTypeId}' would otherwise match nothing and rate nothing");

            rule.Bands.Should().NotBeEmpty();
            rule.Bands[^1].UpTo.Should().BeNull("the last band has to catch everything above the ones before it");
        }
    }

    [Fact]
    public void The_fixed_costs_are_per_engagement_and_not_per_finding()
    {
        EstimateCatalogue.FixedCosts.Should().NotBeEmpty();

        foreach (var cost in EstimateCatalogue.FixedCosts)
        {
            cost.Low.Should().BeLessThanOrEqualTo(cost.High);
            cost.Name.Should().NotBeNullOrWhiteSpace("a client sees these named in the report");
        }
    }

    [Fact]
    public void The_point_scale_is_fibonacci_and_matches_the_estimate_type()
    {
        EstimateCatalogue.PointScale.Should().Equal(Estimate.PointScale,
            "two point scales means a story sized against one and planned against the other");
    }
}

/// <summary>
/// Guards against the read that compiles, passes review and fails only in production.
/// </summary>
public class DataLayerTests
{
    /// <summary>The data layer's source, found from the test assembly rather than hardcoded.</summary>
    private static string DataLayer()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory!.FullName, "src", "PowerPete.Analyzer.Data");
    }

    /// <summary>
    /// The file with its line comments removed.
    /// </summary>
    /// <remarks>
    /// Scanned without them because the comment explaining why a starred select is wrong
    /// contains the words, and a guard that fails on its own rationale gets deleted.
    /// </remarks>
    /// <param name="source">The file's text.</param>
    private static string WithoutComments(string source) =>
        string.Join(
            Environment.NewLine,
            source.Split('\n')
                .Select(line => line.TrimStart().StartsWith("//", StringComparison.Ordinal) ? string.Empty : line));

    [Fact]
    public void Never_selects_star_into_a_record()
    {
        // Dapper materialises a record through its constructor and needs one matching the
        // columns it was handed. A starred select hands it every column the table has,
        // including audit columns the record deliberately does not declare, and it throws
        // at run time with a message about constructors rather than about columns.
        //
        // This cost a day: the engagement list threw on every request, so the product
        // accepted a new engagement and then showed an empty picker. The row was written and
        // could not be read back, which looks exactly like a permissions problem and is not.
        var offenders = Directory.EnumerateFiles(DataLayer(), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => WithoutComments(File.ReadAllText(file)).Contains("SELECT *", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .ToList();

        offenders.Should().BeEmpty(
            "a starred select into a record fails to materialise the moment the table gains a column");
    }
}
