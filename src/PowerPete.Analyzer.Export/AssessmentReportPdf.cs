namespace PowerPete.Analyzer.Export;

using System.Globalization;
using PowerPete.Analyzer.Analysis;
using PowerPete.Analyzer.Domain;
using PowerPete.Analyzer.Domain.Localization;
using PowerPete.Analyzer.Export.Pdf;
using Syncfusion.Drawing;
using Syncfusion.Pdf.Graphics;

/// <summary>
/// The document a client reads.
/// </summary>
/// <remarks>
/// Follows report-model.json section by section, and only the sections marked generated are
/// built here. The written ones take a consultant's text and print an empty placeholder with
/// its prompt when nobody has written any, rather than being quietly dropped: a report missing
/// its scenarios because nobody noticed is worse than one saying the scenarios are missing.
///
/// The caveats are on page two, before the numbers. A reader who reaches a total before
/// reaching a warning has already decided what the total means.
/// </remarks>
public sealed partial class AssessmentReportPdf
{
    /// <summary>What the report needs.</summary>
    /// <param name="EngagementName">Whose estate.</param>
    /// <param name="ClientName">Who it is for.</param>
    /// <param name="RunId">Which run.</param>
    /// <param name="ProducedUtc">When.</param>
    /// <param name="ExtractionMode">How it was read.</param>
    /// <param name="Identity">Who the connection authenticated as.</param>
    /// <param name="SolutionNames">What was in scope.</param>
    /// <param name="Score">The numbers.</param>
    /// <param name="Findings">Findings with their estimates.</param>
    /// <param name="Customisation">The components by customisation chart data.</param>
    /// <param name="Roadmap">The roadmap items.</param>
    /// <param name="Written">Consultant text, keyed by report section id.</param>
    /// <param name="Language">The locale the document is written in. English when absent.</param>
    public sealed record Model(
        string EngagementName,
        string? ClientName,
        Guid RunId,
        DateTime ProducedUtc,
        string ExtractionMode,
        string? Identity,
        IReadOnlyList<string> SolutionNames,
        RunScore Score,
        IReadOnlyList<(Finding Finding, Estimate Estimate, DiscoveredComponent? Component)> Findings,
        IReadOnlyList<CustomisationRow> Customisation,
        IReadOnlyList<RoadmapItem> Roadmap,
        IReadOnlyDictionary<string, string> Written,
        string Language = "en")
    {
        /// <summary>
        /// The words this document is written in.
        /// </summary>
        /// <remarks>
        /// Built once with the model rather than passed through every method that
        /// writes a label. Every lookup carries its English, so a key nobody has
        /// translated renders in English rather than leaving a hole on page four.
        /// </remarks>
        internal Localiser Text { get; } = new Localiser("report", Language);

        /// <summary>The rule catalogue, in the same language.</summary>
        /// <remarks>
        /// A second namespace because a rule name is content and a column heading is
        /// chrome. They are corrected by different people at different times, and the
        /// finding text is the half a client actually reads.
        /// </remarks>
        internal Localiser Rules { get; } = new Localiser("finding", Language);
    }

    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    /// <summary>Worst first, which is the order every severity list in this product reads in.</summary>
    private static readonly string[] SeverityOrder = ["critical", "high", "medium", "low", "info"];

    /// <summary>The roadmap's three bands, nearest work first.</summary>
    private static readonly string[] RoadmapBands = ["unclutter", "accelerate", "innovate"];

