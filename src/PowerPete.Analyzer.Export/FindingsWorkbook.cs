namespace PowerPete.Analyzer.Export;

using ClosedXML.Excel;
using PowerPete.Analyzer.Analysis;
using PowerPete.Analyzer.DevOps;
using PowerPete.Analyzer.Domain;
using PowerPete.Analyzer.Domain.Localization;

/// <summary>
/// The workbook a consultant filters in a workshop.
/// </summary>
/// <remarks>
/// Five sheets, and the order matters. Whoever opens this lands on the caveats before the
/// findings, because somebody who reads a short findings list without knowing a third of the
/// checks never ran will conclude the estate is clean.
///
/// Every sheet has a frozen header row and an autofilter, because the first thing anybody does
/// with this file is sort it and the second is filter it.
/// </remarks>
public sealed class FindingsWorkbook
{
    /// <summary>Everything one workbook needs.</summary>
    /// <param name="EngagementName">Whose estate.</param>
    /// <param name="RunId">Which run, so a figure can be traced back.</param>
    /// <param name="ProducedUtc">When.</param>
    /// <param name="ExtractionMode">How it was read. A report from an offline export and one from a live connection answer different questions.</param>
    /// <param name="Identity">Who the connection authenticated as.</param>
    /// <param name="Score">The numbers.</param>
    /// <param name="Findings">Findings with their estimates and components.</param>
    /// <param name="Components">The inventory.</param>
    /// <param name="Customisation">The components by customisation chart data.</param>
    /// <param name="Backlog">The work items as they would be created.</param>
    /// <param name="Language">The locale the document is written in. English when absent.</param>
    public sealed record Model(
        string EngagementName,
        Guid RunId,
        DateTime ProducedUtc,
        string ExtractionMode,
        string? Identity,
        RunScore Score,
        IReadOnlyList<(Finding Finding, Estimate Estimate, DiscoveredComponent? Component)> Findings,
        IReadOnlyList<(DiscoveredComponent Component, Complexity Complexity)> Components,
        IReadOnlyList<CustomisationRow> Customisation,
        IReadOnlyList<BacklogItem> Backlog,
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
        internal Localiser Text { get; } = new Localiser("inventory", Language);

        /// <summary>The rule catalogue, in the same language.</summary>
        /// <remarks>
        /// A second namespace because a rule name is content and a column heading is
        /// chrome. They are corrected by different people at different times, and the
        /// finding text is the half a client actually reads.
        /// </remarks>
        internal Localiser Rules { get; } = new Localiser("finding", Language);
    }

