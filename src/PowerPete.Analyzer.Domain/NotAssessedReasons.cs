namespace PowerPete.Analyzer.Domain;

using System.Globalization;
using PowerPete.Analyzer.Domain.Localization;

/// <summary>
/// Why a check could not run, as a key rather than as a sentence.
/// </summary>
/// <remarks>
/// These sentences were composed in English by the rule engine and stored, so the report
/// printed them in English whatever language it was written in. A German assessment carried
/// sixteen lines of English in the one section a client reads most carefully, which is the
/// section that explains what the product did not look at.
///
/// A key and its argument rather than a sentence, decided at render time. The same stored run
/// then reads correctly in all six languages, and a translation corrected next month applies
/// to the runs that already exist rather than only to future ones.
///
/// The argument is already stored: <see cref="NotAssessed.MissingEvidence"/> is the evidence
/// source the sentence names, so nothing new had to be written to the database.
/// </remarks>
public static class NotAssessedReasons
{
    /// <summary>The connection cannot reach the evidence this rule needs, at all.</summary>
    public const string Unreachable = "notAssessed.unreachable";

    /// <summary>The rule is in the catalogue and nothing implements it yet.</summary>
    public const string NoHandler = "notAssessed.noHandler";

    /// <summary>
    /// The connection can reach the evidence and the read came back with nothing.
    /// </summary>
    /// <remarks>
    /// A different thing from <see cref="Unreachable"/> and the distinction is the point. The
    /// first is a fact about how the client chose to connect and nothing can be done about it
    /// on this run; this one is a fault somebody can go and fix.
    /// </remarks>
    public const string ReadReturnedNothing = "notAssessed.readReturnedNothing";

    /// <summary>
    /// The sentence for one entry, in the reader's language.
    /// </summary>
    /// <remarks>
    /// Anything that is not one of the keys above is returned as it stands. Runs analysed
    /// before this existed hold a finished English sentence in the same column, and printing
    /// a lookup key where a sentence used to be would be a worse regression than the English.
    /// </remarks>
    /// <param name="text">The report's localiser.</param>
    /// <param name="entry">The check that did not run.</param>
    /// <returns>What to print.</returns>
    public static string Describe(Localiser text, NotAssessed entry)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(entry);

        var evidence = entry.MissingEvidence ?? string.Empty;

        return entry.Reason switch
        {
            Unreachable => string.Format(
                CultureInfo.InvariantCulture,
                text[Unreachable,
                    "This connection could not reach {0}, which this rule needs. It has not been checked and is "
                    + "not reported as passing."],
                evidence),

            ReadReturnedNothing => string.Format(
                CultureInfo.InvariantCulture,
                text[ReadReturnedNothing,
                    "This connection reaches {0} and the read of it did not return anything on this run, so the "
                    + "rule has not been checked. That is a fault to look into rather than a limit of the "
                    + "connection."],
                evidence),

            NoHandler => text[NoHandler,
                "This rule is declared in the catalogue and has no detection implemented yet."],

            _ => entry.Reason,
        };
    }
}
