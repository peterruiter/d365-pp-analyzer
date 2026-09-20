namespace PowerPete.Analyzer.Tests;

using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using PowerPete.Analyzer.Data;
using PowerPete.Analyzer.Domain;
using PowerPete.Analyzer.Pipeline;
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
    [Fact]
    public void Reads_every_setting_the_infrastructure_writes()
    {
        // This is the test that would have caught a deployment that was broken from the day
        // it was first deployed.
        //
        // The bicep wrote KeyVault__Uri. The API read KeyVaultUri, found nothing, and built
        // the secret store that refuses. The worker read ANALYZER_KEYVAULT_URI and found
        // nothing either. The system health page read KeyVault:Uri, found it, wrote a probe
        // secret and reported a healthy vault. Nothing disagreed out loud, and the only
        // symptom was a 503 at the last step of the sign-in wizard.
        //
        // Both directions, because each one hides a different fault. A name written and never
        // read is a setting an operator thinks they have set. A name read and never written is
        // a feature that is off in every deployment.
        var written = WrittenByTheInfrastructure();

        written.Should().NotBeEmpty("the bicep sets environment variables and this has to be reading them");

        // Variables the platform and the Azure SDK read for themselves. They belong in the
        // container definition and no code of ours asks for them by name.
        var platform = new[] { "AZURE_CLIENT_ID", "APPLICATIONINSIGHTS_CONNECTION_STRING" };

        var accepted = DeploymentSettings.All
            .SelectMany(DeploymentSettings.Names)
            .Select(DeploymentSettings.EnvironmentName)
            .ToHashSet(StringComparer.Ordinal);

        var unread = written
            .Where(name => !platform.Contains(name, StringComparer.Ordinal))
            .Where(name => !accepted.Contains(name))
            .ToList();

        unread.Should().BeEmpty(
            "the infrastructure sets these on the container and nothing in the product reads them, "
            + "so an operator can set them correctly and watch the feature stay off");

        var unwritten = DeploymentSettings.All
            .Where(setting => setting.Deployed)
            .Where(setting => !DeploymentSettings.Names(setting)
                .Select(DeploymentSettings.EnvironmentName)
                .Any(name => written.Contains(name, StringComparer.Ordinal)))
            .Select(setting => setting.Name)
            .ToList();

        unwritten.Should().BeEmpty(
            "these are read from a deployed container and the bicep never sets them under any name "
            + "the code accepts");
    }

    [Fact]
    public void Runs_the_worker_assembly_the_build_actually_produces()
    {
        // The other half of the same deployment, and the one that cost more. The worker's
        // entry point ran "dotnet PowerPete.Analyzer.Jobs.dll", the Jobs project sets
        // AssemblyName to analyzer, and the container crash looped every five minutes from
        // the first deployment onwards. Every run ever queued sat in the queue.
        var root = Directory.GetParent(Solution())!.FullName;

        var assemblyName = Regex.Match(
            File.ReadAllText(Path.Combine(Solution(), "PowerPete.Analyzer.Jobs", "PowerPete.Analyzer.Jobs.csproj")),
            @"<AssemblyName>(?<name>[^<]+)</AssemblyName>",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        assemblyName.Success.Should().BeTrue("the worker project sets its own assembly name and this reads it");

        var expected = $"{assemblyName.Groups["name"].Value}.dll";
        var dockerfile = File.ReadAllText(Path.Combine(root, "Dockerfile"));

        var entryPoint = Regex.Match(
            dockerfile,
            @"exec dotnet (?<path>\S+\.dll)",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        entryPoint.Success.Should().BeTrue("the image writes a shell entry point for the worker");

        Path.GetFileName(entryPoint.Groups["path"].Value).Should().Be(
            expected,
            "the worker entry point has to name the assembly the publish step produces, or the "
            + "container starts, fails to find it, and restarts for ever");
    }

    [Fact]
    public void Tells_the_worker_container_to_work()
    {
        // The third way one deployment managed to run nothing.
        //
        // With the assembly found, the container ran the dispatcher with no arguments. The
        // dispatcher's answer to no arguments is its help screen and exit 0, which the
        // platform reads as a container that finished its work, so it started it again. A
        // crash loop that exits successfully and prints a help screen looks like nothing at
        // all in a log.
        //
        // Both places, because they protect each other. The bicep passes it, and the image
        // falls back to it for a container deployed by hand.
        var root = Directory.GetParent(Solution())!.FullName;

        var bicep = File.ReadAllText(Path.Combine(root, "infra", "modules", "containerapps.bicep"));

        var worker = bicep[bicep.IndexOf("name: 'worker'", StringComparison.Ordinal)..];

        worker.Should().NotBeEmpty("the bicep defines a worker container and this reads it");

        worker[..worker.IndexOf("scale:", StringComparison.Ordinal)]
            .Should().Contain(
                "'work'",
                "the worker container has to be told which command to run, or it prints its help "
                + "and exits zero for ever");

        File.ReadAllText(Path.Combine(root, "Dockerfile"))
            .Should().Contain(
                "set -- work",
                "the image has to default to the poll loop, so a worker deployed without arguments "
                + "still polls");
    }

    [Fact]
    public void Can_pass_every_template_parameter_that_is_cleared_by_omitting_it()
    {
        // A parameter defaulting to an empty string is not optional, it is destructive: the
        // template writes the empty value over whatever is deployed. Every one of those has
        // to be reachable from the deployment script, or the only way to set it is by hand
        // and the next deployment silently takes it away again.
        //
        // The first global administrator and the support contact were exactly that. The
        // template took them, the container was given them, nothing read them under the name
        // they were written under, and the script had no parameter for either, so there was
        // no supported way to set them and no way to notice.
        var root = Directory.GetParent(Solution())!.FullName;
        var template = File.ReadAllText(Path.Combine(root, "infra", "main.bicep"));
        var script = File.ReadAllText(Path.Combine(root, "build", "Deploy-Infrastructure.ps1"));

        var clearedByOmission = Regex
            .Matches(
                template,
                @"^param (?<name>[A-Za-z][A-Za-z0-9]*) string = ''",
                RegexOptions.Multiline,
                TimeSpan.FromSeconds(5))
            .Select(match => match.Groups["name"].Value)
            .ToList();

        clearedByOmission.Should().NotBeEmpty("the template has parameters that default to empty");

        var unreachable = clearedByOmission
            .Where(name => !script.Contains($"{name}=$", StringComparison.Ordinal))
            .ToList();

        unreachable.Should().BeEmpty(
            "the template overwrites these with an empty value when they are not passed, so a "
            + "parameter the deployment script cannot pass is one that cannot survive a deployment");
    }

    [Fact]
    public void Runs_the_stages_the_contract_declares_in_the_order_it_declares_them()
    {
        // PipelineOrder exists because two callers need the order without being able to
        // build a stage: the worker, to know what "everything after this one" means when
        // somebody re-runs a stage, and the API, to draw a timeline before a run has
        // reached anything.
        //
        // A list like that is exactly the kind that falls quietly out of step. The way it
        // would go wrong is a retry that discards the wrong stages, which looks like the
        // product losing work for no reason.
        var contract = Contracts.Read("analysis-stages");

        var declared = contract.GetProperty("stages").EnumerateArray()
            .Select(stage => (
                Id: stage.GetProperty("id").GetString()!,
                Order: stage.GetProperty("order").GetInt32()))
            .OrderBy(stage => stage.Order)
            .Select(stage => stage.Id)
            .ToList();

        // Not every declared stage is one the worker runs: normalise, approve and export
        // are declared and handled elsewhere. What matters is that the ones it does run
        // appear in the order the contract puts them in.
        var run = PipelineOrder.Stages;

        run.Should().OnlyHaveUniqueItems();

        foreach (var stage in run)
        {
            declared.Should().Contain(
                stage,
                "the worker runs this stage and analysis-stages.json is where the pipeline is declared");
        }

        var expected = declared.Where(run.Contains).ToList();

        run.Should().Equal(
            expected,
            "the worker's order and the contract's order have to be the same one, or running a stage "
            + "again discards the wrong work");
    }

    [Fact]
    public void Runs_the_checks_each_mode_says_it_runs()
    {
        // The defaults are resolved in code, once, at the top of a run, and declared in the
        // contract beside the mode they belong to. A drift between the two is a report that
        // says it ran the checker and did not.
        var contract = Contracts.Read("analysis-stages");

        foreach (var mode in contract.GetProperty("modes").EnumerateArray())
        {
            var id = mode.GetProperty("id").GetString()!;
            var checks = RunChecks.ForMode(id);

            checks.SolutionChecker.Should().Be(
                mode.GetProperty("runsChecker").GetBoolean(),
                $"analysis-stages.json says whether {id} runs the checker");

            // "model" is the only value that means a model was asked. bandDefault and
            // asApproved both mean it was not.
            checks.ModelEstimates.Should().Be(
                mode.GetProperty("estimates").GetString() == "model",
                $"analysis-stages.json says how {id} estimates");
        }
    }

    [Fact]
    public void Knows_a_first_party_solution_by_what_the_contract_says()
    {
        // Which solutions start unticked is a judgement about a client's estate, so it is
        // declared rather than buried. Being wrong is cheap by design: the only consequence
        // is a box in the wrong position that somebody can move.
        var selection = Contracts.Read("analysis-stages").GetProperty("solutionSelection");
        var firstParty = selection.GetProperty("firstParty");

        static List<string> Values(JsonElement element, string name) =>
            [.. element.GetProperty(name).EnumerateArray().Select(value => value.GetString()!)];

        FirstPartySolutions.PublisherPrefixes.Should().BeEquivalentTo(Values(firstParty, "publisherPrefixes"));
        FirstPartySolutions.PublisherNameFragments.Should().BeEquivalentTo(Values(firstParty, "publisherNameContains"));
        FirstPartySolutions.UniqueNames.Should().BeEquivalentTo(Values(firstParty, "uniqueNames"));

        selection.GetProperty("firstPartyDefaultsToUnselected").GetBoolean().Should().BeTrue(
            "the picker unticks them by default and the contract is where that is written down");
    }

    [Fact]
    public void Tells_a_client_solution_from_one_of_Microsofts()
    {
        // The positive control matters more than the negatives here. A rule that called
        // everything first party would tick nothing by default, and the run would read an
        // empty estate while looking like it had asked.
        var theirs = new SolutionSummary("nwu_core", "Northwind Core", "1.0", false, "nwu", "Northwind BV", 140);

        FirstPartySolutions.IsFirstParty(theirs).Should().BeFalse();

        FirstPartySolutions.IsFirstParty(
            new SolutionSummary("msdyn_Sales", "Sales", "9.0", true, "msdyn", "Dynamics 365", 4000))
            .Should().BeTrue("the prefix is Microsoft's");

        FirstPartySolutions.IsFirstParty(
            new SolutionSummary("SomeThing", "Something", "1.0", true, "abc", "Microsoft Corporation", 10))
            .Should().BeTrue("the publisher names them even where the prefix does not");

        FirstPartySolutions.IsFirstParty(
            new SolutionSummary("Active", "Active", null, false, "nwu", "Northwind BV", null))
            .Should().BeTrue("the default solution holds everything customised outside a solution");
    }

    [Theory]
    [InlineData("CK_AnalysisRun_Mode", @"'(?<value>[a-zA-Z]+)'", "Mode")]
    [InlineData("CK_AnalysisRun_Status", @"'(?<value>[a-zA-Z]+)'", "Status")]
    [InlineData("CK_RunStage_Status", @"'(?<value>[a-zA-Z]+)'", "StageStatus")]
    [InlineData("CK_RunCommand_Command", @"'(?<value>[a-zA-Z]+)'", "Command")]
    public void Never_writes_a_value_a_check_constraint_would_refuse(string constraint, string pattern, string kind)
    {
        // The most expensive defect this product has shipped, twice, in one family.
        //
        // The web page asked for a run mode of "discover", which is a real mode in the
        // sibling product this page was ported from and not one here. The database refused
        // the insert on CK_AnalysisRun_Mode, the API threw, the response was not the shape
        // the page reads errors from, and the button did nothing at all, visibly, from the
        // day it was written. Nobody could have found it by reading either side: both were
        // internally consistent.
        //
        // The same shape was one line away a second time, when a "resume" command was added
        // and CK_RunCommand_Command allowed five values that did not include it.
        //
        // So: every literal the code writes into one of these columns has to be one the
        // constraint accepts. Scanned rather than listed, because a list here would be a
        // third copy of the same set.
        var allowed = ConstraintValues(constraint, pattern);

        allowed.Should().NotBeEmpty($"{constraint} has to be readable from the migrations for this to mean anything");

        var written = kind switch
        {
            // Anchored on the request that starts a run, not on the word "mode". A bare
            // mode: 'x' matches an administration screen's own state and reports a failure
            // about nothing, and a test that invents failures gets switched off.
            "Mode" => Literals(@"/runs`,\s*'POST',\s*\{\s*mode:\s*'(?<value>[a-zA-Z]+)'")
                // Named receivers, because a connection has a Mode too and its values are a
                // different set entirely. Matching the property name alone reported
                // "offlineZip" as an illegal run mode, which is a failure about nothing.
                .Concat(Literals(@"(?:run|state|request)\.Mode\s*(?:==|!=|is)\s*""(?<value>[a-zA-Z]+)""")),
            // Two ways in, because the store writes one of these through a helper and one
            // as a literal inside a VALUES list. The first version of this checked only the
            // helper, passed cleanly against the missing "resume" it was written to catch,
            // and had to be fixed before it meant anything.
            "Command" => Literals(@"QueueCommandAsync\([^,]+,\s*""(?<value>[a-zA-Z]+)""")
                .Concat(Literals(@"INTO ops\.RunCommand[^;]*?VALUES\s*\([^)]*?'(?<value>[a-zA-Z]+)'")),
            "Status" => Literals(@"SetRunStatusAsync\([^,]+,\s*""(?<value>[a-zA-Z]+)""")
                .Concat(Literals(@"Status = '(?<value>[a-zA-Z]+)'")),
            _ => Literals(@"StageOutcome\(""(?<value>[a-zA-Z]+)"""),
        };

        var refused = written.Distinct(StringComparer.Ordinal)
            .Where(value => !allowed.Contains(value, StringComparer.Ordinal))
            .ToList();

        refused.Should().BeEmpty(
            $"{constraint} would refuse these, and a refused insert surfaces as a button that does nothing "
            + "rather than as an error anybody can read");
    }

    /// <summary>The literals one check constraint permits, read out of the migrations.</summary>
    private static List<string> ConstraintValues(string constraint, string pattern)
    {
        var root = Directory.GetParent(Solution())!.FullName;
        var values = new List<string>();

        // Later migrations rebuild constraints that earlier ones created, so the last
        // definition wins, exactly as it does in the database.
        foreach (var file in Directory
            .EnumerateFiles(Path.Combine(root, "db", "migrations"), "*.sql")
            .OrderBy(file => file, StringComparer.Ordinal))
        {
            var text = File.ReadAllText(file);

            foreach (Match definition in Regex.Matches(
                text,
                $@"CONSTRAINT {constraint} CHECK \([^)]*IN\s*\((?<values>[^)]*)\)",
                RegexOptions.Singleline,
                TimeSpan.FromSeconds(5)))
            {
                values.Clear();

                values.AddRange(Regex
                    .Matches(definition.Groups["values"].Value, pattern, RegexOptions.None, TimeSpan.FromSeconds(5))
                    .Select(match => match.Groups["value"].Value));
            }
        }

        return values;
    }

    /// <summary>Every literal in the product's own source that one pattern picks out.</summary>
    private static List<string> Literals(string pattern)
    {
        var found = new List<string>();

        var roots = new[] { Solution(), Path.Combine(Directory.GetParent(Solution())!.FullName, "src", "web", "src") };

        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var file in Directory
                .EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                .Where(file => file.EndsWith(".cs", StringComparison.Ordinal) || file.EndsWith(".tsx", StringComparison.Ordinal))
                .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
            {
                found.AddRange(Regex
                    .Matches(File.ReadAllText(file), pattern, RegexOptions.None, TimeSpan.FromSeconds(5))
                    .Select(match => match.Groups["value"].Value));
            }
        }

        return found;
    }

    [Fact]
    public void Writes_no_sentence_into_a_document_without_asking_the_translator()
    {
        // A German assessment carried sixteen lines of English in the section that explains
        // what the product did not look at, because those sentences were composed in code
        // and never went near a bundle. Several more were hardcoded in the renderer beside
        // labels that were translated, so the same page mixed two languages.
        //
        // Every lookup carries its English as a fallback, which is what made this invisible:
        // the document renders perfectly in all six languages and is only right in one.
        //
        // A sentence, not a word. Short literals are column keys, format strings, file
        // names and units, and demanding a translation for each would make the test noise
        // that somebody eventually deletes.
        var source = File.ReadAllText(
            Path.Combine(Solution(), "PowerPete.Analyzer.Export", "AssessmentReportPdf.cs"));

        const char Quote = (char)34;

        var untranslated = new List<string>();

        foreach (var line in source.Split('\n'))
        {
            var text = line.Trim();

            // Comments are prose on purpose.
            if (text.StartsWith("//", StringComparison.Ordinal) || text.StartsWith("///", StringComparison.Ordinal)) continue;

            foreach (Match literal in Regex.Matches(
                text, @"""(?<value>[^""\\]{25,})""", RegexOptions.None, TimeSpan.FromSeconds(5)))
            {
                var value = literal.Groups["value"].Value;

                // An interpolation hole can hold a string of its own, and a naive scan for
                // a quoted span cuts through the middle of one and reports the wreckage as
                // an untranslated sentence. Unbalanced braces are what that looks like.
                if (value.Count(character => character == '{') != value.Count(character => character == '}')) continue;

                // What is left once the interpolation holes are taken out. A layout string
                // of three values and two separators is long and has no words in it, and
                // there is nothing for anybody to translate.
                var words = Regex.Replace(
                    value, @"\{[^}]*\}", " ", RegexOptions.None, TimeSpan.FromSeconds(5));

                if (words.Count(char.IsLetter) < 20) continue;

                // Four words or more is a sentence. Below that it is a label, and labels
                // are looked up by the key beside them.
                if (words.Trim().Count(char.IsWhiteSpace) < 3) continue;

                // A key, which is the thing being looked up rather than the thing printed.
                if (value.Contains('.', StringComparison.Ordinal) && !value.Contains(' ', StringComparison.Ordinal)) continue;

                // Inside a lookup: Text["key", "the English"] or Rules["key", "..."]. The
                // English fallback is exactly where a sentence belongs.
                var before = text[..literal.Index];

                if (before.Contains("Text[", StringComparison.Ordinal)
                    || before.Contains("Rules[", StringComparison.Ordinal))
                {
                    continue;
                }

                // A continuation of one. A long fallback is written over several lines and
                // only the first carries the bracket.
                if (text.StartsWith("+ \"", StringComparison.Ordinal)
                    || text.StartsWith(Quote))
                {
                    continue;
                }

                untranslated.Add(value.Length <= 70 ? value : value[..70] + "...");
            }
        }

        untranslated.Should().BeEmpty(
            "every sentence in the report has to be looked up, or it renders in English inside a document "
            + "written in another language and nothing fails while it does");
    }

    /// <summary>Every environment variable name the container definition sets.</summary>
    private static List<string> WrittenByTheInfrastructure()
    {
        var root = Directory.GetParent(Solution())!.FullName;
        var bicep = File.ReadAllText(Path.Combine(root, "infra", "modules", "containerapps.bicep"));

        // An environment entry, which is a name with a value or a secret reference beside it.
        // Matching a bare name picked up the registry sku and the container names, which are
        // not settings and would have to be excused one by one as the file grew.
        return Regex
            .Matches(
                bicep,
                @"\{ name: '(?<name>[A-Za-z0-9_]+)', (?:value|secretRef):",
                RegexOptions.None,
                TimeSpan.FromSeconds(5))
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

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
