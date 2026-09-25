using System.Globalization;
using Syncfusion.Drawing;
using Syncfusion.Pdf.Graphics;

namespace PowerPete.Analyzer.Export.Pdf;

/// <summary>
/// How a run of text should look.
///
/// A record with init properties rather than a parameter list, so a call site derives one style
/// from another the way the previous renderer chained its calls: <c>Body with { Bold = true }</c>
/// reads as an intention, where a nine argument constructor reads as a puzzle.
/// </summary>
internal sealed record TextStyle
{
    /// <summary>Point size.</summary>
    public float Size { get; init; } = 10f;

    /// <summary>Whether to use the bold face. Ubuntu ships as two files, so this picks a file.</summary>
    public bool Bold { get; init; }

    /// <summary>A hex colour from <see cref="Brand"/>.</summary>
    public string Colour { get; init; } = Brand.Ink;

    /// <summary>Line spacing as a multiple of the point size, the way CSS expresses it.</summary>
    public float LineHeight { get; init; } = 1.45f;

    /// <summary>Extra space between characters, in points. Used on the small capitalised labels.</summary>
    public float LetterSpacing { get; init; }

    /// <summary>Horizontal alignment within the space the caller gives it.</summary>
    public PdfTextAlignment Align { get; init; } = PdfTextAlignment.Left;
}

/// <summary>
/// Fonts, colours and text measurement for the PDF deliverables.
///
/// Measurement is the reason this type exists. The renderer draws at coordinates rather than
/// flowing a declarative tree, so anything that has to sit behind or around text, a tinted
/// panel, a rule, a bordered box, has to know how tall that text will be before it is drawn.
/// <see cref="Measure"/> answers that without putting anything on the page.
///
/// Fonts are cached per size and weight. Constructing a <see cref="PdfTrueTypeFont"/> parses and
/// subsets the typeface, and a report asks for the same handful of sizes hundreds of times.
/// </summary>
internal sealed class PdfTheme : IDisposable
{
    private readonly Dictionary<(bool Bold, float Size), PdfTrueTypeFont> fonts = [];
    private readonly List<MemoryStream> faces = [];
    private readonly PdfStringLayouter layouter = new();

    /// <summary>The face for a style, embedded and subset so the container needs no font cache.</summary>
    public PdfFont Font(TextStyle style)
    {
        var key = (style.Bold, style.Size);
        if (fonts.TryGetValue(key, out var cached))
        {
            return cached;
        }

        // A fresh stream per face. Syncfusion reads the stream lazily while subsetting, so two
        // fonts cannot share one and rewind it under each other.
        var stream = new MemoryStream(style.Bold ? Brand.UbuntuBold : Brand.UbuntuRegular, writable: false);
        faces.Add(stream);

        var font = new PdfTrueTypeFont(stream, style.Size, PdfFontStyle.Regular);
        fonts[key] = font;
        return font;
    }

    /// <summary>The string format carrying alignment, tracking and line spacing for a style.</summary>
    public static PdfStringFormat Format(TextStyle style) => new()
    {
        Alignment = style.Align,
        LineAlignment = PdfVerticalAlignment.Top,
        CharacterSpacing = style.LetterSpacing,

        // Syncfusion takes the gap between lines in points, where the style expresses line height
        // as a multiple of the size the way CSS does. Convert rather than reinterpret.
        LineSpacing = style.Size * (style.LineHeight - 1f),
        WordWrap = PdfWordWrapType.Word
    };

    /// <summary>How tall <paramref name="text"/> will be when wrapped into <paramref name="width"/>.</summary>
    public float Measure(string text, TextStyle style, float width)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0f;
        }

        // A tall but finite box. Passing float.MaxValue overflows the layouter's arithmetic.
        var result = layouter.Layout(text, Font(style), Format(style), new SizeF(width, 10_000f));
        return result.ActualSize.Height;
    }

    /// <summary>A brand hex string as a PDF colour.</summary>
    public static PdfColor Colour(string hex)
    {
        var value = hex.AsSpan().TrimStart('#');
        return new PdfColor(
            byte.Parse(value[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(value[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(value[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    /// <summary>A brand hex string as a fill.</summary>
    public static PdfBrush Brush(string hex) => new PdfSolidBrush(Colour(hex));

    /// <summary>A brand hex string as a stroke of the given width.</summary>
    public static PdfPen Pen(string hex, float width) => new(Colour(hex), width);

    public void Dispose()
    {
        foreach (var face in faces)
        {
            face.Dispose();
        }

        faces.Clear();
        fonts.Clear();
    }
}
