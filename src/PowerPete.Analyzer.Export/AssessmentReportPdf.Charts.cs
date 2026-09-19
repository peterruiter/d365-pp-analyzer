namespace PowerPete.Analyzer.Export;

using System.Globalization;
using PowerPete.Analyzer.Export.Pdf;
using Syncfusion.Drawing;
using Syncfusion.Pdf.Graphics;

/// <summary>
/// The report's drawings.
/// </summary>
/// <remarks>
/// Drawn straight onto the page rather than pulled from a charting library. Five chart
/// shapes do not repay a dependency, and every one of these is rectangles, arcs and a row of
/// labels. A library would also bring its own fonts, and the container has none: the sibling
/// product learned that by shipping SVG whose text resolved against fonts that were not
/// there and rendered as nothing.
///
/// Every chart here is a second reading of a table that is already on the page. That is
/// deliberate. A chart is what somebody remembers and a table is what they check, and a
/// number that appears only in a picture is a number nobody can audit. Nothing is drawn that
/// is not also written down.
/// </remarks>
public sealed partial class AssessmentReportPdf
{
    /// <summary>
    /// The series colours, in the order they are handed out.
    /// </summary>
    /// <remarks>
    /// Capgemini's own palette rather than a generated ramp, and ordered so that adjacent
    /// series stay distinguishable in greyscale: a report gets printed, and two blues of the
    /// same weight become one blue on a laser printer.
    /// </remarks>
    private static readonly string[] Series =
    [
        CapgeminiBrand.Blue,
        CapgeminiBrand.LightBlue,
        CapgeminiBrand.Turquoise,
        CapgeminiBrand.Yellow,
        CapgeminiBrand.Teal,
        CapgeminiBrand.Terracotta,
        CapgeminiBrand.DeepRed,
        CapgeminiBrand.Muted
    ];

    private const float BarHeight = 13f;
    private const float BarGap = 7f;
    private const float LabelWidth = 132f;

    /// <summary>
    /// A horizontal bar per entry, longest first.
    /// </summary>
    /// <remarks>
    /// Horizontal rather than vertical because the labels are words like "integration" and
    /// "classicWorkflowBackground". Rotated labels under a column chart are unreadable at
    /// print size and a legend makes the reader look twice for something a row already says.
    /// </remarks>
    /// <param name="flow">Where it goes.</param>
    /// <param name="entries">Label and value, in the order they should read.</param>
    /// <param name="format">How to write the value beside the bar.</param>
    /// <param name="colour">The bar colour, or null to take one per row from the palette.</param>
    private static void BarChart(
        Flow flow,
        IReadOnlyList<(string Label, decimal Value)> entries,
        Func<decimal, string> format,
        string? colour = null)
    {
        if (entries.Count == 0) return;

        var largest = entries.Max(entry => entry.Value);

        // Everything at zero would divide by nothing and draw a row of full width bars,
        // which reads as the opposite of what it is.
        if (largest <= 0) return;

        var index = 0;

        foreach (var (label, value) in entries)
        {
            var share = (float)Math.Clamp(value / largest, 0m, 1m);
            var fill = colour ?? Series[index % Series.Length];
            index++;

            flow.Fixed(BarHeight, (graphics, box) =>
            {
                var textStyle = new PdfStringFormat(PdfTextAlignment.Left, PdfVerticalAlignment.Middle);
                var font = flow.Theme.Font(new TextStyle { Size = 8f });

                graphics.DrawString(label, font, PdfTheme.Brush(CapgeminiBrand.Ink),
                    new RectangleF(box.Left, box.Top, LabelWidth - 6f, BarHeight), textStyle);

                var trackLeft = box.Left + LabelWidth;
                var trackWidth = Math.Max(box.Width - LabelWidth - 54f, 10f);

                graphics.DrawRectangle(PdfTheme.Brush(CapgeminiBrand.Background),
                    new RectangleF(trackLeft, box.Top + 2f, trackWidth, BarHeight - 4f));

                graphics.DrawRectangle(PdfTheme.Brush(fill),
                    new RectangleF(trackLeft, box.Top + 2f, Math.Max(trackWidth * share, 1f), BarHeight - 4f));

                graphics.DrawString(format(value), font, PdfTheme.Brush(CapgeminiBrand.Muted),
                    new RectangleF(trackLeft + trackWidth + 6f, box.Top, 48f, BarHeight),
                    new PdfStringFormat(PdfTextAlignment.Right, PdfVerticalAlignment.Middle));
            }, paddingTop: BarGap);
        }

        flow.Gap(12f);
    }