    /// <summary>Builds the report.</summary>
    /// <param name="model">What to write.</param>
    public static byte[] Build(Model model)
    {
        ArgumentNullException.ThrowIfNull(model);

        SyncfusionLicence.Register();

        using var theme = new PdfTheme();
        using var surface = new PdfSurface(theme, headerHeight: 46f, footerHeight: 34f);

        surface.Frame(
            header: (graphics, size) => Header(graphics, size, theme, model),
            footer: (graphics, size) => Footer(graphics, size, theme, model));

        Cover(surface, model);

        var flow = surface.Start(48f, 48f);

        ReadThisFirst(flow, model);
        ManagementSummary(flow, model);
        Estate(flow, model);
        CraftAndComplexity(flow, model);
        Lifecycle(flow, model);
        Findings(flow, model);

        // Delivery sits with the findings rather than at the back. The findings say what the
        // estate looks like; only the team can say why, and the two read as one argument.
        WrittenSection(flow, model, "delivery", model.Text["report.delivery", "Delivery and ALM"],
            "Describe the deployment path from a developer's machine to production, in the words the team used. "
            + "The findings tell you what the estate looks like; only they can tell you why.");

        Roadmap(flow, model);
        Backlog(flow, model);
        NotAssessed(flow, model);

        // The three sections nothing in an estate can produce. They were declared in the
        // report model and never emitted, so a consultant could write them and the document
        // would not carry them.
        WrittenSection(flow, model, "functionalMaturity", model.Text["report.functionalMaturity", "Functional maturity"],
            "Score each capability nought to five from what you saw and heard. Say who told you, per axis.");

        WrittenSection(flow, model, "readiness", model.Text["report.readiness", "Readiness for change"],
            "Score from the interviews. If you did not interview anybody, leave this section out rather than "
            + "filling it in from impressions.");

        WrittenSection(flow, model, "scenarios", model.Text["report.scenarios", "Scenarios"],
            "Name the scenarios the client is actually choosing between, in their words. For each: what it gives them, what it needs from them, and what worries you.");

        Method(flow, model);

        return surface.Save();
    }

    private static void Header(PdfGraphics graphics, SizeF size, PdfTheme theme, Model model)
    {
        graphics.DrawString(model.EngagementName,
            theme.Font(new TextStyle { Size = 8, Colour = CapgeminiBrand.Muted }),
            PdfTheme.Brush(CapgeminiBrand.Muted), new PointF(48f, 24f));

        graphics.DrawLine(PdfTheme.Pen(CapgeminiBrand.Line, 0.5f),
            new PointF(48f, 42f), new PointF(size.Width - 48f, 42f));
    }

    private static void Footer(PdfGraphics graphics, SizeF size, PdfTheme theme, Model model)
    {
        var style = new TextStyle { Size = 7.5f, Colour = CapgeminiBrand.Muted };

        graphics.DrawString(
            $"Produced {model.ProducedUtc:yyyy-MM-dd} from run {model.RunId.ToString()[..8]}. Read via {model.ExtractionMode}.",
            theme.Font(style), PdfTheme.Brush(CapgeminiBrand.Muted), new PointF(48f, size.Height - 24f));
    }

    private static void Cover(PdfSurface surface, Model model)
    {
        var page = surface.AddPage();
        var graphics = page.Graphics;
        var width = surface.PageWidth;

        graphics.DrawRectangle(PdfTheme.Brush(CapgeminiBrand.DarkBlue), new RectangleF(0, 0, width, 300f));

        graphics.DrawString(model.Text["report.platform", "Power Platform"],
            surface.Theme.Font(new TextStyle { Size = 30, Bold = true, Colour = "#FFFFFF" }),
            PdfTheme.Brush("#FFFFFF"), new PointF(48f, 120f));

        graphics.DrawString(model.Text["report.title", "Solution assessment"],
            surface.Theme.Font(new TextStyle { Size = 30, Bold = true, Colour = CapgeminiBrand.LightBlue }),
            PdfTheme.Brush(CapgeminiBrand.LightBlue), new PointF(48f, 160f));

        graphics.DrawString(model.ClientName ?? model.EngagementName,
            surface.Theme.Font(new TextStyle { Size = 14, Colour = "#FFFFFF" }),
            PdfTheme.Brush("#FFFFFF"), new PointF(48f, 220f));

        // The extraction mode is on the cover rather than in an appendix. A report from an
        // offline export and one from a live connection answer different questions, and
        // whoever forwards this will not read the appendix.
        graphics.DrawString(
            $"{model.ProducedUtc:d MMMM yyyy}  ·  read via {model.ExtractionMode}  ·  {model.SolutionNames.Count} solution(s)",
            surface.Theme.Font(new TextStyle { Size = 10, Colour = CapgeminiBrand.LightBlue }),
            PdfTheme.Brush(CapgeminiBrand.LightBlue), new PointF(48f, 250f));
    }