    /// <summary>Builds the workbook.</summary>
    /// <param name="model">What to write.</param>
    public static byte[] Build(Model model)
    {
        ArgumentNullException.ThrowIfNull(model);

        using var workbook = new XLWorkbook();

        WriteSummary(workbook, model);
        WriteFindings(workbook, model);
        WriteBacklog(workbook, model);
        WriteInventory(workbook, model);
        WriteCustomisation(workbook, model);
        WriteNotAssessed(workbook, model);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// The first sheet, and the one that keeps the rest honest.
    /// </summary>
    /// <remarks>
    /// The caveats sit above the numbers rather than below them. A reader who scrolls past a
    /// total to reach a warning has already decided what the total means.
    /// </remarks>
    private static void WriteSummary(XLWorkbook workbook, Model model)
    {
        var sheet = workbook.Worksheets.Add(model.Text["inventory.sheet.summary", "Read this first"]);
        var row = 1;

        sheet.Cell(row, 1).Value = model.Text["inventory.product", "Power Platform Solution Analyzer"];
        sheet.Cell(row, 1).Style.Font.Bold = true;
        sheet.Cell(row, 1).Style.Font.FontSize = 14;
        row += 2;

        void Pair(string label, object? value)
        {
            sheet.Cell(row, 1).Value = label;
            sheet.Cell(row, 1).Style.Font.Bold = true;
            sheet.Cell(row, 2).Value = value?.ToString() ?? "";
            row++;
        }

        Pair(model.Text["inventory.engagement", "Engagement"], model.EngagementName);
        Pair(model.Text["inventory.produced", "Produced"], model.ProducedUtc.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " UTC");
        Pair(model.Text["inventory.run", "Run"], model.RunId);
        Pair(model.Text["inventory.readVia", "Read via"], model.ExtractionMode);
        Pair(model.Text["inventory.authenticatedAs", "Authenticated as"], model.Identity ?? "not recorded");
        row++;

        if (model.Score.Caveats.Count > 0)
        {
            sheet.Cell(row, 1).Value = model.Text["inventory.caveats", "Before quoting anything in this workbook"];
            sheet.Cell(row, 1).Style.Font.Bold = true;
            sheet.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml(CapgeminiBrand.Terracotta);
            row++;

            foreach (var caveat in model.Score.Caveats)
            {
                sheet.Cell(row, 1).Value = caveat;
                sheet.Range(row, 1, row, 6).Merge().Style.Alignment.WrapText = true;
                sheet.Row(row).Height = 30;
                row++;
            }

            row++;
        }

        Pair(model.Text["inventory.components", "Components"], model.Score.ComponentsTotal);
        Pair(model.Text["inventory.lowCodeShare", "Low code share"], model.Score.LowCodeShare is null
            ? "nothing counted"
            : model.Score.LowCodeShare.Value.ToString("P0", System.Globalization.CultureInfo.InvariantCulture));

        sheet.Cell(row, 1).Value = model.Text["inventory.howCalculated", "How that is calculated"];
        sheet.Cell(row, 2).Value = model.Score.RatioDefinition;
        sheet.Range(row, 2, row, 8).Merge().Style.Alignment.WrapText = true;
        sheet.Row(row).Height = 45;
        row += 2;

        Pair(model.Text["inventory.sheet.findings", "Findings"], model.Score.FindingsBySeverity.Values.Sum());
        Pair(model.Text["inventory.checksThatCouldNotRun", "Checks that could not run"], model.Score.NotAssessed.Count);
        Pair(model.Text["inventory.estimatedEffort", "Estimated effort"], $"{model.Score.TotalLowHours:0.#} to {model.Score.TotalHighHours:0.#} hours");
        Pair(model.Text["inventory.plusFixedCosts", "Plus per engagement costs"], $"{model.Score.FixedCostLowHours:0.#} to {model.Score.FixedCostHighHours:0.#} hours");

        sheet.Cell(row, 1).Value = model.Text["inventory.noSingleFigure", "There is no single figure"];
        sheet.Cell(row, 2).Value =
            "Every estimate in this workbook is a range with a rationale. A midpoint is a decision somebody " +
            "takes and owns, not a number this tool produced.";
        sheet.Range(row, 2, row, 8).Merge().Style.Alignment.WrapText = true;
        sheet.Row(row).Height = 30;

        sheet.Column(1).Width = 32;
        sheet.Column(2).Width = 60;
    }

    private static void WriteFindings(XLWorkbook workbook, Model model)
    {
        var sheet = workbook.Worksheets.Add(model.Text["inventory.sheet.findings", "Findings"]);

        Header(sheet,
            model.Text["inventory.severity", "Severity"], model.Text["inventory.category", "Category"], model.Text["inventory.rule", "Rule"], model.Text["inventory.component", "Component"], model.Text["inventory.type", "Type"], model.Text["inventory.solution", "Solution"], model.Text["inventory.managed", "Managed"],
            model.Text["inventory.lowHours", "Low hours"], model.Text["inventory.highHours", "High hours"], model.Text["inventory.points", "Points"], model.Text["inventory.confidence", "Confidence"], model.Text["inventory.estimateFrom", "Estimate from"], model.Text["inventory.rationale", "Rationale"], model.Text["inventory.flagged", "Flagged"], model.Text["inventory.evidence", "Evidence"]);

        var row = 2;

        foreach (var entry in model.Findings
            .OrderBy(entry => entry.Finding.Severity)
            .ThenBy(entry => entry.Finding.RuleId, StringComparer.Ordinal))
        {
            sheet.Cell(row, 1).Value = entry.Finding.Severity.ToString();
            sheet.Cell(row, 1).Style.Font.FontColor = SeverityColour(entry.Finding.Severity);
            sheet.Cell(row, 2).Value = entry.Finding.Rule?.Category ?? "";
            sheet.Cell(row, 3).Value = RuleName(model, entry.Finding);
            sheet.Cell(row, 4).Value = entry.Finding.ComponentName ?? "(solution wide)";
            sheet.Cell(row, 5).Value = entry.Component?.Type?.Name ?? "";
            sheet.Cell(row, 6).Value = entry.Component?.SolutionUniqueName ?? "";
            sheet.Cell(row, 7).Value = entry.Component?.IsManaged == true ? "yes" : "";
            sheet.Cell(row, 8).Value = entry.Estimate.LowHours;
            sheet.Cell(row, 9).Value = entry.Estimate.HighHours;
            sheet.Cell(row, 10).Value = entry.Estimate.StoryPoints;
            sheet.Cell(row, 11).Value = entry.Estimate.Confidence.ToString();
            sheet.Cell(row, 12).Value = entry.Estimate.Layer switch
            {
                EstimateLayer.EngagementOverride => "a consultant",
                EstimateLayer.Model => "a model",
                _ => "band default"
            };
            sheet.Cell(row, 13).Value = entry.Estimate.Rationale;
            sheet.Cell(row, 14).Value = entry.Estimate.FlaggedReason ?? "";

            // The evidence goes in the sheet, not just the report. Somebody filtering this in
            // a workshop needs to be able to check a claim in the row they are looking at.
            sheet.Cell(row, 15).Value = string.Join("; ", entry.Finding.Evidence
                .Where(pair => pair.Value is not null)
                .Select(pair => $"{pair.Key}: {pair.Value}"));

            row++;
        }

        Finish(sheet, row, 15);
        sheet.Column(13).Width = 60;
        sheet.Column(15).Width = 70;
    }

    private static void WriteBacklog(XLWorkbook workbook, Model model)
    {
        var sheet = workbook.Worksheets.Add(model.Text["inventory.sheet.backlog", "Backlog"]);

        Header(sheet, model.Text["inventory.type", "Type"], model.Text["inventory.title", "Title"], model.Text["inventory.priority", "Priority"], model.Text["inventory.points", "Points"], model.Text["inventory.lowHours", "Low hours"], model.Text["inventory.highHours", "High hours"],
            model.Text["inventory.acceptance", "Acceptance criteria"], model.Text["inventory.testRequirement", "Test requirement"], model.Text["inventory.tags", "Tags"], model.Text["inventory.key", "Key"]);

        var row = 2;

        foreach (var item in model.Backlog)
        {
            sheet.Cell(row, 1).Value = item.Type;
            sheet.Cell(row, 2).Value = item.Title;
            sheet.Cell(row, 3).Value = item.Priority;
            sheet.Cell(row, 4).Value = item.StoryPoints;
            sheet.Cell(row, 5).Value = item.LowHours;
            sheet.Cell(row, 6).Value = item.HighHours;
            sheet.Cell(row, 7).Value = Strip(item.AcceptanceCriteria);
            sheet.Cell(row, 8).Value = item.TestRequirement;
            sheet.Cell(row, 9).Value = string.Join("; ", item.Tags);
            sheet.Cell(row, 10).Value = item.Key;

            // Indented by level, so the hierarchy reads without a pivot.
            if (item.ParentKey is not null) sheet.Cell(row, 2).Style.Alignment.Indent = 2;

            row++;
        }

        Finish(sheet, row, 10);
        sheet.Column(2).Width = 60;
        sheet.Column(7).Width = 70;
        sheet.Column(8).Width = 70;
    }

    private static void WriteInventory(XLWorkbook workbook, Model model)
    {
        var sheet = workbook.Worksheets.Add(model.Text["inventory.sheet.inventory", "Inventory"]);

        Header(sheet, model.Text["inventory.type", "Type"], model.Text["inventory.name", "Name"], model.Text["inventory.schemaName", "Schema name"], model.Text["inventory.solution", "Solution"], model.Text["inventory.domain", "Domain"], model.Text["inventory.craft", "Craft"], model.Text["inventory.lifecycle", "Lifecycle"],
            model.Text["inventory.complexity", "Complexity"], model.Text["inventory.inRatio", "In ratio"], model.Text["inventory.managed", "Managed"], model.Text["inventory.owner", "Owner"]);

        var row = 2;

        foreach (var (component, complexity) in model.Components
            .OrderBy(entry => entry.Component.TypeId, StringComparer.Ordinal)
            .ThenBy(entry => entry.Component.DisplayName, StringComparer.Ordinal))
        {
            sheet.Cell(row, 1).Value = component.Type?.Name ?? component.TypeId;
            sheet.Cell(row, 2).Value = component.DisplayName;
            sheet.Cell(row, 3).Value = component.SchemaName ?? "";
            sheet.Cell(row, 4).Value = component.SolutionUniqueName ?? "";
            sheet.Cell(row, 5).Value = component.Type?.Domain ?? "";
            sheet.Cell(row, 6).Value = component.Type?.Craft.ToString() ?? "";
            sheet.Cell(row, 7).Value = component.Type?.Lifecycle.ToString() ?? "";
            sheet.Cell(row, 8).Value = complexity.ToString();

            if (complexity == Complexity.Unrated)
            {
                // Said in the cell rather than left as a word nobody recognises. This is the
                // column people will ask about, and the answer is not "simple".
                sheet.Cell(row, 8).Style.Font.FontColor = XLColor.FromHtml(CapgeminiBrand.Muted);
                sheet.Cell(row, 8).GetComment().AddText("Not measurable from what this extraction reached. Not the same as simple.");
            }

            sheet.Cell(row, 9).Value = component.Type?.CountsTowardRatio == true ? "yes" : "";
            sheet.Cell(row, 10).Value = component.IsManaged ? "yes" : "";
            sheet.Cell(row, 11).Value = component.OwnerUpn ?? "";

            row++;
        }

        Finish(sheet, row, 11);
    }

    private static void WriteCustomisation(XLWorkbook workbook, Model model)
    {
        var sheet = workbook.Worksheets.Add(model.Text["inventory.sheet.customisation", "Customisation"]);

        sheet.Cell(1, 1).Value = model.Text["inventory.componentsByCustomisation", "Components by customisation"];
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(2, 1).Value =
            "Complexity is measured from each component's own attributes. Unrated means the measure was not " +
            "available, which is a different statement from simple and is kept in its own column for that reason.";
        sheet.Range(2, 1, 2, 6).Merge().Style.Alignment.WrapText = true;
        sheet.Row(2).Height = 30;

        var header = 4;
        var labels = new[] { model.Text["inventory.category", "Category"], model.Text["inventory.simple", "Simple"], model.Text["inventory.medium", "Medium"], model.Text["inventory.complex", "Complex"], model.Text["inventory.unrated", "Unrated"], model.Text["inventory.total", "Total"] };

        for (var column = 0; column < labels.Length; column++)
        {
            sheet.Cell(header, column + 1).Value = labels[column];
            sheet.Cell(header, column + 1).Style.Font.Bold = true;
            sheet.Cell(header, column + 1).Style.Fill.BackgroundColor = XLColor.FromHtml(CapgeminiBrand.DarkBlue);
            sheet.Cell(header, column + 1).Style.Font.FontColor = XLColor.White;
        }

        var row = header + 1;

        foreach (var entry in model.Customisation)
        {
            sheet.Cell(row, 1).Value = entry.Category;
            sheet.Cell(row, 2).Value = entry.Simple;
            sheet.Cell(row, 3).Value = entry.Medium;
            sheet.Cell(row, 4).Value = entry.Complex;
            sheet.Cell(row, 5).Value = entry.Unrated;
            sheet.Cell(row, 6).Value = entry.Total;
            row++;
        }

        sheet.Columns(1, 6).AdjustToContents();
    }

    private static void WriteNotAssessed(XLWorkbook workbook, Model model)
    {
        var sheet = workbook.Worksheets.Add(model.Text["inventory.sheet.notAssessed", "Not assessed"]);

        sheet.Cell(1, 1).Value = $"{model.Score.NotAssessed.Count} of {RuleCatalogue.All.Count} checks could not run";
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(2, 1).Value =
            "None of these is reported as passing anywhere in this workbook. A short findings list is not the " +
            "same thing as a clean estate.";
        sheet.Range(2, 1, 2, 5).Merge().Style.Alignment.WrapText = true;

        var header = 4;
        sheet.Cell(header, 1).Value = model.Text["inventory.rule", "Rule"];
        sheet.Cell(header, 2).Value = model.Text["inventory.whyItCouldNotRun", "Why it could not run"];
        sheet.Cell(header, 3).Value = model.Text["inventory.missingEvidence", "Missing evidence"];
        sheet.Range(header, 1, header, 3).Style.Font.Bold = true;

        var row = header + 1;

        foreach (var entry in model.Score.NotAssessed.OrderBy(entry => entry.RuleId, StringComparer.Ordinal))
        {
            sheet.Cell(row, 1).Value = entry.RuleId;
            sheet.Cell(row, 2).Value = entry.Reason;
            sheet.Cell(row, 3).Value = entry.MissingEvidence ?? "";
            row++;
        }

        sheet.Column(1).Width = 45;
        sheet.Column(2).Width = 80;
    }

    private static void Header(IXLWorksheet sheet, params string[] labels)
    {
        for (var column = 0; column < labels.Length; column++)
        {
            var cell = sheet.Cell(1, column + 1);
            cell.Value = labels[column];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml(CapgeminiBrand.DarkBlue);
        }
    }

    private static void Finish(IXLWorksheet sheet, int lastRow, int columns)
    {
        // Frozen header and an autofilter, because sorting and filtering are the first two
        // things anybody does with this file.
        sheet.SheetView.FreezeRows(1);

        if (lastRow > 2)
        {
            sheet.Range(1, 1, lastRow - 1, columns).SetAutoFilter();
        }

        sheet.Columns(1, columns).AdjustToContents(1, 200, 8, 55);
    }

    private static XLColor SeverityColour(Severity severity) => severity switch
    {
        Severity.Critical => XLColor.FromHtml(CapgeminiBrand.DeepRed),
        Severity.High => XLColor.FromHtml(CapgeminiBrand.Terracotta),
        Severity.Medium => XLColor.FromHtml(CapgeminiBrand.Ink),
        _ => XLColor.FromHtml(CapgeminiBrand.Muted)
    };

    /// <summary>
    /// Turns the HTML a work item carries back into something a spreadsheet cell can hold.
    /// </summary>
    /// <remarks>
    /// Crude on purpose. The acceptance criteria are built as HTML because that is what the
    /// Azure DevOps field renders, and a cell showing raw tags is worse than a cell showing
    /// slightly flattened text.
    /// </remarks>
    private static string Strip(string html) =>
        System.Text.RegularExpressions.Regex.Replace(
            html.Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
                .Replace("</p>", "\n", StringComparison.OrdinalIgnoreCase),
            "<[^>]+>", string.Empty).Trim();

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

}
