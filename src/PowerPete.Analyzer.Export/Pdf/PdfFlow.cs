using Syncfusion.Drawing;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;

namespace PowerPete.Analyzer.Export.Pdf;

/// <summary>Where a cell sits when the tallest cell in its row decides the height.</summary>
internal enum CellAlign
{
    /// <summary>Against the top of the row. The default.</summary>
    Top,

    /// <summary>Centred against the tallest cell.</summary>
    Middle,

    /// <summary>Against the bottom of the row.</summary>
    Bottom
}

/// <summary>
/// The document being written, and the page currently being written on.
///
/// Header and footer are page templates rather than something painted per page, so they appear
/// on pages this code creates and on pages the renderer creates for itself when a long block
/// overflows. Keeping that in one place is worth the indirection.
/// </summary>
internal sealed class PdfSurface : IDisposable
{
    private readonly PdfDocument document = new();

    /// <param name="theme">Fonts, colours and measurement.</param>
    /// <param name="headerHeight">Reserved at the top of every page.</param>
    /// <param name="footerHeight">Reserved at the bottom of every page.</param>
    /// <param name="contentPadding">Extra space between the header and the first line of content.</param>
    public PdfSurface(PdfTheme theme, float headerHeight, float footerHeight, float contentPadding = 0f)
    {
        Theme = theme;
        document.PageSettings.Size = PdfPageSize.A4;
        document.PageSettings.Margins.All = 0;

        HeaderHeight = headerHeight;
        FooterHeight = footerHeight;

        // Coordinates handed to a page's graphics are relative to the client area, which starts
        // below the top template and ends above the bottom one, not to the paper. Measured, not
        // assumed: with a 70pt header on A4, graphics Y of zero lands at page Y of 70 and the
        // client height comes back as 726 rather than 842. Treating these as page coordinates
        // pushes the last block of every page down into the footer band.
        ContentTop = contentPadding;
        ContentBottom = document.PageSettings.Size.Height - headerHeight - footerHeight;
    }

    /// <summary>Fonts, colours and measurement.</summary>
    public PdfTheme Theme { get; }

    /// <summary>Width of the paper.</summary>
    public float PageWidth => document.PageSettings.Size.Width;

    /// <summary>Reserved at the top of every page.</summary>
    public float HeaderHeight { get; }

    /// <summary>Reserved at the bottom of every page.</summary>
    public float FooterHeight { get; }

    /// <summary>The first Y a line of content may occupy, measured from the top of the client area.</summary>
    public float ContentTop { get; }

    /// <summary>The Y past which content must move to a new page, measured from the top of the client area.</summary>
    public float ContentBottom { get; }

    /// <summary>How tall a full page of content can be, used to spot a block that can never fit.</summary>
    public float ContentHeight => ContentBottom - ContentTop;

    /// <summary>
    /// Paints the running header and footer, once, for every page.
    ///
    /// The painters receive a graphics surface the size of the band rather than the page, so
    /// they lay out from zero and ignore where on the page the band sits.
    /// </summary>
    public void Frame(Action<PdfGraphics, SizeF>? header, Action<PdfGraphics, SizeF>? footer)
    {
        if (header is not null && HeaderHeight > 0f)
        {
            var band = new PdfPageTemplateElement(new RectangleF(0, 0, PageWidth, HeaderHeight));
            header(band.Graphics, new SizeF(PageWidth, HeaderHeight));
            document.Template.Top = band;
        }

        if (footer is not null && FooterHeight > 0f)
        {
            var band = new PdfPageTemplateElement(new RectangleF(0, 0, PageWidth, FooterHeight));
            footer(band.Graphics, new SizeF(PageWidth, FooterHeight));
            document.Template.Bottom = band;
        }
    }

    /// <summary>Starts a new page and returns it.</summary>
    public PdfPage AddPage() => document.Pages.Add();