    private static void Heading(Flow flow, string text)
    {
        flow.Break();
        flow.Text(text, new TextStyle { Size = 18, Bold = true, Colour = CapgeminiBrand.DarkBlue });
        flow.Rule(CapgeminiBrand.LightBlue, paddingTop: 6f, thickness: 2f, width: 60f);
        flow.Gap(14f);
    }

    private static void Body(Flow flow, string text, float paddingTop = 8f) =>
        flow.Text(text, new TextStyle { Size = 10 }, paddingTop);

    /// <summary>
    /// The caveats, before anything else.
    /// </summary>
    /// <remarks>
    /// The single most important page in the document. Everything after it is a number, and a
    /// reader who does not know that a third of the checks never ran will read a short findings
    /// list as a clean bill of health.
    /// </remarks>
    private static void ReadThisFirst(Flow flow, Model model)
    {
        Heading(flow, model.Text["report.howToRead", "How to read this"]);

        if (model.Score.Caveats.Count == 0)
        {
            Body(flow, "Every check ran, every solution in the environment was analysed, and every finding was " +
                       "estimated individually. That is unusual and it is worth saying so.");
        }
        else
        {
            Body(flow, "This assessment has limits, and they are here rather than in an appendix.");

            foreach (var caveat in model.Score.Caveats)
            {
                flow.Panel(
                    background: "#FFFFFF",
                    border: CapgeminiBrand.Terracotta,
                    content: inner => inner.Text(caveat, new TextStyle { Size = 9.5f }),
                    paddingTop: 8f);
            }
        }

        flow.Panel(
            background: CapgeminiBrand.Line,
            border: CapgeminiBrand.Line,
            content: inner => inner.Text(
                "Every estimate in this report is a range with a reason attached. There is no single figure " +
                "anywhere, because a single figure is a decision somebody takes and owns rather than a number " +
                "a tool produced.",
                new TextStyle { Size = 9.5f }),
            paddingTop: 12f);
    }

    private static void ManagementSummary(Flow flow, Model model)
    {
        Heading(flow, model.Text["report.managementSummary", "Management summary"]);

        var worst = model.Findings
            .Where(entry => entry.Finding.Severity <= Severity.High)
            .OrderBy(entry => entry.Finding.Severity)
            .Take(3)
            .ToList();

        Body(flow, string.Create(Culture,
            $"{model.Score.ComponentsTotal} components across {model.SolutionNames.Count} solution(s). " +
            $"{model.Score.FindingsBySeverity.Values.Sum()} findings, estimated at " +
            $"{model.Score.TotalLowHours:0.#} to {model.Score.TotalHighHours:0.#} hours, plus " +
            $"{model.Score.FixedCostLowHours:0.#} to {model.Score.FixedCostHighHours:0.#} hours of per engagement cost."));

        if (worst.Count > 0)
        {
            Body(flow, "The three that matter most:");

            foreach (var entry in worst)
            {
                flow.Text($"·  {RuleName(model, entry.Finding)}: {entry.Finding.ComponentName ?? model.Text["report.solutionWide", "solution wide"]}",
                    new TextStyle { Size = 10, Bold = true }, paddingTop: 6f);
                flow.Text(RuleText(model, entry.Finding, "why", entry.Finding.Rule?.Why), new TextStyle { Size = 9.5f, Colour = CapgeminiBrand.Muted });
            }
        }

        WrittenBlock(flow, model, "managementSummary",
            "What were they trying to achieve, and does what you found help or block it? Name the one thing you would fix first and why.");
    }

    private static void Estate(Flow flow, Model model)
    {
        Heading(flow, model.Text["report.whatIsInTheEstate", "What is in the estate"]);

        BarChart(flow,
            [.. model.Score.ByDomain.OrderByDescending(pair => pair.Value).Select(pair => (pair.Key, (decimal)pair.Value))],
            value => value.ToString("0", Culture));

        flow.Table(table =>
        {
            table.Columns(3, 1);
            table.Header([model.Text["report.domain", "Domain"], model.Text["report.components", "Components"]], [false, true]);

            foreach (var entry in model.Score.ByDomain.OrderByDescending(pair => pair.Value))
            {
                table.Cell(entry.Key);
                table.Cell(entry.Value.ToString(Culture), right: true);
            }
        });
    }

