namespace PowerPete.Analyzer.Tests;

using System.Text.Json;
using System.Text.RegularExpressions;
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

    /// <summary>The localisation resources, beside the contracts.</summary>
    private static string Resources()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(
            directory!.FullName, "src", "PowerPete.Analyzer.Domain", "Localization", "Resources");
    }

    [Fact]
    public void Every_rule_is_translated_into_every_language()
    {
        // A rule with no localisation does not fail, warn, or look wrong in a test. It
        // renders its own key: the report says "finding.performance.pcfBundleSize.name"
        // where the finding's name should be, in a document with a client's name on it.
        //
        // Seven rules shipped that way, in all six languages, and nothing noticed, because
        // every other guard in this codebase is about the rule catalogue and the catalogue
        // was complete. The bundles are hand written and the catalogue is not, so the two
        // drift apart in exactly one direction and only this checks it.
        //
        // English included rather than treated as the source. It is the fallback for every
        // other language, so a key missing there is the one case where there is nothing left
        // to fall back to.
        var rules = Contracts.Read("rule-catalogue").Array("rules").Select(rule => rule.Str("id")).ToList();
        var missing = new List<string>();

        foreach (var language in Contracts.Read("locales").Array("locales").Select(locale => locale.Str("code")))
        {
            var path = Path.Combine(Resources(), $"finding.{language}.json");

            File.Exists(path).Should().BeTrue($"the finding bundle for {language} has to exist");

            var bundle = JsonDocument.Parse(File.ReadAllText(path)).RootElement;

            missing.AddRange(
                from rule in rules
                from part in new[] { "name", "why", "recommendation" }
                where !bundle.TryGetProperty($"finding.{rule}.{part}", out _)
                select $"{language}: finding.{rule}.{part}");
        }

        missing.Should().BeEmpty(
            "an untranslated rule prints its own resource key into a client's report");
    }

    /// <summary>
    /// Component types the contract says travel in a solution file, that the offline reader
    /// has no reader for, each with the reason it is still on this list.
    /// </summary>
    /// <remarks>
    /// Named rather than counted, so that adding one is a decision somebody makes rather
    /// than a silence nobody notices. Every entry here is a section of an offline report
    /// that is empty because nothing looked, and the difference between that and a section
    /// empty because there was nothing to find is the entire argument of this product.
    /// </remarks>
    private static readonly Dictionary<string, string> UnreadFromSolutionZip = new(StringComparer.Ordinal)
    {
        ["chart"] = "No export to hand contains a SavedQueryVisualization. A reader written against "
            + "documentation alone would be the third one this repository got wrong that way.",
        ["dashboard"] = "Same: none of the nine real exports carries one.",
        ["report"] = "Same. RDL reports are rare in the estates seen so far and lifecycle.rdlReport "
            + "has therefore never fired against a real file.",
        ["emailTemplate"] = "Same.",
        ["customPage"] = "Travels as a canvas app inside the solution and is not distinguished from "
            + "one yet. The canvas reader finds it; it is typed as canvasApp.",
        ["customWorkflowActivity"] = "Lives inside a plug-in assembly. The assembly is read; the "
            + "activities within it need the assembly's types, which an offline read does not have.",
        ["customConnector"] = "Needs a connection. Declared for solutionZip in the contract because "
            + "the definition can travel, but no export to hand carries one.",
        ["copilotStudioAgent"] = "Needs a connection in practice. The bot content in a solution is "
            + "opaque to this reader."
    };

    [Fact]
    public void Every_type_a_solution_file_can_carry_is_read_or_named()
    {
        // The defect that keeps recurring, and the only one that is invisible by
        // construction. A component type declared with solutionZip evidence and no reader
        // does not throw, does not warn and does not appear in the not assessed list: the
        // report simply has nothing where it should be, and reads as a clean estate.
        //
        // It has now happened three times. pcfControl, which made a solution holding one
        // code component read as an estate with nothing in it. relationship, which hid two
        // hundred and forty-six components across nine real exports. serviceEndpoint, which
        // was worse than hiding a finding: the alerting rule checks for endpoints and found
        // none, so a solution that has one reported a High that was not true.
        //
        // The check is over the reader's source text rather than over a run, because no
        // single file contains every type and a run can only prove the types that file has.
        // A type the reader never names cannot possibly be produced by it.
        var source = File.ReadAllText(Path.Combine(
            Solution(), "PowerPete.Analyzer.Extraction", "SolutionZipReader.cs"));

        var unread = Contracts.Read("component-model").Array("componentTypes")
            .Where(type => type.Array("evidence").Any(e => e.GetString() == "solutionZip"))
            .Select(type => type.Str("id"))
            .Where(id => !source.Contains($"\"{id}\"", StringComparison.Ordinal))
            .ToList();

        unread.Should().BeSubsetOf(
            UnreadFromSolutionZip.Keys,
            "a type the reader never names is a section of the report that is silently empty. "
            + "Either read it, or add it to UnreadFromSolutionZip with the reason");

        // The other direction. A type that gained a reader and stayed on the list makes the
        // list a lie, and the list is the thing that is supposed to be trustworthy.
        UnreadFromSolutionZip.Keys.Should().BeSubsetOf(
            unread,
            "this type is read now, so remove it from UnreadFromSolutionZip");
    }

    [Fact]
    public void Every_key_the_report_asks_for_exists_in_every_language()
    {
        // The other half of the translation problem, and the quieter half.
        //
        // Every lookup in the renderer carries its English as a second argument, which is
        // what stops a missing key leaving a hole on page four. It also means a key that
        // exists in no bundle at all renders perfectly, in English, in all six languages,
        // and nothing ever says so. Fourteen of them had accumulated that way, including
        // three section headings and the whole legend of the low code donut.
        //
        // Scanned out of the source rather than listed here, so a key added tomorrow is
        // checked tomorrow.
        var source = Directory
            .EnumerateFiles(Path.Combine(Solution(), "PowerPete.Analyzer.Export"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.ReadAllText);

        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var text in source)
        {
            foreach (Match match in Regex.Matches(text, @"\[\s*""(report\.[A-Za-z0-9.]+)""", RegexOptions.None, TimeSpan.FromSeconds(5)))
            {
                used.Add(match.Groups[1].Value);
            }
        }

        used.Should().NotBeEmpty("the renderer looks keys up and the scan has to find them");

        var missing = new List<string>();

        foreach (var language in Contracts.Read("locales").Array("locales").Select(locale => locale.Str("code")))
        {
            var bundle = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(Resources(), $"report.{language}.json"))).RootElement;

            missing.AddRange(used
                .Where(key => !bundle.TryGetProperty(key, out _))
                .Select(key => $"{language}: {key}"));
        }

        missing.Should().BeEmpty(
            "a key with no bundle entry renders its English fallback in every language, silently");
    }

    [Fact]
    public void Never_selects_a_different_number_of_columns_than_the_record_has()
    {
        // The other half of the starred select, and the half that actually bit.
        //
        // Dapper materialises a record through its constructor and needs the columns to
        // match it. A starred select fails that way and there is a test above for it. An
        // explicit list can fail exactly the same way by drifting from the record beside
        // it, and nothing caught that: GetEngagementAsync selected nine columns into an
        // eight property record, threw about constructors rather than about columns, and
        // killed every run the worker ever picked up.
        //
        // Counted rather than name matched. Dapper is forgiving about order and about
        // case and unforgiving about arity, so arity is what this checks.
        var sources = Directory
            .EnumerateFiles(Path.Combine(Solution(), "PowerPete.Analyzer.Data"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        var mismatched = new List<string>();
        var checkedAny = false;

        foreach (var file in sources)
        {
            var text = File.ReadAllText(file);

            // The shape this is looking for: a typed Dapper call, then the first SELECT
            // literal after it. Anything it cannot read confidently is skipped rather
            // than guessed at, because a test that invents a failure is worse than one
            // that misses a case.
            foreach (Match call in Regex.Matches(
                text,
                @"Query(?:Single|SingleOrDefault|First|FirstOrDefault)?Async<(?<type>[A-Za-z][A-Za-z0-9_]*)>\s*\(",
                RegexOptions.None,
                TimeSpan.FromSeconds(5)))
            {
                var type = Type.GetType($"PowerPete.Analyzer.Data.{call.Groups["type"].Value}, PowerPete.Analyzer.Data");

                // Only records with one constructor. A tuple, a scalar or a type with
                // several constructors is not something this can reason about.
                if (type is null || !type.IsClass) continue;

                var constructors = type.GetConstructors();
                if (constructors.Length != 1) continue;

                var arity = constructors[0].GetParameters().Length;
                if (arity < 2) continue;

                // Bounded, and bounded twice.
                //
                // A call whose SQL is in a variable has no literal after it, and an
                // unbounded search walks forward into the next method and reads somebody
                // else's select. It did: the first version of this test reported an eight
                // column record against a thirteen column query from further down the
                // file, which is a failure about nothing.
                var window = text[call.Index..];
                // Past this call's own text. Searching from a fixed offset finds the
                // "Async<" inside the call that was just matched and truncates the window
                // to nothing, which is how the first version of this passed against the
                // exact defect it was written for.
                var next = window.IndexOf("Async<", call.Length, StringComparison.Ordinal);

                if (next > 0) window = window[..next];

                var select = Regex.Match(
                    window,
                    @"SELECT\s+(?<columns>[^;]*?)\s+FROM\s",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline,
                    TimeSpan.FromSeconds(5));

                // Close to the call, or it belongs to something else. The literal sits a
                // line or two below the call; anything further away is not this query's.
                if (!select.Success || select.Index > 200) continue;

                var columns = select.Groups["columns"].Value;

                // A hole in an interpolated string is a column list held somewhere else,
                // which is the thing this defect was fixed by adopting. Those are the
                // ones that cannot drift, so they are the ones not worth parsing.
                if (columns.Contains('{', StringComparison.Ordinal)) continue;

                // Anything with its own parentheses or a star is a shape this is not
                // confident about splitting.
                if (columns.Contains('(', StringComparison.Ordinal)) continue;
                if (columns.Contains('*', StringComparison.Ordinal)) continue;

                var count = columns.Split(',').Count(column => column.Trim().Length > 0);

                checkedAny = true;

                if (count != arity)
                {
                    mismatched.Add(
                        $"{Path.GetFileName(file)}: {type.Name} takes {arity} but the select has {count}");
                }
            }
        }

        checkedAny.Should().BeTrue("the scan has to be finding queries for this to mean anything");

        mismatched.Should().BeEmpty(
            "Dapper needs a constructor matching the columns, and the exception it throws when there is none "
            + "names constructors rather than the column that was added");
    }

    /// <summary>The src folder.</summary>
    private static string Solution()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory!.FullName, "src");
    }
}