    /// <summary>Opens a column of content across the page, inside the given horizontal insets.</summary>
    /// <param name="insetLeft">Left inset of the text column.</param>
    /// <param name="insetRight">Right inset of the text column.</param>
    /// <param name="top">
    /// Where the first block sits. Defaults to the normal content top; the report's cover passes
    /// zero so its navy band can bleed to the top edge of the paper.
    /// </param>
    public Flow Start(float insetLeft, float insetRight, float top = float.NaN)
    {
        var page = AddPage();
        var start = float.IsNaN(top) ? ContentTop : top;
        return new Flow(this, insetLeft, PageWidth - insetLeft - insetRight, start, page, measuring: false);
    }

    /// <summary>The finished document.</summary>
    public byte[] Save()
    {
        using var buffer = new MemoryStream();
        document.Save(buffer);
        return buffer.ToArray();
    }

    public void Dispose() => document.Dispose();
}

/// <summary>
/// A column of content that lays itself out downwards and starts a new page when it runs out.
///
/// This is the piece that stands in for a declarative layout tree. Every method advances a
/// cursor, and anything that needs to know how tall its children are, a tinted panel or a block
/// that must not be split, runs those children twice: once against a flow in measuring mode
/// that draws nothing, then again for real. That costs a second pass and buys the ability to
/// draw a background behind content whose height nobody knows in advance.
///
/// Content passed to those methods must be free of side effects, because it will run twice.
/// </summary>
internal sealed class Flow
{
    private readonly PdfSurface surface;

    internal Flow(PdfSurface surface, float left, float width, float top, PdfPage page, bool measuring)
    {
        this.surface = surface;
        Left = left;
        Width = width;
        Y = top;
        Page = page;
        Measuring = measuring;
    }

    /// <summary>Left edge of this column, in points from the left of the paper.</summary>
    public float Left { get; }

    /// <summary>Width available to content.</summary>
    public float Width { get; }

    /// <summary>The cursor. Content is drawn here and this moves down.</summary>
    public float Y { get; private set; }

    /// <summary>The page the cursor is on.</summary>
    public PdfPage Page { get; private set; }

    /// <summary>True when this pass exists only to work out how tall something is.</summary>
    public bool Measuring { get; }

    /// <summary>Fonts, colours and measurement, for painters that draw their own text.</summary>
    public PdfTheme Theme => surface.Theme;

    /// <summary>Moves the cursor down without drawing.</summary>
    public void Gap(float height) => Y += height;

    /// <summary>
    /// Starts a new page regardless of how much room is left.
    ///
    /// The management report is a fixed sequence of pages, each making one point, so its
    /// boundaries are deliberate rather than a consequence of running out of room.
    /// </summary>
    public void Break(float top = float.NaN)
    {
        if (Measuring)
        {
            return;
        }

        Page = surface.AddPage();
        Y = float.IsNaN(top) ? surface.ContentTop : top;
    }

    /// <summary>
    /// Starts a new page unless at least <paramref name="height"/> remains, drawing nothing.
    ///
    /// This is how a heading is kept with the opening of its paragraph. Wrapping the pair in
    /// <see cref="Together"/> would work for a short section and fail badly for a long one, by
    /// pushing a whole page of prose down to keep it with three words of heading.
    /// </summary>
    public void Reserve(float height)
    {
        if (!Measuring)
        {
            EnsureRoom(height);
        }
    }