    private static void CraftAndComplexity(Flow flow, Model model)
    {
        Heading(flow, model.Text["report.customisationAndComplexity", "Customisation and complexity"]);

        Body(flow, model.Score.LowCodeShare is null
            ? "No components counted toward the low code ratio, so there is no ratio to report."
            : string.Create(Culture, $"Low code is {model.Score.LowCodeShare:P0} of the components that count toward the ratio."));

        flow.Text(model.Score.RatioDefinition,
            new TextStyle { Size = 9, Colour = CapgeminiBrand.Muted }, paddingTop: 6f);

        // Only the three crafts that count toward the ratio. Configuration and content are
        // in the table below and never in the circle, which is the whole argument of the
        // ratio definition printed above it.
        DonutChart(flow,
            [
                (model.Text["report.lowCode", "Low code"], model.Score.ByCraft.GetValueOrDefault("lowCode")),
                (model.Text["report.proCode", "Pro code"], model.Score.ByCraft.GetValueOrDefault("proCode")),
                (model.Text["report.external", "External"], model.Score.ByCraft.GetValueOrDefault("external"))
            ],
            model.Score.LowCodeShare is { } share ? share.ToString("P0", Culture) : "—",
            model.Text["report.lowCode", "Low code"]);

        StackedBarChart(flow,
            [.. model.Customisation
                .Where(row => row.Total > 0)
                .OrderByDescending(row => row.Total)
                .Take(12)
                .Select(row => (row.Category, (IReadOnlyList<int>)[row.Simple, row.Medium, row.Complex, row.Unrated]))],
            [
                model.Text["report.simple", "Simple"],
                model.Text["report.medium", "Medium"],
                model.Text["report.complex", "Complex"],
                model.Text["report.unrated", "Unrated"]
            ]);

        flow.Table(table =>
        {
            table.Columns(3, 1, 1, 1, 1, 1);
            table.Header([model.Text["report.component", "Component"], model.Text["report.simple", "Simple"], model.Text["report.medium", "Medium"], model.Text["report.complex", "Complex"], model.Text["report.unrated", "Unrated"], model.Text["report.total", "Total"]],
                [false, true, true, true, true, true]);

            foreach (var row in model.Customisation.Take(20))
            {
                table.Cell(row.Category);
                table.Cell(row.Simple.ToString(Culture), right: true);
                table.Cell(row.Medium.ToString(Culture), right: true);
                table.Cell(row.Complex.ToString(Culture), right: true);
                table.Cell(row.Unrated.ToString(Culture), right: true,
                    colour: row.Unrated > 0 ? CapgeminiBrand.Muted : null);
                table.Cell(row.Total.ToString(Culture), right: true, bold: true);
            }
        }, paddingTop: 12f);

        var unrated = model.Customisation.Sum(row => row.Unrated);

        if (unrated > 0)
        {
            flow.Text(
                $"{unrated} components could not be measured and are counted as unrated rather than simple. " +
                "The two are different statements and folding one into the other flatters the estate.",
                new TextStyle { Size = 9, Colour = CapgeminiBrand.Muted }, paddingTop: 8f);
        }
    }

    private static void Lifecycle(Flow flow, Model model)
    {
        Heading(flow, model.Text["report.lifecyclePosition", "Lifecycle position"]);

        BarChart(flow,
            [.. model.Score.ByLifecycle.OrderByDescending(pair => pair.Value).Select(pair => (pair.Key, (decimal)pair.Value))],
            value => value.ToString("0", Culture));

        flow.Table(table =>
        {
            table.Columns(3, 1);
            table.Header([model.Text["report.position", "Position"], model.Text["report.components", "Components"]], [false, true]);

            foreach (var entry in model.Score.ByLifecycle.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                table.Cell(entry.Key);
                table.Cell(entry.Value.ToString(Culture), right: true);
            }
        });

        flow.Text(
            "Dated means supported and no longer where the platform is going. Deprecated means Microsoft has " +
            "announced removal. Where this report calls something dated rather than deprecated, that is a reading " +
            "of where the investment has gone rather than a Microsoft statement, and it is marked as such per component.",
            new TextStyle { Size = 9, Colour = CapgeminiBrand.Muted }, paddingTop: 10f);
    }

