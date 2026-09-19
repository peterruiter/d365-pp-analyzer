namespace PowerPete.Analyzer.Domain.Localization;

using System.Collections.Concurrent;
using System.Text.Json;

/// <summary>
/// The words a document is written in.
/// </summary>
/// <remarks>
/// Every call carries its English text as an argument rather than looking it up. That is
/// deliberate and it is the difference between a report with a gap in it and a report in the
/// wrong language: a key nobody has translated yet renders in English, which is a document
/// somebody can still hand to a client, and a key nobody has written at all is a compile error
/// rather than a blank line on page four.
///
/// A Dutch consultant handing a Dutch report to a Dutch client cannot have the findings in
/// English, which is why the finding and report namespaces exist. The same consultant would
/// rather have one paragraph in English than a hole where a paragraph should be, which is why
/// the fallback is silent.
///
/// Bundles are read once and kept. They are a few hundred short strings and a report renders
/// dozens of them.
/// </remarks>
public sealed class Localiser
{
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> Cache = new(StringComparer.Ordinal);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IReadOnlyDictionary<string, string> bundle;

    /// <summary>The language this writes in.</summary>
    public string Language { get; }

    /// <summary>
    /// Opens one namespace in one language.
    /// </summary>
    /// <param name="ns">ui, report, finding, backlog or inventory.</param>
    /// <param name="language">A locale code. Anything unknown falls back to the default.</param>
    public Localiser(string ns, string? language)
    {
        Language = LocaleCatalogue.Resolve(language).Code;
        bundle = Load(ns, Language);
    }

    /// <summary>
    /// The translated text, or the English that was passed in.
    /// </summary>
    /// <param name="key">The key, which is the same in every language.</param>
    /// <param name="english">What to write when nothing has translated it yet.</param>
    public string this[string key, string english] =>
        bundle.TryGetValue(key, out var translated) && !string.IsNullOrWhiteSpace(translated)
            ? translated
            : english;

    /// <summary>Whether a key has been translated, for a caller that wants to know.</summary>
    /// <param name="key">The key.</param>
    public bool Has(string key) => bundle.ContainsKey(key);

    /// <summary>
    /// Where the bundles live.
    /// </summary>
    /// <remarks>
    /// Beside the application rather than embedded, because a correction to a translation
    /// should not need a rebuild of the product, and because the same files are served to the
    /// web application from the same place.
    /// </remarks>
    public static string Directory { get; set; } = Find();

    /// <summary>
    /// Where the bundles are, wherever this is running from.
    /// </summary>
    /// <remarks>
    /// In the container they sit beside the application. From a command line run they are
    /// several folders above bin/Debug, and a lookup that only knew the first place silently
    /// wrote every document in English on a developer's machine while working in production,
    /// which is the worst way round for a defect about languages.
    /// </remarks>
    private static string Find()
    {
        const string tail = "src/PowerPete.Analyzer.Domain/Localization/Resources";
        var relative = tail.Replace('/', Path.DirectorySeparatorChar);

        foreach (var start in new[] { AppContext.BaseDirectory, System.IO.Directory.GetCurrentDirectory() })
        {
            var directory = new DirectoryInfo(start);

            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, relative);

                if (System.IO.Directory.Exists(candidate)) return candidate;

                directory = directory.Parent;
            }
        }

        // Nothing found. Every lookup then falls back to the English it was given, which is a
        // product in one language rather than a product that will not start.
        return Path.Combine(AppContext.BaseDirectory, relative);
    }

    private static IReadOnlyDictionary<string, string> Load(string ns, string language) =>
        Cache.GetOrAdd($"{ns}.{language}", _ =>
        {
            var path = Path.Combine(Directory, $"{ns}.{language}.json");

            if (!File.Exists(path))
            {
                // Not an error. English is the only locale guaranteed complete, and a
                // namespace nobody has translated yet renders in English everywhere.
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path), Json)
                    ?? new Dictionary<string, string>(StringComparer.Ordinal);
            }
            catch (JsonException)
            {
                // A malformed bundle degrades to English rather than taking the report with
                // it. The document is the deliverable; the translation is how it reads.
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }
        });
}