    /// <summary>Draws a paragraph, wrapping to the column and paginating if it outgrows the page.</summary>
    public void Text(string? value, TextStyle style, float paddingTop = 0f)
    {
        Y += paddingTop;

        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        var height = surface.Theme.Measure(value, style, Width);

        if (Measuring)
        {
            Y += height;
            return;
        }

        var font = surface.Theme.Font(style);
        var brush = PdfTheme.Brush(style.Colour);
        var format = PdfTheme.Format(style);

        if (height <= surface.ContentHeight)
        {
            EnsureRoom(height);
            Page.Graphics.DrawString(value, font, brush, new RectangleF(Left, Y, Width, height), format);
            Y += height;
            return;
        }

        // Longer than a whole page. Hand it to the renderer, which adds pages itself, and pick
        // the cursor back up wherever it finished.
        var element = new PdfTextElement(value, font, pen: null!, brush, format);
        var result = element.Draw(
            Page,
            new RectangleF(Left, Y, Width, surface.ContentBottom - Y),
            new PdfLayoutFormat { Layout = PdfLayoutType.Paginate, Break = PdfLayoutBreakType.FitPage });

        Page = result.Page;
        Y = result.Bounds.Bottom;
    }

    /// <summary>Draws a horizontal rule across the column.</summary>
    public void Rule(string colour, float paddingTop = 0f, float thickness = 0.75f, float width = 0f)
    {
        Y += paddingTop;

        if (!Measuring)
        {
            var length = width > 0f ? width : Width;
            Page.Graphics.DrawLine(PdfTheme.Pen(colour, thickness), Left, Y, Left + length, Y);
        }

        Y += thickness;
    }

    /// <summary>Reserves a band of the column and hands it to a painter, for charts and diagrams.</summary>
    public void Fixed(float height, Action<PdfGraphics, RectangleF> paint, float paddingTop = 0f)
    {
        Y += paddingTop;

        if (!Measuring)
        {
            EnsureRoom(height);
            paint(Page.Graphics, new RectangleF(Left, Y, Width, height));
        }

        Y += height;
    }

    /// <summary>Draws an embedded image, scaled to the given box.</summary>
    public void Image(byte[] bytes, float width, float height, PdfTextAlignment align = PdfTextAlignment.Left, float paddingTop = 0f)
    {
        Y += paddingTop;

        if (!Measuring)
        {
            EnsureRoom(height);
            using var stream = new MemoryStream(bytes, writable: false);
            var bitmap = new PdfBitmap(stream);
            var x = align switch
            {
                PdfTextAlignment.Right => Left + Width - width,
                PdfTextAlignment.Center => Left + ((Width - width) / 2f),
                _ => Left
            };

            Page.Graphics.DrawImage(bitmap, x, Y, width, height);
        }

        Y += height;
    }

    /// <summary>
    /// Draws content as one block that will not be split across a page break.
    ///
    /// A signature block or a numbered finding needs this: a heading stranded at the foot of one
    /// page with its body on the next is the specific ugliness worth a measuring pass to avoid.
    /// </summary>
    public void Together(Action<Flow> content, float paddingTop = 0f)
    {
        Y += paddingTop;
        var height = MeasureBlock(content, Width);

        if (Measuring)
        {
            Y += height;
            return;
        }

        EnsureRoom(height);
        var inner = new Flow(surface, Left, Width, Y, Page, measuring: false);
        content(inner);
        Page = inner.Page;
        Y = Math.Max(inner.Y, Y);
    }

    /// <summary>Draws content inside a tinted or bordered box that sizes itself to what it holds.</summary>
    /// <param name="content">What goes in the box.</param>
    /// <param name="background">Fill colour, or null for none.</param>
    /// <param name="border">Stroke colour, or null for none.</param>
    /// <param name="padding">Space between the box edge and its content.</param>
    /// <param name="paddingTop">Space above the box.</param>
    /// <param name="accent">
    /// Colour of a heavy bar down the left edge. The report uses it to carry a meaning the box
    /// itself does not, such as which data series a figure belongs to or that a note is a caveat.
    /// </param>
    /// <param name="accentWidth">Thickness of that bar.</param>
    public void Panel(
        Action<Flow> content,
        string? background = null,
        string? border = null,
        float padding = 0f,
        float paddingTop = 0f,
        string? accent = null,
        float accentWidth = 3f)
    {
        Y += paddingTop;

        var innerWidth = Width - (padding * 2f);
        var height = MeasureBlock(content, innerWidth) + (padding * 2f);

        if (Measuring)
        {
            Y += height;
            return;
        }

        EnsureRoom(height);
        var box = new RectangleF(Left, Y, Width, height);

        if (background is not null)
        {
            Page.Graphics.DrawRectangle(PdfTheme.Brush(background), box);
        }

        if (border is not null)
        {
            Page.Graphics.DrawRectangle(PdfTheme.Pen(border, 0.75f), box);
        }

        if (accent is not null)
        {
            Page.Graphics.DrawRectangle(PdfTheme.Brush(accent), new RectangleF(box.Left, box.Top, accentWidth, box.Height));
        }

        var inner = new Flow(surface, Left + padding, innerWidth, Y + padding, Page, measuring: false);
        content(inner);
        Y = box.Bottom;
    }