    private static void Findings(Flow flow, Model model)
    {
        Heading(flow, model.Text["report.findings", "Findings"]);

        // Worst first rather than largest first. The order of severity is the point, and
        // sorting by count would put a hundred low findings above two critical ones.
        BarChart(flow,
            [.. SeverityOrder
                .Where(level => model.Score.FindingsBySeverity.ContainsKey(level))
                .Select(level => (model.Text["severity." + level, level], (decimal)model.Score.FindingsBySeverity[level]))],
            value => value.ToString("0", Culture),
            colour: CapgeminiBrand.Blue);

        if (model.Score.DebtByDomain.Count > 0)
        {
            flow.Text(model.Text["report.debtByDomain", "Where the hours sit"],
                new TextStyle { Size = 11, Bold = true }, paddingTop: 16f);

            BarChart(flow,
                [.. model.Score.DebtByDomain.OrderByDescending(pair => pair.Value).Select(pair => (pair.Key, pair.Value))],
                value => string.Create(Culture, $"{value:0} h"));
        }

        foreach (var category in model.Findings
            .GroupBy(entry => entry.Finding.Rule?.Category ?? "other", StringComparer.Ordinal)
            .OrderBy(group => group.Min(entry => entry.Finding.Severity)))
        {
            flow.Together(inner =>
            {
                inner.Text(category.Key, new TextStyle { Size = 12, Bold = true, Colour = CapgeminiBrand.Blue });

                inner.Table(table =>
                {
                    table.Columns(1, 4, 2, 2);
                    table.Header([model.Text["report.severity", "Severity"], model.Text["report.finding", "Finding"], model.Text["report.component", "Component"], model.Text["report.hours", "Hours"]], [false, false, false, true]);

                    foreach (var entry in category.OrderBy(entry => entry.Finding.Severity).Take(25))
                    {
                        table.Cell(entry.Finding.Severity.ToString(), colour: SeverityColour(entry.Finding.Severity));
                        table.Cell(RuleName(model, entry.Finding));
                        table.Cell(entry.Finding.ComponentName ?? "solution wide");
                        table.Cell($"{entry.Estimate.LowHours:0.#}–{entry.Estimate.HighHours:0.#}", right: true);
                    }
                });

                if (category.Count() > 25)
                {
                    inner.Text($"and {category.Count() - 25} more, in the findings workbook.",
                        new TextStyle { Size = 9, Colour = CapgeminiBrand.Muted }, paddingTop: 4f);
                }
            }, paddingTop: 14f);
        }
    }

    private static void Roadmap(Flow flow, Model model)
    {
        Heading(flow, model.Text["report.roadmap", "Roadmap"]);

        var bands = RoadmapBuilder.BandProfile(model.Roadmap);

        Body(flow,
            "Each finding placed by what kind of problem it is rather than by what it costs. Most of them land " +
            "in unclutter, which is the correct shape for a debt assessment: an analyser finds debt, not new " +
            "capability.");

        // The grid before the tables. This is the page a client keeps after the rest is
        // filed, and thirty rows across three tables is not something anybody keeps.
        var rows = model.Roadmap
            .Select(item => item.Position.Row)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(row => row, StringComparer.Ordinal)
            .ToList();

        GridChart(flow, rows, RoadmapBands, (row, band) =>
        {
            var cell = model.Roadmap
                .Where(item => item.Position.Row == row && item.Position.Band == band)
                .ToList();

            return (cell.Sum(item => item.FindingCount), cell.Sum(item => item.HighHours));
        });

        foreach (var band in RoadmapBands)
        {
            var items = model.Roadmap.Where(item => item.Position.Band == band).ToList();
            if (items.Count == 0) continue;

            flow.Together(inner =>
            {
                inner.Text($"{band}  ({bands.GetValueOrDefault(band)} findings)",
                    new TextStyle { Size = 12, Bold = true, Colour = CapgeminiBrand.Blue });

                inner.Table(table =>
                {
                    table.Columns(4, 2, 2, 2);
                    table.Header([model.Text["report.item", "Item"], model.Text["report.row", "Row"], model.Text["report.column", "Column"], model.Text["report.hours", "Hours"]], [false, false, false, true]);

                    foreach (var item in items.OrderByDescending(item => item.HighHours))
                    {
                        table.Cell(item.Label);
                        table.Cell(item.Position.Row);
                        table.Cell(item.Position.Column);
                        table.Cell($"{item.LowHours:0.#}–{item.HighHours:0.#}", right: true);
                    }
                });
            }, paddingTop: 12f);
        }
    }

