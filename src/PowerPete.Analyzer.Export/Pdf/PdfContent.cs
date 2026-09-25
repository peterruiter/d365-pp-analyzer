using Syncfusion.Drawing;
using Syncfusion.Pdf.Graphics;

namespace PowerPete.Analyzer.Export.Pdf;

/// <summary>A stretch of text in one style, part of a paragraph made of several.</summary>
/// <param name="Text">The words.</param>
/// <param name="Style">How they look.</param>
internal sealed record TextRun(string Text, TextStyle Style);

/// <summary>One cell of a table.</summary>
/// <param name="Value">What it says.</param>
/// <param name="Right">Right aligned, which every number in this report is.</param>
/// <param name="Bold">Used on a total row.</param>
/// <param name="Top">Carries a heavy rule above it, which marks a total.</param>
/// <param name="Colour">Overrides the ink colour, used to carry a feasibility band.</param>
/// <param name="Size">Point size, dropped on the wordier columns.</param>
internal sealed record TableCell(
    string Value,
    bool Right = false,
    bool Bold = false,
    bool Top = false,
    string? Colour = null,
    float Size = 8.5f);

/// <summary>
/// A table of text, drawn row by row so a long one runs onto the next page.
///
/// Deliberately not Syncfusion's own grid. The report's tables carry a specific look, a heavy
/// rule under the header and above a total, hairlines between rows and a band colour on a single
/// cell, and expressing that through a general purpose grid's styling model was more code than
/// drawing the rules directly.
/// </summary>
internal sealed class TableBuilder
{
    private readonly List<int> weights = [];
    private readonly List<TableCell[]> rows = [];
    private readonly List<TableCell> current = [];
    private bool headerDrawn;

    /// <summary>Relative widths, one per column.</summary>
    public void Columns(params int[] columnWeights)
    {
        weights.Clear();
        weights.AddRange(columnWeights);
    }

    /// <summary>The header row, in small capitals over a heavy rule.</summary>
    public void Header(string[] titles, bool[] rightAligned)
    {
        for (var index = 0; index < titles.Length; index++)
        {
            Add(new TableCell(titles[index].ToUpperInvariant(), rightAligned[index], Bold: true, Size: 7f, Colour: Brand.Muted));
        }

        headerDrawn = true;
    }

    /// <summary>One cell. Cells fill left to right and wrap to a new row at the last column.</summary>
    public void Cell(TableCell cell) => Add(cell);

    /// <inheritdoc cref="Cell(TableCell)"/>
    public void Cell(string value, bool right = false, bool bold = false, bool top = false, string? colour = null, float size = 8.5f) =>
        Add(new TableCell(value, right, bold, top, colour, size));

    private void Add(TableCell cell)
    {
        current.Add(cell);

        if (weights.Count > 0 && current.Count == weights.Count)
        {
            rows.Add([.. current]);
            current.Clear();
        }
    }

    /// <summary>Whether the first row is a header, which carries the heavy rule beneath it.</summary>
    internal bool HasHeader => headerDrawn;

    internal IReadOnlyList<TableCell[]> Rows
    {
        get
        {
            if (current.Count > 0)
            {
                rows.Add([.. current]);
                current.Clear();
            }

            return rows;
        }
    }

    internal float[] Widths(float total)
    {
        var sum = weights.Sum();
        return sum == 0 ? [total] : [.. weights.Select(weight => total * weight / (float)sum)];
    }
}

/// <summary>Paragraph and table drawing built on <see cref="Flow"/>.</summary>
internal static class FlowContent
{
    private const float CellPaddingRight = 4f;
    private const float CellPaddingVertical = 2.5f;

