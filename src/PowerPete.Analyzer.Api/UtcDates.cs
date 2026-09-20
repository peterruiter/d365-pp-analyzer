namespace PowerPete.Analyzer.Api;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Writes every date as the UTC instant it is.
/// </summary>
/// <remarks>
/// Every date in this product's database is UTC, and the column names say so. Dapper hands
/// them back as <see cref="DateTimeKind.Unspecified"/>, because SQL Server's datetime2 carries
/// no zone, and System.Text.Json then writes an ISO string with no offset on the end.
///
/// A browser parses an ISO string with no offset as local time. So a stage that started at
/// 08:57 UTC was read by a browser in Amsterdam as 08:57 local, two hours in the past, and
/// every running stage showed an elapsed time of about 120 minutes until it finished. Once it
/// finished the arithmetic was between two values shifted the same way and came out right,
/// which is why it looked like a display glitch that fixed itself rather than like a timezone.
///
/// Fixed here rather than in the browser, and for the whole API rather than per endpoint,
/// because "assume UTC" written into one screen is a rule the next screen does not know.
/// </remarks>
internal sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    /// <inheritdoc />
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTime().ToUniversalTime();

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        // Unspecified means it came from the database, where everything is UTC. Saying so is
        // not a conversion: it attaches the fact the column name already asserts.
        var utc = value.Kind switch
        {
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => value,
        };

        writer.WriteStringValue(utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture));
    }
}

/// <summary>The same for a date that may be absent.</summary>
/// <remarks>
/// A separate converter because System.Text.Json does not apply a converter for
/// <c>DateTime</c> to <c>DateTime?</c>, and the nullable ones are the interesting half here:
/// a stage that has not finished has no completed time, and that is exactly the stage whose
/// elapsed time was wrong.
/// </remarks>
internal sealed class NullableUtcDateTimeConverter : JsonConverter<DateTime?>
{
    private static readonly UtcDateTimeConverter Inner = new();

    /// <inheritdoc />
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? null : reader.GetDateTime().ToUniversalTime();

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        Inner.Write(writer, value.Value, options);
    }
}
