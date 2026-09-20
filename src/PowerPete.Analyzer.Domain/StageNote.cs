namespace PowerPete.Analyzer.Domain;

using System.Text.Json;

/// <summary>
/// What a stage is doing right now, in a form the screen can translate.
/// </summary>
/// <remarks>
/// A run reads a client's estate for minutes or hours, and until this existed the only thing
/// the run screen learned between one stage finishing and the next was nothing at all. A
/// person watching a stage that has said "running" for eleven minutes cannot tell a slow
/// export from a hung worker, and the usual response to that is to restart something.
///
/// A key and its arguments rather than a sentence, because the sentence has to arrive in the
/// reader's language and the worker does not know what that is. The worker writes
/// <c>exporting</c> with a solution name and two numbers; the screen turns that into
/// "Exporting CapTranslator (3 of 11)" in whichever of the six languages somebody is reading.
///
/// The arguments are the parts that are not translatable: a solution's unique name is a
/// publisher's choice and a count is a count. Anything that would need translating belongs in
/// the key, which is why the notes name a stage's work rather than the component type it is
/// reading.
/// </remarks>
/// <param name="Key">The localisation key, without its namespace prefix.</param>
/// <param name="Args">What fills its placeholders, in order.</param>
public sealed record StageNote(string Key, IReadOnlyList<string> Args)
{
    /// <summary>How it is written to the stage row.</summary>
    private static readonly JsonSerializerOptions Format = new(JsonSerializerDefaults.Web);

    /// <summary>One note with no arguments.</summary>
    /// <param name="key">The localisation key.</param>
    public StageNote(string key)
        : this(key, [])
    {
    }

    /// <summary>One note about the nth of m things.</summary>
    /// <param name="key">The localisation key.</param>
    /// <param name="subject">What is being worked on. A name, never a sentence.</param>
    /// <param name="done">How many are finished, counting this one.</param>
    /// <param name="total">How many there are.</param>
    public StageNote(string key, string subject, int done, int total)
        : this(key, [subject, done.ToString(System.Globalization.CultureInfo.InvariantCulture), total.ToString(System.Globalization.CultureInfo.InvariantCulture)])
    {
    }

    /// <summary>
    /// The note as the column holds it.
    /// </summary>
    /// <remarks>
    /// Truncated rather than allowed to fail. The column is four hundred characters, a
    /// solution's unique name has no declared limit, and a progress note is the last thing
    /// in this product that should be able to stop a run.
    /// </remarks>
    public string ToJson()
    {
        var json = JsonSerializer.Serialize(this, Format);

        return json.Length <= 400 ? json : JsonSerializer.Serialize(new StageNote(Key), Format);
    }
}