    private static void Backlog(Flow flow, Model model)
    {
        Heading(flow, model.Text["report.estimate", "Estimate"]);

        // A range per category rather than a bar to its midpoint. The estimate is a range,
        // and a chart that draws the middle of it states more confidence than the number
        // behind it has.
        RangeBarChart(flow,
            [.. model.Findings
                .GroupBy(entry => entry.Finding.Rule?.Category ?? "other", StringComparer.Ordinal)
                .OrderByDescending(group => group.Sum(entry => entry.Estimate.HighHours))
                .Select(group => (
                    group.Key,
                    group.Sum(entry => entry.Estimate.LowHours),
                    group.Sum(entry => entry.Estimate.HighHours)))]);

        flow.Table(table =>
        {
            table.Columns(3, 1, 2);
            table.Header([model.Text["report.category", "Category"], model.Text["report.findings", "Findings"], model.Text["report.hours", "Hours"]], [false, true, true]);

            foreach (var category in model.Findings
                .GroupBy(entry => entry.Finding.Rule?.Category ?? "other", StringComparer.Ordinal)
                .OrderByDescending(group => group.Sum(entry => entry.Estimate.HighHours)))
            {
                table.Cell(category.Key);
                table.Cell(category.Count().ToString(Culture), right: true);
                table.Cell($"{category.Sum(entry => entry.Estimate.LowHours):0.#}–{category.Sum(entry => entry.Estimate.HighHours):0.#}",
                    right: true);
            }

            table.Cell(model.Text["report.perEngagementCosts", "Per engagement costs"], bold: true, top: true);
            table.Cell("", right: true, top: true);
            table.Cell($"{model.Score.FixedCostLowHours:0.#}–{model.Score.FixedCostHighHours:0.#}", right: true, bold: true, top: true);

            table.Cell(model.Text["report.total", "Total"], bold: true);
            table.Cell("", right: true);
            table.Cell($"{model.Score.TotalLowHours + model.Score.FixedCostLowHours:0.#}–" +
                       $"{model.Score.TotalHighHours + model.Score.FixedCostHighHours:0.#}", right: true, bold: true);
        });

        var bandOnly = model.Findings.Count(entry => entry.Estimate.Layer == EstimateLayer.BandDefault);

        if (bandOnly > 0)
        {
            flow.Text(
                $"{bandOnly} of these were not estimated individually and carry the band for their rule. " +
                "A band is what you get before anybody has looked at the specific component.",
                new TextStyle { Size = 9, Colour = CapgeminiBrand.Muted }, paddingTop: 8f);
        }
    }

    private static void NotAssessed(Flow flow, Model model)
    {
        Heading(flow, model.Text["report.whatWasNotAssessed", "What was not assessed"]);

        if (model.Score.NotAssessed.Count == 0)
        {
            Body(flow, model.Text["report.everyCheckRan", "Every check in the catalogue ran."]);
            return;
        }

        Body(flow, string.Create(
            Culture,
            $"{model.Score.NotAssessed.Count} of {RuleCatalogue.All.Count} checks could not run. None of them is reported as passing anywhere in this report."));

        flow.Table(table =>
        {
            table.Columns(3, 5);
            table.Header([model.Text["report.check", "Check"], model.Text["report.whyNot", "Why not"]], [false, false]);

            foreach (var entry in model.Score.NotAssessed.OrderBy(entry => entry.RuleId, StringComparer.Ordinal))
            {
                table.Cell(entry.RuleId);
                table.Cell(entry.Reason);
            }
        }, paddingTop: 10f);
    }