    /// <summary>
    /// One bar per row, divided into its parts.
    /// </summary>
    /// <remarks>
    /// Each bar is the same length, because the question this chart answers is what a
    /// category is made of rather than how big it is. The size is in the table beside it.
    /// </remarks>
    /// <param name="flow">Where it goes.</param>
    /// <param name="rows">The label and its segments, in series order.</param>
    /// <param name="seriesNames">What each segment is, for the key.</param>
    private static void StackedBarChart(
        Flow flow,
        IReadOnlyList<(string Label, IReadOnlyList<int> Parts)> rows,
        IReadOnlyList<string> seriesNames)
    {
        if (rows.Count == 0) return;

        Key(flow, [.. seriesNames.Select((name, position) => (name, Series[position % Series.Length]))]);

        foreach (var (label, parts) in rows)
        {
            var total = parts.Sum();
            if (total <= 0) continue;

            flow.Fixed(BarHeight, (graphics, box) =>
            {
                var font = flow.Theme.Font(new TextStyle { Size = 8f });

                graphics.DrawString(label, font, PdfTheme.Brush(CapgeminiBrand.Ink),
                    new RectangleF(box.Left, box.Top, LabelWidth - 6f, BarHeight),
                    new PdfStringFormat(PdfTextAlignment.Left, PdfVerticalAlignment.Middle));

                var trackLeft = box.Left + LabelWidth;
                var trackWidth = Math.Max(box.Width - LabelWidth - 54f, 10f);
                var x = trackLeft;

                for (var position = 0; position < parts.Count; position++)
                {
                    var width = trackWidth * ((float)parts[position] / total);
                    if (width <= 0) continue;

                    graphics.DrawRectangle(PdfTheme.Brush(Series[position % Series.Length]),
                        new RectangleF(x, box.Top + 2f, width, BarHeight - 4f));

                    x += width;
                }

                graphics.DrawString(total.ToString(CultureInfo.InvariantCulture), font,
                    PdfTheme.Brush(CapgeminiBrand.Muted),
                    new RectangleF(trackLeft + trackWidth + 6f, box.Top, 48f, BarHeight),
                    new PdfStringFormat(PdfTextAlignment.Right, PdfVerticalAlignment.Middle));
            }, paddingTop: BarGap);
        }

        flow.Gap(12f);
    }

