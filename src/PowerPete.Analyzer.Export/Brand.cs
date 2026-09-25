using System.Reflection;
using System.Text.Json;

namespace PowerPete.Analyzer.Export;

/// <summary>
/// Whose product this is, as far as anything that draws is concerned.
/// </summary>
/// <remarks>
/// This was <c>CapgeminiBrand</c>, a static class of <c>const</c> colours, which is exactly
/// the right shape for a product that will only ever have one brand and exactly the wrong
/// one the day it has two. The values now come from <c>brands/&lt;id&gt;.json</c>, chosen by
/// the <c>Brand</c> setting, and every call site reads the same way it always did.
///
/// The brand files and the wordmarks are embedded in the assembly rather than read from
/// disk, for the reason the wordmarks always were: the report is rendered inside a container
/// with no font cache and no asset share, so anything a deliverable needs has to travel with
/// the code.
///
/// One brand per process. It is chosen at startup from configuration and does not change
/// while the process runs, which is what makes a plain static surface honest here: a request
/// scoped brand would be a different product with a different argument about who is allowed
/// to pick one.
/// </remarks>
public static class Brand
{
    /// <summary>The brand shipped when nothing says otherwise.</summary>
    /// <remarks>
    /// Named rather than "the first one found". A deployment whose Brand setting is missing
    /// or misspelt gets a product that looks like something, and which something it is
    /// should be a decision in the source rather than whatever the file system enumerated.
    /// </remarks>
    public const string Default = "powerpete";

    private static readonly Assembly Owner = typeof(Brand).Assembly;

    private static Definition current = Load(Default);

    /// <summary>
    /// Picks the brand for this process.
    /// </summary>
    /// <remarks>
    /// Called once at startup. An unknown id throws rather than falling back, because a
    /// container configured with a brand that does not exist is a deployment mistake, and a
    /// product that quietly ships the wrong livery to a client is worse than one that
    /// refuses to start and says why.
    /// </remarks>
    /// <param name="id">The brand id, for example powerpete or capgemini.</param>
    public static void Use(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;

        current = Load(id.Trim().ToLowerInvariant());
    }

    /// <summary>Every brand this build carries, for a screen or a test that wants to list them.</summary>
    public static IReadOnlyList<string> Available { get; } =
        [.. Owner.GetManifestResourceNames()
            .Where(name => name.StartsWith("PowerPete.Analyzer.Export.Brands.", StringComparison.Ordinal)
                && name.EndsWith(".json", StringComparison.Ordinal))
            .Select(name => name["PowerPete.Analyzer.Export.Brands.".Length..^".json".Length])
            .OrderBy(name => name, StringComparer.Ordinal)];

    // ------------------------------------------------------------------ who --

    /// <summary>The brand's id, as the configuration names it.</summary>
    public static string Id => current.Id;

    /// <summary>What to call the brand in a sentence.</summary>
    public static string Name => current.Name;

    /// <summary>What to call the product.</summary>
    public static string Product => current.Product;

    /// <summary>Where the brand lives.</summary>
    public static string Site => current.Site;

    /// <summary>The line that closes a report.</summary>
    public static string Tagline => current.Tagline;

    /// <summary>The sentences that name a vendor rather than state a fact.</summary>
    public static IReadOnlyDictionary<string, string> Copy => current.Copy;

    /// <summary>The screen palette, as a ramp slot to colour map.</summary>
    public static IReadOnlyDictionary<string, string> Ramp => current.Ramp;

    // -------------------------------------------------------------- palette --

    /// <summary>The primary. Used for emphasis and the first data series.</summary>
    public static string Blue => current.Pdf["blue"];

    /// <summary>Almost black. Cover bands, headings and the darkest chart series.</summary>
    public static string DarkBlue => current.Pdf["darkBlue"];

    /// <summary>The second data series and the accent rule under a heading.</summary>
    public static string LightBlue => current.Pdf["lightBlue"];

    /// <summary>Third data series.</summary>
    public static string Turquoise => current.Pdf["turquoise"];

    /// <summary>Fourth data series.</summary>
    public static string Yellow => current.Pdf["yellow"];

    /// <summary>Used where a positive outcome needs a colour.</summary>
    public static string Teal => current.Pdf["teal"];

    /// <summary>Used for a warning or a milestone marker, never for a series.</summary>
    public static string Terracotta => current.Pdf["terracotta"];

    /// <summary>Reserved for a figure that needs challenging.</summary>
    public static string DeepRed => current.Pdf["deepRed"];

    /// <summary>Body text.</summary>
    public static string Ink => current.Pdf["ink"];

    /// <summary>Secondary text, table headers and captions.</summary>
    public static string Muted => current.Pdf["muted"];

