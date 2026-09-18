using System.Reflection;

namespace PowerPete.Analyzer.Export;

/// <summary>
/// The Capgemini brand surface both deliverables share: the palette, the wordmark and the
/// Ubuntu typeface.
///
/// The palette is the brand book v1.0 (March 2026) set, the same one the Dynamics 365 Contact
/// Center estimator uses, so a client who receives both recognises them as one family. The
/// assets are embedded in the assembly rather than read from disk because the report is
/// generated inside a container that has no font cache and no asset share.
/// </summary>
public static class CapgeminiBrand
{
    // ---------------------------------------------------------------- palette --

    /// <summary>Capgemini blue. The primary. Used for emphasis and the first data series.</summary>
    public const string Blue = "#0058AB";

    /// <summary>Deep blue, almost black. Cover bands, headings and the darkest chart series.</summary>
    public const string DarkBlue = "#121A38";

    /// <summary>Light blue. The second data series and the accent rule under a heading.</summary>
    public const string LightBlue = "#1DB8F2";

    /// <summary>Turquoise. Third data series.</summary>
    public const string Turquoise = "#00D5D0";

    /// <summary>Yellow. Fourth data series.</summary>
    public const string Yellow = "#FEB100";

    /// <summary>Teal. Used where a positive outcome needs a colour.</summary>
    public const string Teal = "#00828E";

    /// <summary>Terracotta. Used for a warning or a milestone marker, never for a series.</summary>
    public const string Terracotta = "#BE4D00";

    /// <summary>Deep red. Reserved for a figure that needs challenging.</summary>
    public const string DeepRed = "#8F3237";

    /// <summary>Body text.</summary>
    public const string Ink = "#121A38";

    /// <summary>Secondary text, table headers and captions.</summary>
    public const string Muted = "#5F6B85";

    /// <summary>Rules and table borders.</summary>
    public const string Line = "#D9DFEA";

    /// <summary>Page background behind a card.</summary>
    public const string Background = "#F1F4F9";

    /// <summary>A tinted panel on a white page.</summary>
    public const string BlueSoft = "#E6EEF7";

    /// <summary>White.</summary>
    public const string White = "#FFFFFF";

    /// <summary>The tagline that closes the report, as it appears on Capgemini material.</summary>
    public const string Tagline = "Make it real.";

    /// <summary>Series colours in order, for a stacked bar or a legend.</summary>
    public static readonly IReadOnlyList<string> Series = [Blue, LightBlue, Turquoise, Yellow, Teal, Terracotta];

    /// <summary>The colour a feasibility band should carry, so the same band reads the same everywhere.</summary>
    public static string BandColour(string? bandId) => bandId switch
    {
        "automateNow" => Teal,
        "automateWithWork" => Blue,
        "assistOnly" => Yellow,
        "leaveAlone" => Muted,
        _ => Muted
    };

    // ----------------------------------------------------------------- assets --

    private static readonly Assembly Owner = typeof(CapgeminiBrand).Assembly;

    /// <summary>The wordmark reversed out for a navy band. 1200 by 392.</summary>
    public static byte[] WordmarkWhite { get; } = Load("capgemini-wordmark-white.png");

    /// <summary>The wordmark in blue for a white page footer. 1200 by 392.</summary>
    public static byte[] WordmarkBlue { get; } = Load("capgemini-wordmark-blue.png");

    /// <summary>Ubuntu Regular, the Capgemini text face.</summary>
    public static byte[] UbuntuRegular { get; } = Load("Ubuntu-Regular.ttf");

    /// <summary>Ubuntu Bold.</summary>
    public static byte[] UbuntuBold { get; } = Load("Ubuntu-Bold.ttf");

    /// <summary>The wordmark's aspect ratio, so it is never stretched.</summary>
    public const float WordmarkAspect = 392f / 1200f;

    /// <summary>The family name to ask a renderer for once the faces are registered.</summary>
    public const string FontFamily = "Ubuntu";

    private static byte[] Load(string fileName)
    {
        var resource = $"PowerPete.Analyzer.Export.Brand.{fileName}";
        using var stream = Owner.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException(
                $"Brand asset '{resource}' is not embedded. Check the EmbeddedResource glob in PowerPete.Analyzer.Export.csproj.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