    /// <summary>
    /// A low to high range per row, on one shared scale.
    /// </summary>
    /// <remarks>
    /// The estimate is a range and drawing its midpoint as a bar would be the one chart in
    /// this document that states more confidence than the number behind it has. The bar is
    /// the range; where it starts matters as much as where it ends.
    /// </remarks>
    /// <param name="flow">Where it goes.</param>
    /// <param name="entries">Label, low and high.</param>
    private static void RangeBarChart(
        Flow flow,
        IReadOnlyList<(string Label, decimal Low, decimal High)> entries)
    {
        if (entries.Count == 0) return;

        var largest = entries.Max(entry => entry.High);
        if (largest <= 0) return;

        var index = 0;

        foreach (var (label, low, high) in entries)
        {
            var fill = Series[index % Series.Length];
            index++;

            var start = (float)Math.Clamp(low / largest, 0m, 1m);
            var end = (float)Math.Clamp(high / largest, 0m, 1m);

            flow.Fixed(BarHeight, (graphics, box) =>
            {
                var font = flow.Theme.Font(new TextStyle { Size = 8f });

                graphics.DrawString(label, font, PdfTheme.Brush(CapgeminiBrand.Ink),
                    new RectangleF(box.Left, box.Top, LabelWidth - 6f, BarHeight),
                    new PdfStringFormat(PdfTextAlignment.Left, PdfVerticalAlignment.Middle));

                var trackLeft = box.Left + LabelWidth;
                var trackWidth = Math.Max(box.Width - LabelWidth - 74f, 10f);

                graphics.DrawRectangle(PdfTheme.Brush(CapgeminiBrand.Background),
                    new RectangleF(trackLeft, box.Top + 2f, trackWidth, BarHeight - 4f));

                graphics.DrawRectangle(PdfTheme.Brush(fill),
                    new RectangleF(
                        trackLeft + (trackWidth * start),
                        box.Top + 2f,
                        Math.Max(trackWidth * (end - start), 1.5f),
                        BarHeight - 4f));

                graphics.DrawString(
                    string.Create(CultureInfo.InvariantCulture, $"{low:0}–{high:0}"),
                    font, PdfTheme.Brush(CapgeminiBrand.Muted),
                    new RectangleF(trackLeft + trackWidth + 6f, box.Top, 68f, BarHeight),
                    new PdfStringFormat(PdfTextAlignment.Right, PdfVerticalAlignment.Middle));
            }, paddingTop: BarGap);
        }

        flow.Gap(12f);
    }

    private const float DonutSize = 104f;

    /// <summary>
    /// The ratio, as the one picture in the document that is a circle.
    /// </summary>
    /// <remarks>
    /// A donut rather than a pie so the number can sit in the middle, which is the only part
    /// anybody quotes. Only the three crafts that count toward the ratio are in it;
    /// configuration and content are counted beside it and never inside, because a solution
    /// with four hundred columns and one plug-in is not ninety-nine percent low code.
    /// </remarks>
    /// <param name="flow">Where it goes.</param>
    /// <param name="slices">Label and value, in series order.</param>
    /// <param name="centre">The figure to write in the hole.</param>
    /// <param name="caption">What that figure is.</param>
    private static void DonutChart(
        Flow flow,
        IReadOnlyList<(string Label, int Value)> slices,
        string centre,
        string caption)
    {
        var total = slices.Sum(slice => slice.Value);
        if (total <= 0) return;

        flow.Fixed(DonutSize, (graphics, box) =>
        {
            var square = new RectangleF(box.Left, box.Top, DonutSize, DonutSize);
            var angle = -90f;
            var position = 0;

            foreach (var (_, value) in slices)
            {
                var sweep = 360f * ((float)value / total);

                if (sweep > 0)
                {
                    graphics.DrawPie(PdfTheme.Brush(Series[position % Series.Length]), square, angle, sweep);
                }

                angle += sweep;
                position++;
            }

            // The hole, punched by drawing the page colour over the middle. Syncfusion has no
            // donut primitive and a path with two figures is more code than a circle.
            const float ring = 26f;

            graphics.DrawEllipse(PdfTheme.Brush(CapgeminiBrand.White), new RectangleF(
                square.Left + ring, square.Top + ring, DonutSize - (ring * 2), DonutSize - (ring * 2)));

            graphics.DrawString(centre, flow.Theme.Font(new TextStyle { Size = 15f, Bold = true }), PdfTheme.Brush(CapgeminiBrand.Ink),
                new RectangleF(square.Left, square.Top + (DonutSize / 2) - 21f, DonutSize, 24f),
                new PdfStringFormat(PdfTextAlignment.Center, PdfVerticalAlignment.Middle));

            graphics.DrawString(caption, flow.Theme.Font(new TextStyle { Size = 7f }), PdfTheme.Brush(CapgeminiBrand.Muted),
                new RectangleF(square.Left, square.Top + (DonutSize / 2) + 4f, DonutSize, 11f),
                new PdfStringFormat(PdfTextAlignment.Center, PdfVerticalAlignment.Middle));

            // The key sits beside the circle rather than under it, because the circle is
            // square and leaves the whole right half of the line empty.
            var keyTop = box.Top + 10f;

            for (var index = 0; index < slices.Count; index++)
            {
                var (label, value) = slices[index];
                var share = (decimal)value / total;

                graphics.DrawRectangle(PdfTheme.Brush(Series[index % Series.Length]),
                    new RectangleF(box.Left + DonutSize + 18f, keyTop + 2f, 7f, 7f));

                graphics.DrawString(
                    string.Create(CultureInfo.InvariantCulture, $"{label}  {value}  ({share:P0})"),
                    flow.Theme.Font(new TextStyle { Size = 8f }), PdfTheme.Brush(CapgeminiBrand.Ink),
                    new RectangleF(box.Left + DonutSize + 31f, keyTop, box.Width - DonutSize - 31f, 11f),
                    new PdfStringFormat(PdfTextAlignment.Left, PdfVerticalAlignment.Middle));

                keyTop += 15f;
            }
        }, paddingTop: 10f);

        flow.Gap(12f);
    }