    /// <summary>
    /// A paragraph made of several styles, wrapped as one body of text.
    ///
    /// A single call to draw a string takes one font, so a sentence with two bold words in the
    /// middle of it cannot be drawn that way. This measures word by word, breaks the line when
    /// the next word will not fit, and draws each word in its own run's style. The alternative,
    /// breaking each run onto its own line, would turn the report's narrative paragraphs into a
    /// ragged list.
    /// </summary>
    public static void Rich(this Flow flow, IReadOnlyList<TextRun> runs, float paddingTop = 0f)
    {
        flow.Gap(paddingTop);

        var words = new List<(string Word, TextStyle Style, float Width)>();
        foreach (var run in runs)
        {
            if (string.IsNullOrEmpty(run.Text))
            {
                continue;
            }

            var font = flow.Theme.Font(run.Style);
            foreach (var word in Split(run.Text))
            {
                words.Add((word, run.Style, font.MeasureString(word).Width));
            }
        }

        if (words.Count == 0)
        {
            return;
        }

        // Break into lines first, so both passes agree on where the breaks fall.
        var lines = new List<List<(string Word, TextStyle Style, float Width)>>();
        var line = new List<(string Word, TextStyle Style, float Width)>();
        var used = 0f;

        foreach (var word in words)
        {
            var isSpace = string.IsNullOrWhiteSpace(word.Word);

            if (used + word.Width > flow.Width && line.Count > 0 && !isSpace)
            {
                lines.Add(line);
                line = [];
                used = 0f;
            }

            // A space that lands at the start of a wrapped line is dropped rather than indenting it.
            if (isSpace && line.Count == 0)
            {
                continue;
            }

            line.Add(word);
            used += word.Width;
        }

        if (line.Count > 0)
        {
            lines.Add(line);
        }

        foreach (var row in lines)
        {
            var height = row.Max(word => word.Style.Size * word.Style.LineHeight);

            if (flow.Measuring)
            {
                flow.Gap(height);
                continue;
            }

            flow.Reserve(height);

            var x = flow.Left;
            foreach (var (word, style, width) in row)
            {
                if (!string.IsNullOrWhiteSpace(word))
                {
                    flow.Page.Graphics.DrawString(
                        word,
                        flow.Theme.Font(style),
                        PdfTheme.Brush(style.Colour),
                        new PointF(x, flow.Y));
                }

                x += width;
            }

            flow.Gap(height);
        }
    }

    /// <summary>Splits into words, keeping the spaces so a line can be rebuilt exactly.</summary>
    private static IEnumerable<string> Split(string text)
    {
        var start = 0;
        for (var index = 1; index <= text.Length; index++)
        {
            if (index == text.Length || char.IsWhiteSpace(text[index]) != char.IsWhiteSpace(text[index - 1]))
            {
                yield return text[start..index];
                start = index;
            }
        }
    }

    /// <summary>Draws a table, one row at a time so a long one continues onto the next page.</summary>
    public static void Table(this Flow flow, Action<TableBuilder> build, float paddingTop = 0f)
    {
        flow.Gap(paddingTop);

        var builder = new TableBuilder();
        build(builder);

        var rows = builder.Rows;
        if (rows.Count == 0)
        {
            return;
        }

        var widths = builder.Widths(flow.Width);

        for (var index = 0; index < rows.Count; index++)
        {
            var isHeader = index == 0 && builder.HasHeader;
            DrawRow(flow, rows[index], widths, isHeader);
        }
    }

    private static void DrawRow(Flow flow, TableCell[] cells, float[] widths, bool isHeader)
    {
        var height = 0f;
        for (var index = 0; index < cells.Length && index < widths.Length; index++)
        {
            var style = StyleFor(cells[index]);
            height = Math.Max(height, flow.Theme.Measure(cells[index].Value, style, Math.Max(1f, widths[index] - CellPaddingRight)));
        }

        height = Math.Max(height, 10f) + (CellPaddingVertical * 2f);

        if (flow.Measuring)
        {
            flow.Gap(height);
            return;
        }

        flow.Reserve(height);

        var graphics = flow.Page.Graphics;
        var top = flow.Y;
        var x = flow.Left;

        for (var index = 0; index < cells.Length && index < widths.Length; index++)
        {
            var cell = cells[index];
            var style = StyleFor(cell);
            var width = Math.Max(1f, widths[index] - CellPaddingRight);

            graphics.DrawString(
                cell.Value,
                flow.Theme.Font(style),
                PdfTheme.Brush(style.Colour),
                new RectangleF(x, top + CellPaddingVertical, width, height - (CellPaddingVertical * 2f)),
                PdfTheme.Format(style));

            x += widths[index];
        }

        var right = flow.Left + widths.Sum();
        var bottom = top + height;

        // A heavy rule under the header and above a total, a hairline everywhere else. That is
        // the whole visual grammar of these tables.
        if (cells.Any(cell => cell.Top))
        {
            graphics.DrawLine(PdfTheme.Pen(Brand.DarkBlue, 0.8f), flow.Left, top, right, top);
        }

        graphics.DrawLine(
            isHeader ? PdfTheme.Pen(Brand.DarkBlue, 0.8f) : PdfTheme.Pen(Brand.Line, 0.4f),
            flow.Left,
            bottom,
            right,
            bottom);

        flow.Gap(height);
    }

    private static TextStyle StyleFor(TableCell cell) => new()
    {
        Size = cell.Size,
        Bold = cell.Bold,
        Colour = cell.Colour ?? Brand.Ink,
        LineHeight = 1.25f,
        Align = cell.Right ? PdfTextAlignment.Right : PdfTextAlignment.Left
    };
}
