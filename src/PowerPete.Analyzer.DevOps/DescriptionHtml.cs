namespace PowerPete.Analyzer.DevOps;

using System.Text.RegularExpressions;

/// <summary>
/// Reads the small dialect of HTML the backlog builder writes.
/// </summary>
/// <remarks>
/// One reader, shared, rather than one per target. The builder emits HTML because that is
/// what the Azure DevOps description field takes, and every other target has to turn it
/// into something else: Atlassian Document Format for Jira, markdown for GitHub. Each of
/// those conversions needs the same first step, and the first version of the second one was
/// a copy of the first with the same four regexes in it.
///
/// That is the shape this codebase keeps being caught by. Two half parsers agree on the day
/// they are written and disagree the first time the builder emits a tag only one of them
/// knows, and the disagreement shows up as a section missing from a client's board rather
/// than as anything a compiler or a test would raise.
///
/// This is not a general HTML converter and is not trying to be one. It understands the six
/// sections this product writes, and anything else degrades to its text rather than being
/// dropped.
/// </remarks>
internal static partial class DescriptionHtml
{
    /// <summary>The block elements in some of our own HTML, as a tag and its text.</summary>
    /// <param name="html">The description, the criteria, or a plain sentence.</param>
    public static IEnumerable<(string Tag, string Text)> Blocks(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) yield break;

        var matches = BlockElement().Matches(html);

        // Not everything handed to this is markup. The acceptance criteria are HTML and the
        // test requirement is a plain sentence, and a regex over block elements finds
        // nothing in a plain sentence: the heading was written and the text under it
        // silently was not, which is a worse page than no heading at all.
        if (matches.Count == 0)
        {
            var plain = Text(html);

            if (plain.Length > 0) yield return ("p", plain);

            yield break;
        }

        foreach (Match match in matches)
        {
            yield return (match.Groups["tag"].Value.ToLowerInvariant(), Text(match.Groups["body"].Value));
        }
    }

    /// <summary>The text inside a block, with its inline markup removed and entities restored.</summary>
    /// <param name="inner">The block's inner HTML.</param>
    public static string Text(string inner)
    {
        var withBreaks = LineBreak().Replace(inner, " ");
        var withoutTags = InlineTag().Replace(withBreaks, string.Empty);

        return WhitespaceRun().Replace(System.Net.WebUtility.HtmlDecode(withoutTags), " ").Trim();
    }

    /// <summary>One line, with every run of whitespace collapsed to the given separator.</summary>
    /// <param name="value">The text.</param>
    /// <param name="separator">What to put where the whitespace was.</param>
    public static string Flatten(string value, string separator = " ") =>
        WhitespaceRun().Replace(value.Trim(), separator);

    [GeneratedRegex(@"<(?<tag>h[1-6]|p|li)\b[^>]*>(?<body>.*?)</\k<tag>>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline, 5000)]
    private static partial Regex BlockElement();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase, 5000)]
    private static partial Regex LineBreak();

    [GeneratedRegex("<[^>]+>", RegexOptions.None, 5000)]
    private static partial Regex InlineTag();

    [GeneratedRegex(@"\s+", RegexOptions.None, 5000)]
    private static partial Regex WhitespaceRun();
}