    /// <summary>Rules and table borders.</summary>
    public static string Line => current.Pdf["line"];

    /// <summary>Page background behind a card.</summary>
    public static string Background => current.Pdf["background"];

    /// <summary>A tinted panel on a white page.</summary>
    public static string BlueSoft => current.Pdf["blueSoft"];

    /// <summary>White.</summary>
    public const string White = "#FFFFFF";

    /// <summary>Series colours in order, for a stacked bar or a legend.</summary>
    public static IReadOnlyList<string> Series =>
        [Blue, LightBlue, Turquoise, Yellow, Teal, Terracotta];

    /// <summary>The colour a feasibility band should carry, so the same band reads the same everywhere.</summary>
    /// <param name="bandId">The band.</param>
    public static string BandColour(string? bandId) => bandId switch
    {
        "automateNow" => Teal,
        "automateWithWork" => Blue,
        "assistOnly" => Yellow,
        "leaveAlone" => Muted,
        _ => Muted
    };

    // ----------------------------------------------------------------- assets --

    /// <summary>The wordmark reversed out for a dark band.</summary>
    public static byte[] WordmarkWhite => Asset(current.Assets["wordmarkWhite"]);

    /// <summary>The wordmark in the brand's own colour, for a white page footer.</summary>
    public static byte[] WordmarkBlue => Asset(current.Assets["wordmarkDark"]);

    /// <summary>Ubuntu Regular. Both brands use it, which is why it is not in a brand file.</summary>
    public static byte[] UbuntuRegular { get; } = Asset("Ubuntu-Regular.ttf");

    /// <summary>Ubuntu Bold.</summary>
    public static byte[] UbuntuBold { get; } = Asset("Ubuntu-Bold.ttf");

    /// <summary>The wordmark's aspect ratio, so it is never stretched.</summary>
    /// <remarks>
    /// Per brand, and the reason this is not a constant any more. The Capgemini wordmark is
    /// nearly square at 0.33 and the Power Pete one is a long text mark at 0.14; drawing one
    /// at the other's ratio does not fail, it produces a squashed logo on a client's cover.
    /// </remarks>
    public static float WordmarkAspect => current.Assets.TryGetValue("wordmarkAspect", out var value)
        && float.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var aspect)
            ? aspect
            : 0.32667f;

    /// <summary>The family name to ask a renderer for once the faces are registered.</summary>
    public const string FontFamily = "Ubuntu";

    /// <summary>The file name this brand uses for one of its web assets.</summary>
    /// <remarks>
    /// A name, not a path. The caller decides which folder to look in, so a brand file can
    /// never point at something outside it.
    /// </remarks>
    /// <param name="slot">siteWordmark, siteMark or favicon.</param>
    public static string? AssetName(string slot) =>
        current.Assets.TryGetValue(slot, out var file) && file.Length > 0 ? file : null;

    // ------------------------------------------------------------------ guts --

    /// <summary>One brand file, as it is written.</summary>
    private sealed record Definition(
        string Id,
        string Name,
        string Product,
        string Site,
        string Tagline,
        IReadOnlyDictionary<string, string> Ramp,
        IReadOnlyDictionary<string, string> Pdf,
        IReadOnlyDictionary<string, string> Assets,
        IReadOnlyDictionary<string, string> Copy);

    private static Definition Load(string id)
    {
        using var stream = Owner.GetManifestResourceStream($"PowerPete.Analyzer.Export.Brands.{id}.json")
            ?? throw new InvalidOperationException(
                $"There is no brand called '{id}'. This build carries: {string.Join(", ", Available)}.");

        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;

        return new Definition(
            Text(root, "id"),
            Text(root, "name"),
            Text(root, "product"),
            Text(root, "site"),
            Text(root, "tagline"),
            Map(root, "ramp"),
            Map(root, "pdf"),
            Map(root, "assets"),
            Map(root, "copy"));
    }

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.GetString() is { Length: > 0 } text
            ? text
            : throw new InvalidOperationException($"The brand file has no '{name}'.");

    /// <summary>
    /// One block of a brand file, flattened to strings.
    /// </summary>
    /// <remarks>
    /// Numbers come through as their raw text rather than being parsed here, so a block can
    /// hold an aspect ratio beside a colour without two shapes of dictionary.
    /// </remarks>
    private static Dictionary<string, string> Map(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var block) || block.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"The brand file has no '{name}' block.");
        }

        return block.EnumerateObject().ToDictionary(
            entry => entry.Name,
            entry => entry.Value.ValueKind == JsonValueKind.String
                ? entry.Value.GetString() ?? string.Empty
                : entry.Value.GetRawText(),
            StringComparer.Ordinal);
    }

    private static byte[] Asset(string fileName)
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
