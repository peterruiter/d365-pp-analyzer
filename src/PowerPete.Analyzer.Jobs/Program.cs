using System.Globalization;
using System.Text.Json;
using PowerPete.Analyzer.Analysis;
using PowerPete.Analyzer.Data;
using PowerPete.Analyzer.Domain;
using PowerPete.Analyzer.Export;
using PowerPete.Analyzer.Extraction;

namespace PowerPete.Analyzer.Jobs;

/// <summary>
/// The worker, and a command line that works without one.
/// </summary>
/// <remarks>
/// Two jobs in one binary. <c>work</c> polls the command queue and runs the pipeline, which is
/// what the deployed container does. Everything else runs locally against a file and touches
/// no database, no environment and no credential, which is what makes the product testable on
/// a laptop on the first day rather than after the infrastructure is up.
/// </remarks>
public static class Program
{
    /// <summary>The one set of options the JSON output uses. Building these per call is not free.</summary>
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Entry point.</summary>
    /// <param name="args">The command and its arguments.</param>
    public static async Task<int> Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0)
        {
            Usage();
            return 1;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "analyse" or "analyze" => await AnalyseFileAsync(args).ConfigureAwait(false),
                "rules" => ShowRules(args),
                "components" => ShowComponents(),
                "work" => await WorkAsync().ConfigureAwait(false),
                "migrate-database" => await MigrateDatabaseAsync().ConfigureAwait(false),
                _ => Unknown(args[0])
            };
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    /// <summary>
    /// Applies the migrations, as the person running it.
    /// </summary>
    /// <remarks>
    /// Separate from the worker on purpose. The worker applies migrations when it starts so a
    /// container cannot be pointed at a database it does not know how to build, but a first
    /// run happens before any container exists and is done by somebody signed in as
    /// themselves, against a server that authenticates with Entra and has no password.
    /// </remarks>
    private static async Task<int> MigrateDatabaseAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("ANALYZER_SQL_CONNECTION");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine(
                "No connection string. Set ANALYZER_SQL_CONNECTION, which Deploy-Infrastructure.ps1 prints when it finishes.");
            return 1;
        }

        var results = await new DatabaseMigrator(connectionString)
            .ApplyAsync(Console.WriteLine, CancellationToken.None)
            .ConfigureAwait(false);

        foreach (var result in results)
        {
            Console.WriteLine(result.Applied
                ? $"  applied  {result.Name}  ({result.Batches} batch(es), {result.Elapsed.TotalSeconds:0.0}s)"
                : $"  already  {result.Name}");
        }

        Console.WriteLine($"{results.Count(result => result.Applied)} applied, {results.Count} in total.");
        return 0;
    }

    private static void Usage()
    {
        Console.WriteLine("""
            Power Platform Solution Analyzer

              analyse <solution.zip>            Reads an exported solution and reports what it finds.
                       [--json]                 No database, no environment, no credentials.
                       [--xlsx <file>]          Also writes the findings workbook.
                       [--pdf <file>]           Also writes the assessment report. Needs a
                                                Syncfusion licence key in SYNCFUSION_LICENCE_KEY.
              rules [category]                  Lists the rule catalogue.
              components                        Lists the component types, with craft and lifecycle.
              work                              Polls the command queue and runs the pipeline.
                                                This is what the deployed container does.
              migrate-database                  Applies the migrations in db/migrations to the
                                                database in ANALYZER_SQL_CONNECTION, as you.
                                                Run by ./build/Initialize-Database.ps1.

            Start with: analyse samples/SampleSolution.zip
            """);
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"'{command}' is not a command.");
        Usage();
        return 1;
    }

    /// <summary>
    /// Runs the offline path end to end against one file.
    /// </summary>
    /// <remarks>
    /// The command to reach for first, and the one to point at a client's export before
    /// anything else in this product has been deployed. It reads, resolves, applies every rule
    /// the file can answer, scores, and prints what it could not assess.
    ///
    /// Band defaults only. There is no model call here, deliberately: this command must work
    /// with no configuration at all.
    /// </remarks>
    private static async Task<int> AnalyseFileAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Which file? analyse <solution.zip>");
            return 1;
        }

        var path = args[1];
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"{path} does not exist.");
            return 1;
        }

        await using var file = File.OpenRead(path);
        var read = SolutionZipReader.Read(file);

        var resolved = ReferenceResolver.Resolve(read.Components, read.Links);

        var context = new AnalysisContext(
            read.Components,
            resolved.Links,
            resolved.Unresolved,
            // A file carries no metadata beyond itself, no checker results and no runtime
            // evidence. Saying so is what makes the not assessed list honest.
            new Reach([EvidenceSource.SolutionZip]),
            environmentRole: "unknown");

        var engine = new RuleEngine(AllHandlers());
        var outcome = engine.Run(context);

        // From the generated catalogue rather than a copy. A band table written twice produces
        // two different numbers for the same estate and nothing says which is right.
        var bands = EstimateCatalogue.Bands;

        var findings = outcome.Findings
            .Select(finding => (Finding: finding,
                Estimate: bands[finding.Rule?.EstimateBand ?? "none"].AsEstimate()))
            .ToList();

        var rater = new ComplexityRater(ComplexityRule.FromContract());
        var customisation = rater.ByCustomisation(read.Components);
        var roadmap = RoadmapBuilder.FromContract().Build(findings);

        var fixedCosts = EstimateCatalogue.FixedCosts
            .Select(cost => new FixedCost(cost.Id, cost.Name, cost.Low, cost.High))
            .ToList();

        var score = Scorer.Score(read.Components, findings, outcome.NotAssessed, fixedCosts,
            read.Solutions.Count, read.Solutions.Count);

        if (args.Contains("--json", StringComparer.Ordinal))
        {
            Console.WriteLine(JsonSerializer.Serialize(new { score, findings = findings.Select(entry => entry.Finding) },
                Indented));
            return 0;
        }

        Print(read, outcome, findings, score);

        var components = read.Components.Select(component => (component, rater.Rate(component))).ToList();
        await WriteExportsAsync(args, read, findings, score, components, customisation, roadmap.Items).ConfigureAwait(false);

        return 0;
    }

    /// <summary>
    /// Writes whichever deliverables were asked for.
    /// </summary>
    /// <remarks>
    /// A failure here does not fail the command. The analysis already printed and is the thing
    /// somebody ran this for; a missing licence key should cost them the PDF, not the answer.
    /// </remarks>
    private static async Task WriteExportsAsync(
        string[] args,
        SolutionZipReader.Result read,
        IReadOnlyList<(Finding Finding, Estimate Estimate)> findings,
        RunScore score,
        IReadOnlyList<(DiscoveredComponent Component, Complexity Complexity)> components,
        IReadOnlyList<CustomisationRow> customisation,
        IReadOnlyList<RoadmapItem> roadmap)
    {
        var xlsx = Argument(args, "--xlsx");
        var pdf = Argument(args, "--pdf");

        if (xlsx is null && pdf is null) return;

        var solution = read.Solutions.Count > 0 ? read.Solutions[0] : null;
        var withComponents = findings
            .Select(entry => (entry.Finding, entry.Estimate, (DiscoveredComponent?)null))
            .ToList();

        if (xlsx is not null)
        {
            try
            {
                var bytes = FindingsWorkbook.Build(new FindingsWorkbook.Model(
                    solution?.UniqueName ?? "Local file",
                    Guid.Empty,
                    DateTime.UtcNow,
                    "exported solution file",
                    null,
                    score,
                    withComponents,
                    components,
                    customisation,
                    []));

                await File.WriteAllBytesAsync(xlsx, bytes).ConfigureAwait(false);
                Console.WriteLine($"Wrote {xlsx}");
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                Console.Error.WriteLine($"The workbook was not written: {exception.Message}");
            }
        }

        if (pdf is not null)
        {
            try
            {
                var bytes = AssessmentReportPdf.Build(new AssessmentReportPdf.Model(
                    solution?.UniqueName ?? "Local file",
                    null,
                    Guid.Empty,
                    DateTime.UtcNow,
                    "exported solution file",
                    null,
                    [.. read.Solutions.Select(entry => entry.UniqueName)],
                    score,
                    withComponents,
                    customisation,
                    roadmap,
                    new Dictionary<string, string>(StringComparer.Ordinal)));

                await File.WriteAllBytesAsync(pdf, bytes).ConfigureAwait(false);
                Console.WriteLine($"Wrote {pdf}");
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                // Most often the licence key. Syncfusion does not fail without one, it stamps a
                // trial banner across every page, so the renderer refuses to start rather than
                // letting a watermarked document reach a client who was asked to sign it.
                Console.Error.WriteLine($"The report was not written: {exception.Message}");
            }
        }
    }

    private static string? Argument(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static void Print(
        SolutionZipReader.Result read,
        RuleEngine.Outcome outcome,
        List<(Finding Finding, Estimate Estimate)> findings,
        RunScore score)
    {
        var culture = CultureInfo.InvariantCulture;

        Console.WriteLine();
        Console.WriteLine($"Solution: {string.Join(", ", read.Solutions.Select(solution => solution.UniqueName))}");
        Console.WriteLine($"Components: {read.Components.Count}");
        Console.WriteLine(score.LowCodeShare is null
            ? "Low code ratio: nothing counted toward it."
            : string.Create(culture, $"Low code ratio: {score.LowCodeShare:P0} of {score.ByCraft.Values.Sum()} components"));

        Console.WriteLine();
        Console.WriteLine("Reads:");
        foreach (var entry in read.Reads)
        {
            Console.WriteLine(entry.Succeeded
                ? $"  {entry.ComponentTypeId,-30} {entry.RecordCount}"
                : $"  {entry.ComponentTypeId,-30} NOT READ: {entry.FailureReason}");
        }

        Console.WriteLine();
        Console.WriteLine($"Findings: {findings.Count}");

        foreach (var group in findings
            .GroupBy(entry => entry.Finding.Severity)
            .OrderBy(group => group.Key))
        {
            Console.WriteLine($"  {group.Key}: {group.Count()}");
        }

        Console.WriteLine();
        foreach (var entry in findings.OrderBy(entry => entry.Finding.Severity).Take(40))
        {
            Console.WriteLine($"  [{entry.Finding.Severity}] {entry.Finding.RuleId}: {entry.Finding.ComponentName ?? "solution"}");
        }

        Console.WriteLine();
        Console.WriteLine($"Not assessed: {outcome.NotAssessed.Count} of {RuleCatalogue.All.Count} rules");
        foreach (var entry in outcome.NotAssessed)
        {
            Console.WriteLine($"  {entry.RuleId}: {entry.Reason}");
        }

        if (score.Caveats.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Read this before quoting any of the above:");
            foreach (var caveat in score.Caveats) Console.WriteLine($"  - {caveat}");
        }
    }

    private static int ShowRules(string[] args)
    {
        var category = args.Length > 1 ? args[1] : null;

        foreach (var rule in RuleCatalogue.All
            .Where(rule => category is null || rule.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .OrderBy(rule => rule.Category, StringComparer.Ordinal)
            .ThenBy(rule => rule.Id, StringComparer.Ordinal))
        {
            Console.WriteLine($"{rule.Severity,-8} {rule.Id,-45} {rule.Name}");
            Console.WriteLine($"         {rule.Why}");
            if (rule.FalsePositive is not null) Console.WriteLine($"         Careful: {rule.FalsePositive}");
            Console.WriteLine();
        }

        return 0;
    }

    private static int ShowComponents()
    {
        foreach (var group in ComponentCatalogue.All.GroupBy(type => type.Domain).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            Console.WriteLine(group.Key.ToUpperInvariant());

            foreach (var type in group.OrderBy(type => type.Id, StringComparer.Ordinal))
            {
                var ratio = type.CountsTowardRatio ? "in ratio" : "";
                Console.WriteLine($"  {type.Id,-32} {type.Craft,-10} {type.Lifecycle,-12} {ratio}");
            }

            Console.WriteLine();
        }

        return 0;
    }

    /// <summary>
    /// Polls the command queue.
    /// </summary>
    /// <remarks>
    /// Deliberately not wired up yet. It needs a connection string, a Key Vault, a token
    /// provider and a composition root, and none of those exist. Failing with a sentence that
    /// says so beats a loop that silently does nothing and looks like a healthy worker on a
    /// dashboard.
    /// </remarks>
    private static async Task<int> WorkAsync()
    {
        var settings = WorkerSettings.Read(out var problems);

        if (settings is null)
        {
            // Checked before the loop rather than inside it. A worker that starts polling and
            // then finds it has no connection string looks healthy on a dashboard while doing
            // nothing at all.
            foreach (var problem in problems) Console.Error.WriteLine(problem);
            return 1;
        }

        using var stopping = new CancellationTokenSource();

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            stopping.Cancel();
        };

        return await new Worker(settings).RunAsync(stopping.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Every handler in the analysis assembly.
    /// </summary>
    /// <remarks>
    /// Found by reflection rather than listed. A hand written list is a list somebody forgets
    /// to add to, and a missing handler does not fail: its rule quietly reports as not
    /// implemented and the category looks smaller than it is.
    /// </remarks>
    private static IReadOnlyList<IRuleHandler> AllHandlers() =>
        [.. typeof(RuleEngine).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && type.IsAssignableTo(typeof(IRuleHandler)))
            .Select(type => (IRuleHandler)Activator.CreateInstance(type)!)];
}