    private static void Method(Flow flow, Model model)
    {
        Heading(flow, model.Text["report.howThisWasProduced", "How this was produced"]);

        flow.Table(table =>
        {
            table.Columns(2, 4);

            table.Cell(model.Text["report.run", "Run"], bold: true);
            table.Cell(model.RunId.ToString());
            table.Cell(model.Text["report.produced", "Produced"], bold: true);
            table.Cell(model.ProducedUtc.ToString("u", Culture));
            table.Cell(model.Text["report.readVia", "Read via"], bold: true);
            table.Cell(model.ExtractionMode);
            table.Cell(model.Text["report.authenticatedAs", "Authenticated as"], bold: true);
            table.Cell(model.Identity ?? "not recorded");
            table.Cell(model.Text["report.solutions", "Solutions"], bold: true);
            table.Cell(string.Join(", ", model.SolutionNames));
            table.Cell(model.Text["report.rules", "Rules"], bold: true);
            table.Cell($"{RuleCatalogue.All.Count} in the catalogue, {model.Score.NotAssessed.Count} not assessed");
        });

        flow.Text(
            "An identity matters here. A report produced under an administrator account is not evidence that a " +
            "least privileged integration could have produced the same one.",
            new TextStyle { Size = 9, Colour = CapgeminiBrand.Muted }, paddingTop: 10f);
    }

    /// <summary>
    /// A section only a consultant can write.
    /// </summary>
    /// <remarks>
    /// Prints the prompt when nobody has written anything, rather than dropping the section.
    /// A report missing its scenarios because nobody noticed is worse than one that says so on
    /// the page.
    /// </remarks>
    private static void WrittenSection(Flow flow, Model model, string id, string title, string prompt)
    {
        Heading(flow, title);
        WrittenBlock(flow, model, id, prompt);
    }

    private static void WrittenBlock(Flow flow, Model model, string id, string prompt)
    {
        if (model.Written.TryGetValue(id, out var text) && !string.IsNullOrWhiteSpace(text))
        {
            Body(flow, text, paddingTop: 12f);
            return;
        }

        flow.Panel(
            background: "#FFFFFF",
            border: CapgeminiBrand.Line,
            content: inner =>
            {
                inner.Text(model.Text["report.nobodyHasWritten", "Nobody has written this section."],
                    new TextStyle { Size = 9.5f, Bold = true, Colour = CapgeminiBrand.Terracotta });
                inner.Text(prompt, new TextStyle { Size = 9.5f, Colour = CapgeminiBrand.Muted }, paddingTop: 4f);
                inner.Text("Nothing generated it, and nothing will. This is the half of an assessment that " +
                           "comes from talking to people.",
                    new TextStyle { Size = 9, Colour = CapgeminiBrand.Muted }, paddingTop: 4f);
            },
            paddingTop: 12f);
    }

    private static string SeverityColour(Severity severity) => severity switch
    {
        Severity.Critical => CapgeminiBrand.DeepRed,
        Severity.High => CapgeminiBrand.Terracotta,
        Severity.Medium => CapgeminiBrand.Ink,
        _ => CapgeminiBrand.Muted
    };

    /// <summary>
    /// A rule's name in the document's language, or the catalogue's English.
    /// </summary>
    /// <remarks>
    /// Keyed on the rule id rather than on its English text, so correcting a sentence in the
    /// catalogue does not silently orphan five translations of it. A rule nobody has
    /// translated reads in English, which is a report a consultant can still hand over.
    /// </remarks>
    private static string RuleName(Model model, Finding finding) =>
        model.Rules[$"finding.{finding.RuleId}.name", finding.Rule?.Name ?? finding.RuleId];

    /// <summary>One of a rule's paragraphs, in the document's language.</summary>
    /// <param name="model">The document.</param>
    /// <param name="finding">Whose rule.</param>
    /// <param name="part">why or recommendation.</param>
    /// <param name="english">The catalogue's text, used when nothing has translated it.</param>
    private static string RuleText(Model model, Finding finding, string part, string? english) =>
        model.Rules[$"finding.{finding.RuleId}.{part}", english ?? string.Empty];
}
