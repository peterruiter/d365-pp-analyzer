namespace PowerPete.Analyzer.Export;

using System.Globalization;
using PowerPete.Analyzer.Analysis;
using PowerPete.Analyzer.Domain;
using PowerPete.Analyzer.Domain.Localization;
using PowerPete.Analyzer.Export.Pdf;
using Syncfusion.Drawing;
using Syncfusion.Pdf;
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
    /// <param name="IsDemonstration">Whether this is the sample estate, which the cover has to say out loud.</param>
    /// <param name="Maturity">Capability axis, score and who said so. Empty until somebody scores it.</param>
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
        IReadOnlyList<(string Axis, decimal? Score, string? Evidence)> Maturity,
        string Language = "en",
        bool IsDemonstration = false)
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
        // Content padding, so a heading has air under the header rule instead of sitting on
        // it. It was zero, which put the first line of every page hard against the line
        // above it, and a page whose text touches its own furniture reads as broken
        // whatever else is right about it.
        using var surface = new PdfSurface(theme, headerHeight: 46f, footerHeight: 40f, contentPadding: 26f);

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
        WrittenSection(flow, model, "delivery", model.Text["report.delivery", "Delivery and ALM"]);

        Roadmap(flow, model);
        Backlog(flow, model);
        NotAssessed(flow, model);

        // The three sections nothing in an estate can produce. They were declared in the
        // report model and never emitted, so a consultant could write them and the document
        // would not carry them.
        FunctionalMaturity(flow, model);

        WrittenSection(flow, model, "readiness", model.Text["report.readiness", "Readiness for change"]);

        WrittenSection(flow, model, "scenarios", model.Text["report.scenarios", "Scenarios"]);

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

    /// <summary>
    /// The running footer: where it came from, the page number and the wordmark.
    /// </summary>
    /// <remarks>
    /// The page number is a composite field rather than a counter this code keeps, because
    /// the renderer adds pages of its own whenever a table outgrows one and a number kept
    /// here would not know about them.
    /// </remarks>
    /// <param name="graphics">The page.</param>
    /// <param name="size">Its size.</param>
    /// <param name="theme">Fonts.</param>
    /// <param name="model">What to write.</param>
    private static void Footer(PdfGraphics graphics, SizeF size, PdfTheme theme, Model model)
    {
        var style = new TextStyle { Size = 7.5f, Colour = CapgeminiBrand.Muted };
        var right = size.Width - 48f;

        graphics.DrawLine(PdfTheme.Pen(CapgeminiBrand.Line, 0.5f),
            new PointF(48f, size.Height - 30f), new PointF(right, size.Height - 30f));

        var origin = model.IsDemonstration
            ? model.Text["report.footerSample", "sample data"]
            : $"{model.Text["report.readVia", "read via"]} {model.ExtractionMode}";

        graphics.DrawString(
            $"{model.EngagementName}  ·  {model.ProducedUtc:yyyy-MM-dd}  ·  {origin}",
            theme.Font(style), PdfTheme.Brush(CapgeminiBrand.Muted),
            new RectangleF(48f, size.Height - 22f, size.Width - 96f - 150f, 12f));

        var markHeight = FooterWordmarkWidth * CapgeminiBrand.WordmarkAspect;
        using var mark = new MemoryStream(CapgeminiBrand.WordmarkBlue, writable: false);
        graphics.DrawImage(
            new PdfBitmap(mark), right - FooterWordmarkWidth, size.Height - 24f, FooterWordmarkWidth, markHeight);

        var numbers = new PdfCompositeField(
            theme.Font(style),
            PdfTheme.Brush(style.Colour),
            model.Text["report.page", "Page"] + " {0} " + model.Text["report.of", "of"] + " {1}",
            new PdfAutomaticField[] { new PdfPageNumberField(), new PdfPageCountField() })
        {
            // Bounds and the draw point are added together rather than one replacing the
            // other, so the box carries the width and the point stays at zero on that axis.
            StringFormat = PdfTheme.Format(style with { Align = PdfTextAlignment.Right }),
            Bounds = new RectangleF(0, 0, right - FooterWordmarkWidth - 10f, 12f)
        };

        numbers.Draw(graphics, new PointF(0, size.Height - 22f));
    }

    /// <summary>Width of the wordmark reversed out of the cover band.</summary>
    private const float CoverWordmarkWidth = 132f;

    /// <summary>Width of the wordmark in the running footer.</summary>
    private const float FooterWordmarkWidth = 64f;

    /// <summary>
    /// The cover.
    /// </summary>
    /// <remarks>
    /// Carries the same furniture as the other reports in this suite, because a client
    /// receives two of them from the same account team in the same month and a document
    /// that does not look like its siblings reads as a document from somebody else. The
    /// wordmark reversed out of the navy band, the product name and the tagline opposite
    /// it, a blue rule under the band, and the wordmark again in blue in every footer.
    ///
    /// None of that was here. The palette and the typeface were already right and the
    /// wordmark was already embedded in the assembly; nothing ever drew it.
    /// </remarks>
    /// <param name="surface">The document.</param>
    /// <param name="model">What to write.</param>
    private static void Cover(PdfSurface surface, Model model)
    {
        var page = surface.AddPageWithoutHeader();
        var graphics = page.Graphics;
        var width = surface.PageWidth;

        const float band = 360f;

        // Started above the top of the content area so the band swallows the running
        // header. The header is a document template and templates paint underneath page
        // content, so the cover simply covers it; the alternative is a stray "Demonstration
        // estate" floating above the navy, which is what the first render did.
        var top = -surface.HeaderHeight;

        graphics.DrawRectangle(
            PdfTheme.Brush(CapgeminiBrand.DarkBlue), new RectangleF(0, top, width, band - top));

        // The rule under the band, which is the one piece of the brand that is load bearing
        // rather than decorative: it is what makes the band read as a header rather than as
        // a block of colour somebody left there.
        graphics.DrawRectangle(PdfTheme.Brush(CapgeminiBrand.Blue), new RectangleF(0, band, width, 3.5f));

        var markHeight = CoverWordmarkWidth * CapgeminiBrand.WordmarkAspect;
        using (var mark = new MemoryStream(CapgeminiBrand.WordmarkWhite, writable: false))
        {
            graphics.DrawImage(new PdfBitmap(mark), 48f, 40f, CoverWordmarkWidth, markHeight);
        }

        // The product and the tagline, right aligned against the wordmark, exactly as the
        // sibling reports set them.
        // Line height forced to one and the boxes left generous. Syncfusion draws nothing
        // at all when a line box does not fit its rectangle: no exception, no partial
        // glyph, an empty space where the text was. The default line height of 1.45 puts a
        // 9pt string in a 14pt box over the edge by a fraction of a point, and the first
        // render of this cover came out with the wordmark present and the product name and
        // the tagline simply absent.
        var eyebrow = new TextStyle
        {
            Size = 9,
            Colour = CapgeminiBrand.LightBlue,
            Align = PdfTextAlignment.Right,
            LineHeight = 1f
        };

        graphics.DrawString(
            model.Text["report.coverEyebrow", "Power Platform solution analysis"].ToUpperInvariant(),
            surface.Theme.Font(eyebrow), PdfTheme.Brush(eyebrow.Colour),
            new RectangleF(48f, 42f, width - 96f, 24f), PdfTheme.Format(eyebrow));

        var tagline = new TextStyle
        {
            Size = 15,
            Colour = CapgeminiBrand.White,
            Align = PdfTextAlignment.Right,
            LineHeight = 1f
        };

        graphics.DrawString(
            CapgeminiBrand.Tagline,
            surface.Theme.Font(tagline), PdfTheme.Brush(tagline.Colour),
            new RectangleF(48f, 60f, width - 96f, 32f), PdfTheme.Format(tagline));

        graphics.DrawString(model.Text["report.platform", "Power Platform"],
            surface.Theme.Font(new TextStyle { Size = 30, Bold = true, Colour = CapgeminiBrand.White }),
            PdfTheme.Brush(CapgeminiBrand.White), new PointF(48f, 160f));

        graphics.DrawString(model.Text["report.title", "Solution assessment"],
            surface.Theme.Font(new TextStyle { Size = 30, Bold = true, Colour = CapgeminiBrand.LightBlue }),
            PdfTheme.Brush(CapgeminiBrand.LightBlue), new PointF(48f, 200f));

        graphics.DrawString(model.ClientName ?? model.EngagementName,
            surface.Theme.Font(new TextStyle { Size = 14, Colour = CapgeminiBrand.White }),
            PdfTheme.Brush(CapgeminiBrand.White), new PointF(48f, 254f));

        // The extraction mode is on the cover rather than in an appendix. A report from an
        // offline export and one from a live connection answer different questions, and
        // whoever forwards this will not read the appendix.
        graphics.DrawString(
            $"{model.ProducedUtc:d MMMM yyyy}  ·  {model.Text["report.readVia", "read via"]} {model.ExtractionMode}"
            + $"  ·  {model.SolutionNames.Count} {model.Text["report.solutionsWord", "solution(s)"]}",
            surface.Theme.Font(new TextStyle { Size = 10, Colour = CapgeminiBrand.LightBlue }),
            PdfTheme.Brush(CapgeminiBrand.LightBlue), new PointF(48f, 282f));

        graphics.DrawString(
            model.Text["report.preparedBy", "Prepared by Capgemini. Estimates are ranges from a rule catalogue, not a quotation."],
            surface.Theme.Font(new TextStyle { Size = 8, Colour = CapgeminiBrand.Muted }),
            PdfTheme.Brush("#9AA6BF"), new PointF(48f, 304f));

        // Said on the cover, in a colour nobody scrolls past. A demonstration report that
        // reaches a client without this line is a report about an estate they do not have,
        // with their account team's logo on it.
        if (model.IsDemonstration)
        {
            graphics.DrawRectangle(
                PdfTheme.Brush(CapgeminiBrand.Terracotta), new RectangleF(48f, band - 42f, width - 96f, 26f));

            graphics.DrawString(
                model.Text["report.sampleBanner", "Sample data. No client estate, environment or person is represented here."].ToUpperInvariant(),
                surface.Theme.Font(new TextStyle { Size = 8, Bold = true, Colour = CapgeminiBrand.White, LineHeight = 1f }),
                PdfTheme.Brush(CapgeminiBrand.White), new PointF(58f, band - 35f));
        }

        CoverFigures(surface, page, model, band + 34f);
    }

    /// <summary>
    /// The four numbers, on the cover, under the band.
    /// </summary>
    /// <remarks>
    /// The sibling reports put their headline figures on page one and this one had an empty
    /// half page under the band, which reads as a document somebody did not finish rather
    /// than as a cover.
    ///
    /// Four numbers and no more, because the argument of this product is on the next page
    /// and a cover crowded with figures invites somebody to quote one without it. The last
    /// of the four is how many checks could not run, which belongs beside the other three
    /// for exactly that reason: a findings count means nothing without it.
    /// </remarks>
    /// <param name="surface">The document.</param>
    /// <param name="page">The cover.</param>
    /// <param name="model">What to write.</param>
    /// <param name="top">Where to start, under the band.</param>
    private static void CoverFigures(PdfSurface surface, PdfPage page, Model model, float top)
    {
        var flow = new Flow(surface, 48f, surface.PageWidth - 96f, top, page, measuring: false);

        var figures = new (string Label, string Value, string Note, string Accent)[]
        {
            (model.Text["report.componentsRead", "Components read"],
             model.Score.ComponentsTotal.ToString("N0", Culture),
             $"{model.SolutionNames.Count} {model.Text["report.solutionsWord", "solution(s)"]}",
             CapgeminiBrand.Blue),

            (model.Text["report.findings", "Findings"],
             model.Findings.Count.ToString("N0", Culture),
             model.Score.LowCodeShare is { } share
                 ? $"{share.ToString("P0", Culture)} {model.Text["report.lowCode", "Low code"].ToLowerInvariant()}"
                 : string.Empty,
             CapgeminiBrand.LightBlue),

            (model.Text["report.estimatedEffort", "Estimated effort"],

             // Formatted against Culture rather than by plain interpolation. An
             // interpolated hole uses the current culture, so this line came out as
             // "342,00-1.324 h" on a machine set to Dutch and would have printed
             // differently depending on which region the container happened to run in.
             string.Create(Culture, $"{model.Score.TotalLowHours:N0}–{model.Score.TotalHighHours:N0} h"),
             model.Text["report.aRange", "a range, not a quotation"],
             CapgeminiBrand.Turquoise),

            (model.Text["report.notAssessed", "Not assessed"],
             $"{model.Score.NotAssessed.Count} / {RuleCatalogue.All.Count}",
             model.Text["report.checksThatCouldNotRun", "checks that could not run"],
             CapgeminiBrand.Terracotta)
        };

        flow.Row(
            row =>
            {
                foreach (var (label, value, note, accent) in figures)
                {
                    row.Relative(1, cell => cell.Panel(
                        panel =>
                        {
                            panel.Text(label.ToUpperInvariant(), new TextStyle
                            {
                                Size = 7.5f,
                                Colour = CapgeminiBrand.Muted,
                                LetterSpacing = 0.4f,
                                LineHeight = 1.2f
                            });

                            panel.Text(value, new TextStyle
                            {
                                // Sized so the widest of the four, an hour range, stays on
                                // one line. A card that wraps is taller than its neighbours
                                // and the row stops reading as a row.
                                Size = 16,
                                Bold = true,
                                Colour = CapgeminiBrand.DarkBlue,
                                LineHeight = 1.25f
                            }, paddingTop: 5f);

                            panel.Text(note, new TextStyle
                            {
                                Size = 7.5f,
                                Colour = CapgeminiBrand.Muted,
                                LineHeight = 1.3f
                            }, paddingTop: 3f);
                        },
                        background: CapgeminiBrand.Background,
                        padding: 12f,
                        accent: accent,
                        accentWidth: 3.5f));
                }
            },
            gap: 12f);

        CoverStatement(flow, model);
    }

    /// <summary>
    /// What the report says, and the worst of what it found, under the figures.
    /// </summary>
    /// <remarks>
    /// The cover used to stop after the four figures and leave most of the page white,
    /// which reads as a document somebody abandoned rather than as a cover. The sibling
    /// reports carry their argument on page one and so does this one now.
    ///
    /// The paragraph is the consultant's, where there is one. Falling back to the
    /// generated sentence rather than to a prompt is deliberate: a prompt is the right
    /// thing on the page that asks for it and the wrong thing on a cover a client is
    /// forwarded, where it reads as an unfinished draft.
    /// </remarks>
    /// <param name="flow">The cover.</param>
    /// <param name="model">What to write.</param>
    private static void CoverStatement(Flow flow, Model model)
    {
        flow.Text(model.Text["report.whatThisSays", "What this report says"].ToUpperInvariant(),
            new TextStyle { Size = 8, Bold = true, Colour = CapgeminiBrand.Muted, LetterSpacing = 0.5f },
            paddingTop: 26f);

        var written = model.Written.TryGetValue("managementSummary", out var paragraph)
            && !string.IsNullOrWhiteSpace(paragraph)
                ? paragraph
                : null;

        // The first paragraph only. The rest of it is on the management summary page and a
        // cover that reprints the whole thing gives a reader no reason to turn over.
        var opening = written?.Split(["\n\n"], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
            ?? string.Create(Culture,
                $"{model.Score.ComponentsTotal} components across {model.SolutionNames.Count} solution(s), "
                + $"{model.Findings.Count} findings, and {model.Score.NotAssessed.Count} checks that could not run. "
                + $"Nothing in this document was written by hand: every finding below came from a rule with its "
                + $"detection, its reasoning and its estimate declared in a catalogue.");

        flow.Text(opening, new TextStyle { Size = 10, LineHeight = 1.5f }, paddingTop: 8f);

        var candidates = model.Findings
            .Where(entry => entry.Finding.Severity <= Severity.High)
            .OrderBy(entry => entry.Finding.Severity)
            .ThenByDescending(entry => entry.Estimate.HighHours)
            .Take(6)
            .ToList();

        if (candidates.Count == 0) return;

        // As many as fit, measured rather than assumed.
        //
        // A cover cannot overflow. Six rows fit this estate in English and did not fit it
        // once the paragraph above them ran to five lines: the last row landed alone on
        // page two, under a running header, which is a worse page than the one it was
        // trying to avoid. The paragraph is a consultant's and its length is not knowable
        // from here, so the count is measured against the room left rather than chosen.
        var worst = candidates;

        while (worst.Count > 1 && flow.Measure(probe => WorstTable(probe, model, worst)) > flow.Remaining)
        {
            worst = worst[..^1];
        }

        if (flow.Measure(probe => WorstTable(probe, model, worst)) > flow.Remaining) return;

        WorstTable(flow, model, worst);
    }

    /// <summary>The worst findings, as a table. Drawn twice: once to measure, once for real.</summary>
    /// <param name="flow">Where to write.</param>
    /// <param name="model">What to write.</param>
    /// <param name="worst">The rows.</param>
    private static void WorstTable(
        Flow flow,
        Model model,
        IReadOnlyList<(Finding Finding, Estimate Estimate, DiscoveredComponent? Component)> worst)
    {
        flow.Text(model.Text["report.theWorstOfIt", "The worst of it"].ToUpperInvariant(),
            new TextStyle { Size = 8, Bold = true, Colour = CapgeminiBrand.Muted, LetterSpacing = 0.5f },
            paddingTop: 22f);

        flow.Table(table =>
        {
            // The severity column carries the longest word in several languages
            // ("Schweregrad"), and at one unit it broke mid-word into "SCHWEREGR / AD".
            table.Columns(3, 7, 6, 4);
            table.Header(
                [
                    model.Text["report.severity", "Severity"],
                    model.Text["report.finding", "Finding"],
                    model.Text["report.component", "Component"],
                    model.Text["report.hours", "Hours"]
                ],
                [false, false, false, true]);

            foreach (var entry in worst)
            {
                table.Cell(SeverityLabel(model, entry.Finding.Severity), colour: SeverityColour(entry.Finding.Severity));
                table.Cell(RuleName(model, entry.Finding));
                table.Cell(entry.Finding.ComponentName ?? model.Text["report.solutionWide", "solution wide"]);
                table.Cell(
                    string.Create(Culture, $"{entry.Estimate.LowHours:0.#}–{entry.Estimate.HighHours:0.#}"),
                    right: true);
            }
        });
    }

    /// <summary>
    /// Opens a section.
    /// </summary>
    /// <remarks>
    /// Reserves rather than breaks. Every heading used to start a new page, which is a
    /// defensible rule for a document whose sections are all long and this one's are not:
    /// it produced pages carrying a heading, four lines and a hand's width of white, and
    /// twenty-three pages where the content justified about fifteen.
    ///
    /// The reserve is what keeps a heading attached to something. If a third of the page
    /// remains the section starts here; if it does not, it starts on the next one, and
    /// either way a heading is never the last thing on a page.
    /// </remarks>
    /// <param name="flow">Where to write.</param>
    /// <param name="text">The heading.</param>
    /// <param name="startsPage">
    /// For the few sections that genuinely open a chapter and should not share a page with
    /// the tail of the one before them.
    /// </param>
    private static void Heading(Flow flow, string text, bool startsPage = false)
    {
        if (startsPage)
        {
            flow.Break();
        }
        else
        {
            flow.Gap(26f);
            flow.Reserve(210f);
        }

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
        Heading(flow, model.Text["report.howToRead", "How to read this"], startsPage: true);

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

        WrittenBlock(flow, model, "managementSummary");
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

        // CountedByCraft, not ByCraft. Configuration and content are in the table below and
        // never in the circle, which is the whole argument of the ratio definition printed
        // above it -- but ByCraft also carries the pro code components that do not count
        // toward the ratio, and drawing those inside a circle labelled with the ratio makes
        // the picture disagree with the number in the middle of it. It did: 56 percent of
        // slices around 66 percent of text.
        DonutChart(flow,
            [
                (model.Text["report.lowCode", "Low code"], model.Score.CountedByCraft.GetValueOrDefault("lowCode")),
                (model.Text["report.proCode", "Pro code"], model.Score.CountedByCraft.GetValueOrDefault("proCode")),
                (model.Text["report.external", "External"], model.Score.CountedByCraft.GetValueOrDefault("external"))
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
        Heading(flow, model.Text["report.findings", "Findings"], startsPage: true);

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
                        table.Cell(SeverityLabel(model, entry.Finding.Severity), colour: SeverityColour(entry.Finding.Severity));
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
        Heading(flow, model.Text["report.roadmap", "Roadmap"], startsPage: true);

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

        // The sections nobody wrote, named once, here.
        //
        // They are left out of the body now rather than printed as empty boxes with an
        // instruction to the consultant inside them, which read as a draft sent by
        // mistake. Left out is not the same as unmentioned: a reader who wonders why there
        // is no readiness section should be able to find out that nobody wrote one, and
        // the page that already explains what was and was not looked at is where that
        // belongs.
        var unwritten = WrittenSections
            .Where(section => Written(model, section.Id) is null)
            .Select(section => model.Text[section.Key, section.Fallback])
            .ToList();

        if (unwritten.Count > 0)
        {
            flow.Text(
                $"{model.Text["report.notWritten", "Written by a consultant, and not written for this engagement"]}: "
                + $"{string.Join(", ", unwritten)}.",
                new TextStyle { Size = 9, Colour = CapgeminiBrand.Muted }, paddingTop: 8f);
        }
    }

    /// <summary>
    /// The sections of this report that no amount of reading an estate can produce.
    /// </summary>
    /// <remarks>
    /// Held in one place because two things need the same list and must not disagree: the
    /// body, which prints each one only where somebody wrote it, and the method section,
    /// which names the ones nobody did.
    /// </remarks>
    private static readonly (string Id, string Key, string Fallback)[] WrittenSections =
    [
        ("managementSummary", "report.managementSummary", "Management summary"),
        ("delivery", "report.delivery", "Delivery and ALM"),
        ("functionalMaturity", "report.functionalMaturity", "Functional maturity"),
        ("readiness", "report.readiness", "Readiness for change"),
        ("scenarios", "report.scenarios", "Scenarios")
    ];

    /// <summary>
    /// The capability scores, as a shape and as prose.
    /// </summary>
    /// <remarks>
    /// The radar goes above the writing rather than below it. It is the thing a reader looks
    /// at first in this section, and the paragraph underneath is there to say what the shape
    /// means, which only works in that order.
    ///
    /// Where nobody has scored anything the chart is left out entirely rather than drawn
    /// empty. A radar with no polygon in it looks like a rendering fault, and the written
    /// block below already says plainly that nobody has done this.
    /// </remarks>
    /// <param name="flow">Where it goes.</param>
    /// <param name="model">What to write.</param>
    private static void FunctionalMaturity(Flow flow, Model model)
    {
        var scored = model.Maturity.Any(axis => axis.Score is not null);

        // Nothing scored and nothing written is no section. The chart was already left out
        // when nobody had scored anything, on the grounds that a radar with no polygon in
        // it looks like a rendering fault, and the written block underneath used to carry
        // the explanation. It no longer prints one, so without this the section would be a
        // heading, a rule, and nothing at all.
        if (!scored && Written(model, "functionalMaturity") is null) return;

        Heading(flow, model.Text["report.functionalMaturity", "Functional maturity"]);

        if (scored)
        {
            RadarChart(flow,
                [.. model.Maturity.Select(axis => (AxisLabel(model, axis.Axis), axis.Score))],
                model.Text["report.maturityScale", "Scored nought to five, by a consultant, from interviews and demonstrations. Nothing here is inferred from the estate."]);

            // Who said so, where anybody wrote it down. A score with no provenance is the
            // most quotable figure in the document and the easiest to have made up.
            foreach (var axis in model.Maturity.Where(axis => !string.IsNullOrWhiteSpace(axis.Evidence)))
            {
                flow.Text($"{AxisLabel(model, axis.Axis)}: {axis.Evidence}",
                    new TextStyle { Size = 9, Colour = CapgeminiBrand.Muted }, paddingTop: 3f);
            }
        }

        WrittenBlock(flow, model, "functionalMaturity");
    }

    /// <summary>An axis in the reader's language, or its own identifier.</summary>
    /// <param name="model">The document.</param>
    /// <param name="axis">The axis identifier from the contract.</param>
    private static string AxisLabel(Model model, string axis) => model.Text["axis." + axis, axis];

    /// <summary>What a consultant wrote for a section, or null where nobody wrote anything.</summary>
    /// <param name="model">The report.</param>
    /// <param name="id">Which section.</param>
    private static string? Written(Model model, string id) =>
        model.Written.TryGetValue(id, out var text) && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;

    /// <summary>
    /// A section that only a person can write, printed only where a person has.
    /// </summary>
    /// <remarks>
    /// This used to print the section with its heading and a bordered panel saying nobody
    /// had written it, on the argument that a report missing its scenarios because nobody
    /// noticed is worse than one saying the scenarios are missing.
    ///
    /// That argument was about the wrong reader. A document that goes to a client with
    /// three empty boxes in it and an instruction to the consultant in each one does not
    /// read as candid, it reads as a draft somebody sent by mistake, and the client cannot
    /// act on the prompt because the prompt is not addressed to them.
    ///
    /// The honesty is kept where it belongs: the method section at the back names the
    /// sections nobody wrote, once, in a document that already explains what was and was
    /// not looked at. Nothing is silently dropped; it is just not dropped into the middle
    /// of the argument.
    /// </remarks>
    /// <param name="flow">Where to write.</param>
    /// <param name="model">The report.</param>
    /// <param name="id">Which section.</param>
    /// <param name="title">Its heading.</param>
    private static void WrittenSection(Flow flow, Model model, string id, string title)
    {
        if (Written(model, id) is not { } text) return;

        Heading(flow, title);
        Body(flow, text, paddingTop: 12f);
    }

    /// <summary>The written half of a hybrid section, under the generated numbers.</summary>
    /// <param name="flow">Where to write.</param>
    /// <param name="model">The report.</param>
    /// <param name="id">Which section.</param>
    private static void WrittenBlock(Flow flow, Model model, string id)
    {
        if (Written(model, id) is { } text) Body(flow, text, paddingTop: 12f);
    }

    /// <summary>
    /// A severity in the reader's language.
    /// </summary>
    /// <remarks>
    /// The enum's own name was printed straight into the table, so a German report listed
    /// its worst findings as "Critical" under a column headed "Schweregrad". Every other
    /// word on the page was translated, which made the untranslated one look deliberate.
    /// </remarks>
    /// <param name="model">The report.</param>
    /// <param name="severity">Which severity.</param>
    private static string SeverityLabel(Model model, Severity severity)
    {
        var name = severity.ToString();

        return model.Text[
            $"report.severity.{char.ToLowerInvariant(name[0])}{name[1..]}",
            name];
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