    /// <summary>Lays cells out side by side. The tallest cell decides the height of the row.</summary>
    public void Row(Action<RowBuilder> build, float paddingTop = 0f, float gap = 0f)
    {
        Y += paddingTop;

        var builder = new RowBuilder();
        build(builder);

        if (builder.Cells.Count == 0)
        {
            return;
        }

        var widths = builder.Widths(Width, gap);
        var heights = new float[builder.Cells.Count];
        var height = 0f;

        for (var i = 0; i < builder.Cells.Count; i++)
        {
            heights[i] = MeasureBlock(builder.Cells[i].Content, widths[i]);
            height = Math.Max(height, heights[i]);
        }

        if (Measuring)
        {
            Y += height;
            return;
        }

        EnsureRoom(height);

        var x = Left;
        for (var i = 0; i < builder.Cells.Count; i++)
        {
            var cell = builder.Cells[i];
            var offset = cell.Align switch
            {
                CellAlign.Middle => (height - heights[i]) / 2f,
                CellAlign.Bottom => height - heights[i],
                _ => 0f
            };

            cell.Content(new Flow(surface, x, widths[i], Y + offset, Page, measuring: false));
            x += widths[i] + gap;
        }

        Y += height;
    }

    /// <summary>Runs content against a flow that draws nothing, to learn how tall it is.</summary>
    private float MeasureBlock(Action<Flow> content, float width)
    {
        var probe = new Flow(surface, Left, width, 0f, Page, measuring: true);
        content(probe);
        return probe.Y;
    }

    /// <summary>Moves to a new page if the next block will not fit on this one.</summary>
    private void EnsureRoom(float height)
    {
        if (Measuring || Y + height <= surface.ContentBottom)
        {
            return;
        }

        Page = surface.AddPage();
        Y = surface.ContentTop;
    }
}

/// <summary>Collects the cells of a row before any of them are measured or drawn.</summary>
internal sealed class RowBuilder
{
    internal sealed record Cell(float Weight, float Constant, CellAlign Align, Action<Flow> Content);

    internal List<Cell> Cells { get; } = [];

    /// <summary>A cell that takes a share of whatever width the fixed cells leave.</summary>
    public void Relative(float weight, Action<Flow> content, CellAlign align = CellAlign.Top) =>
        Cells.Add(new Cell(weight, 0f, align, content));

    /// <summary>A cell of a fixed width in points.</summary>
    public void Constant(float width, Action<Flow> content, CellAlign align = CellAlign.Top) =>
        Cells.Add(new Cell(0f, width, align, content));

    /// <summary>Resolves every cell to a width, fixed cells first.</summary>
    internal float[] Widths(float total, float gap)
    {
        var available = total - (gap * (Cells.Count - 1));
        var fixedWidth = Cells.Sum(cell => cell.Constant);
        var weights = Cells.Sum(cell => cell.Weight);
        var flexible = Math.Max(0f, available - fixedWidth);

        return [.. Cells.Select(cell => cell.Weight > 0f && weights > 0f
            ? flexible * (cell.Weight / weights)
            : cell.Constant)];
    }
}