    /// <summary>A row of swatches and names, for the charts whose bars are not self labelling.</summary>
    /// <param name="flow">Where it goes.</param>
    /// <param name="entries">Name and colour.</param>
    private static void Key(Flow flow, IReadOnlyList<(string Name, string Colour)> entries)
    {
        if (entries.Count == 0) return;

        flow.Fixed(12f, (graphics, box) =>
        {
            var x = box.Left + LabelWidth;

            foreach (var (name, colour) in entries)
            {
                graphics.DrawRectangle(PdfTheme.Brush(colour), new RectangleF(x, box.Top + 3f, 7f, 7f));

                var width = (name.Length * 4.6f) + 8f;

                graphics.DrawString(name, flow.Theme.Font(new TextStyle { Size = 7.5f }), PdfTheme.Brush(CapgeminiBrand.Muted),
                    new RectangleF(x + 10f, box.Top, width, 12f),
                    new PdfStringFormat(PdfTextAlignment.Left, PdfVerticalAlignment.Middle));

                x += width + 16f;
            }
        }, paddingTop: 8f);
    }

    /// <summary>
    /// The roadmap, as the grid the contract describes rather than as a list.
    /// </summary>
    /// <remarks>
    /// One column per band and one row per theme, with a cell carrying the count and the
    /// hours that sit in it. This is the page a client keeps after the rest is filed, and a
    /// table of thirty rows is not something anybody keeps.
    ///
    /// Cells are shaded by how much work is in them rather than coloured per row, because
    /// the question the grid answers is where the weight is.
    /// </remarks>
    /// <param name="flow">Where it goes.</param>
    /// <param name="rows">Row labels, in order.</param>
    /// <param name="columns">Column labels, in order.</param>
    /// <param name="cell">The count and hours at a row and column.</param>
    private static void GridChart(
        Flow flow,
        List<string> rows,
        string[] columns,
        Func<string, string, (int Count, decimal Hours)> cell)
    {
        if (rows.Count == 0 || columns.Length == 0) return;

        var heaviest = rows
            .SelectMany(row => columns.Select(column => cell(row, column).Hours))
            .DefaultIfEmpty(0m)
            .Max();

        const float rowHeight = 34f;
        var height = (rows.Count + 1) * rowHeight;

        flow.Fixed(height, (graphics, box) =>
        {
            var labelWidth = 92f;
            var cellWidth = (box.Width - labelWidth) / columns.Length;
            var font = flow.Theme.Font(new TextStyle { Size = 7.5f });

            for (var column = 0; column < columns.Length; column++)
            {
                graphics.DrawString(columns[column], flow.Theme.Font(new TextStyle { Size = 7.5f, Bold = true }),
                    PdfTheme.Brush(CapgeminiBrand.Muted),
                    new RectangleF(box.Left + labelWidth + (column * cellWidth) + 4f, box.Top, cellWidth - 8f, rowHeight),
                    new PdfStringFormat(PdfTextAlignment.Center, PdfVerticalAlignment.Middle));
            }

            for (var row = 0; row < rows.Count; row++)
            {
                var top = box.Top + ((row + 1) * rowHeight);

                graphics.DrawString(rows[row], flow.Theme.Font(new TextStyle { Size = 8f }), PdfTheme.Brush(CapgeminiBrand.Ink),
                    new RectangleF(box.Left, top, labelWidth - 6f, rowHeight),
                    new PdfStringFormat(PdfTextAlignment.Left, PdfVerticalAlignment.Middle));

                for (var column = 0; column < columns.Length; column++)
                {
                    var (count, hours) = cell(rows[row], columns[column]);
                    var left = box.Left + labelWidth + (column * cellWidth);
                    var area = new RectangleF(left + 2f, top + 2f, cellWidth - 4f, rowHeight - 4f);

                    // Empty cells are drawn too. A gap in a grid is information, and leaving
                    // it blank makes the grid look like it failed to render.
                    if (count == 0)
                    {
                        graphics.DrawRectangle(PdfTheme.Pen(CapgeminiBrand.Line, 0.5f), area);
                        continue;
                    }

                    graphics.DrawRectangle(
                        PdfTheme.Brush(heaviest > 0 && hours >= heaviest * 0.5m
                            ? CapgeminiBrand.Blue
                            : CapgeminiBrand.BlueSoft),
                        area);

                    var ink = heaviest > 0 && hours >= heaviest * 0.5m
                        ? CapgeminiBrand.White
                        : CapgeminiBrand.Ink;

                    graphics.DrawString(
                        string.Create(CultureInfo.InvariantCulture, $"{count}\n{hours:0} h"),
                        font, PdfTheme.Brush(ink), area,
                        new PdfStringFormat(PdfTextAlignment.Center, PdfVerticalAlignment.Middle));
                }
            }
        }, paddingTop: 10f);

        flow.Gap(12f);
    }

    private const float RadarSize = 250f;

    /// <summary>
    /// The maturity scores, as the shape they make.
    /// </summary>
    /// <remarks>
    /// The one chart in this document whose numbers no part of the product produced. Every
    /// point is a judgement somebody made in an interview, which is why the axis labels carry
    /// the scores as text as well: a shape is memorable and a number is checkable, and this
    /// is the section most likely to be quoted back.
    ///
    /// Axes with no score are drawn as spokes and skipped by the shape rather than plotted at
    /// the origin. Nought means the capability is absent, which is a finding; not having
    /// looked is not, and a polygon that dives to the centre on an unscored axis says the
    /// first when it means the second.
    ///
    /// The scale prints beside it. A 1.9 with no scale next to it is meaningless and gets
    /// quoted anyway.
    /// </remarks>
    /// <param name="flow">Where it goes.</param>
    /// <param name="scores">Axis label and score, in the order they go round.</param>
    /// <param name="scale">The scale, written beside the chart.</param>
    private static void RadarChart(
        Flow flow,
        IReadOnlyList<(string Label, decimal? Score)> scores,
        string scale)
    {
        if (scores.Count < 3) return;

        const float max = 5f;
        const int rings = 5;

        // Taller than the circle and centred across the full width. The labels sit outside
        // the outer ring on every side, and a circle drawn hard against the left margin puts
        // the ones on that side off the page: the first render lost biAndAnalytics and
        // customer360 entirely.
        const float labelRoom = 14f;

        flow.Fixed(RadarSize + (labelRoom * 2), (graphics, box) =>
        {
            var radius = (RadarSize / 2) - labelRoom;
            var centreX = box.Left + (box.Width / 2);
            var centreY = box.Top + labelRoom + (RadarSize / 2);

            PointF At(int index, float value)
            {
                // Starting at the top and going clockwise, which is how everybody reads one.
                var angle = (-Math.PI / 2) + (2 * Math.PI * index / scores.Count);
                var distance = radius * (value / max);

                return new PointF(
                    centreX + (float)(Math.Cos(angle) * distance),
                    centreY + (float)(Math.Sin(angle) * distance));
            }

            // The web: one ring per point on the scale, so somebody can read a score off the
            // shape rather than only off the labels.
            for (var ring = 1; ring <= rings; ring++)
            {
                var corners = Enumerable.Range(0, scores.Count)
                    .Select(index => At(index, ring))
                    .ToArray();

                graphics.DrawPolygon(PdfTheme.Pen(CapgeminiBrand.Line, ring == rings ? 0.8f : 0.4f), corners);
            }

            for (var index = 0; index < scores.Count; index++)
            {
                var spoke = At(index, max);
                graphics.DrawLine(PdfTheme.Pen(CapgeminiBrand.Line, 0.4f), centreX, centreY, spoke.X, spoke.Y);
            }

            // The shape. Only the scored axes, and only when enough of them are scored for a
            // polygon to mean anything.
            var scored = scores
                .Select((entry, index) => (entry.Score, index))
                .Where(entry => entry.Score is not null)
                .Select(entry => At(entry.index, (float)entry.Score!.Value))
                .ToArray();

            if (scored.Length >= 3)
            {
                graphics.DrawPolygon(
                    PdfTheme.Pen(CapgeminiBrand.Blue, 1.6f),
                    PdfTheme.Brush(CapgeminiBrand.BlueSoft),
                    scored);

                foreach (var point in scored)
                {
                    graphics.DrawEllipse(PdfTheme.Brush(CapgeminiBrand.Blue),
                        new RectangleF(point.X - 2.2f, point.Y - 2.2f, 4.4f, 4.4f));
                }
            }

            var font = flow.Theme.Font(new TextStyle { Size = 7f });

            for (var index = 0; index < scores.Count; index++)
            {
                var (label, score) = scores[index];
                var anchor = At(index, max + 0.62f);

                // Labels sit outside the outer ring and are aligned by which side of the
                // circle they are on, or the ones on the left run back over the shape.
                var left = anchor.X < centreX - 4f;
                var centred = Math.Abs(anchor.X - centreX) <= 4f;

                // Wide enough for the longest axis name plus its score, and explicitly not
                // wrapping. serviceRequestManagement wrapped onto a second line that fell
                // outside the box, and the score went with it: the label read as unscored
                // while the point was plotted, which is the worst of both.
                const float labelWidth = 124f;

                var area = centred
                    ? new RectangleF(anchor.X - (labelWidth / 2), anchor.Y - 6f, labelWidth, 13f)
                    : left
                        ? new RectangleF(anchor.X - labelWidth, anchor.Y - 6f, labelWidth, 13f)
                        : new RectangleF(anchor.X, anchor.Y - 6f, labelWidth, 13f);

                var align = centred
                    ? PdfTextAlignment.Center
                    : left ? PdfTextAlignment.Right : PdfTextAlignment.Left;

                graphics.DrawString(
                    score is null
                        ? $"{label}  —"
                        : string.Create(CultureInfo.InvariantCulture, $"{label}  {score:0.#}"),
                    font,
                    PdfTheme.Brush(score is null ? CapgeminiBrand.Muted : CapgeminiBrand.Ink),
                    area,
                    new PdfStringFormat(align, PdfVerticalAlignment.Middle)
                    {
                        WordWrap = PdfWordWrapType.None
                    });
            }
        }, paddingTop: 10f);

        flow.Text(scale, new TextStyle { Size = 8.5f, Colour = CapgeminiBrand.Muted }, paddingTop: 6f);
        flow.Gap(12f);
    }
}
