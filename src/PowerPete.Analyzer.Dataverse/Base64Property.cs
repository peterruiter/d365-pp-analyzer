namespace PowerPete.Analyzer.Dataverse;

using System.Buffers;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Copies one base64 property out of a JSON response, without holding it.
/// </summary>
/// <remarks>
/// Dataverse returns an exported solution as <c>{"ExportSolutionFile":"UEsDBBQ..."}</c>: a
/// whole zip, base64 encoded, inside a JSON string. Every ordinary way of reading that puts
/// the file in memory two or three times over. <see cref="System.Text.Json.JsonDocument"/>
/// buffers the response, then materialises the base64 as a string, then
/// <c>Convert.FromBase64String</c> allocates the decoded bytes beside it: about 2.3 times the
/// file, on the large object heap, for every solution in the run.
///
/// So this reads the response as it arrives. It finds the property, and from the opening
/// quote to the closing one it pipes the bytes straight through a base64 transform into
/// whatever the caller is writing to, which in this product is a blob. Peak memory is the
/// read buffer, whatever the solution weighs.
///
/// It is a scanner rather than a parser, which is safe for exactly this shape and would not
/// be for a general one. The base64 alphabet has no quote in it, so the closing quote is
/// unambiguous, and a property name cannot contain a quote either. Anything richer than one
/// named string wants a real parser.
/// </remarks>
public static class Base64Property
{
    /// <summary>How much of the response is held at once.</summary>
    private const int BufferSize = 64 * 1024;

    /// <summary>
    /// Finds a base64 string property in a JSON stream and writes its decoded bytes.
    /// </summary>
    /// <param name="json">The response, read forwards once.</param>
    /// <param name="property">The property name, without quotes.</param>
    /// <param name="destination">Where the decoded bytes go. Left open.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Whether the property was there. False means the response did not carry one.</returns>
    /// <exception cref="FormatException">The value was not base64.</exception>
    public static async Task<bool> CopyAsync(
        Stream json,
        string property,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(property);

        // The quotes are part of what is searched for. Without them a property named in a
        // message, or a URL in the OData context that happened to contain the word, would
        // both match.
        var marker = Encoding.UTF8.GetBytes($"\"{property}\"");

        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

        // Write mode: bytes of base64 go in, decoded bytes come out the other side into the
        // destination. FromBase64Transform ignores whitespace, which is what makes it safe
        // against a service that ever decides to wrap the value.
        var transform = new FromBase64Transform();
        var decoder = new CryptoStream(destination, transform, CryptoStreamMode.Write, leaveOpen: true);

        var state = State.SearchingName;
        var matched = 0;
        var found = false;

        try
        {
            int read;

            while ((read = await json.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken).ConfigureAwait(false)) > 0)
            {
                var index = 0;

                while (index < read)
                {
                    switch (state)
                    {
                        case State.SearchingName:
                        {
                            var b = buffer[index++];

                            if (b == marker[matched])
                            {
                                matched++;
                                if (matched == marker.Length) state = State.ExpectingColon;
                            }
                            else
                            {
                                // Restarted on the byte that failed rather than after it,
                                // because that byte may itself begin the marker. Well formed
                                // JSON never puts a quote directly before this property, so
                                // no test here can reach it; it is the difference between a
                                // matcher that is correct and one that is correct as long as
                                // the input stays the shape somebody assumed.
                                matched = b == marker[0] ? 1 : 0;
                            }

                            break;
                        }

                        case State.ExpectingColon:
                        {
                            var b = buffer[index++];

                            if (b == (byte)':') state = State.ExpectingQuote;
                            else if (!IsWhitespace(b)) { state = State.SearchingName; matched = 0; }

                            break;
                        }

                        case State.ExpectingQuote:
                        {
                            var b = buffer[index++];

                            if (b == (byte)'"') { state = State.CopyingValue; found = true; }
                            else if (b == (byte)'n') return false; // null, which is a refusal rather than a file.
                            else if (!IsWhitespace(b)) { state = State.SearchingName; matched = 0; }

                            break;
                        }

                        case State.CopyingValue:
                        {
                            // The longest run of value bytes in this buffer that is neither
                            // the closing quote nor an escape, written in one call. Byte at a
                            // time would work and would be an order of magnitude slower on a
                            // file measured in megabytes.
                            var start = index;

                            while (index < read && buffer[index] != (byte)'"' && buffer[index] != (byte)'\\') index++;

                            if (index > start)
                            {
                                await decoder.WriteAsync(buffer.AsMemory(start, index - start), cancellationToken)
                                    .ConfigureAwait(false);
                            }

                            if (index == read) break;

                            if (buffer[index] == (byte)'"')
                            {
                                // The end of the value. Everything after it in the response
                                // is of no interest, and the connection is dropped rather
                                // than drained.
                                await decoder.FlushFinalBlockAsync(cancellationToken).ConfigureAwait(false);
                                return true;
                            }

                            // A backslash. Base64 has none, so this is JSON escaping a
                            // character that did not need it: some serialisers write \/ for
                            // a forward slash, and a forward slash is a quarter of the
                            // alphabet. Dropping the backslash leaves the character it was
                            // escaping, which is the byte that belongs in the file.
                            index++;
                            break;
                        }

                        default:
                            throw new InvalidOperationException($"Unreachable state {state}.");
                    }
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);

            try
            {
                // Disposing a CryptoStream flushes its final block, and on a response that
                // stopped halfway through the value that final block is a partial base64
                // group, which throws. The interesting failure is the truncation, reported
                // below; letting the decoder's complaint out of a finally would replace it
                // with a message about padding. In the success path the final block has
                // already been flushed and this does nothing.
                await decoder.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception disposal) when (disposal is FormatException or CryptographicException)
            {
            }

            transform.Dispose();
        }

        // The stream ended inside the value. A truncated response is not a file, and the
        // caller is told nothing was written rather than handed a zip that stops halfway.
        if (found)
        {
            throw new EndOfStreamException(
                $"The response ended in the middle of '{property}'. The file it carried is incomplete.");
        }

        return false;
    }

    /// <summary>JSON's four whitespace characters, between tokens.</summary>
    /// <param name="value">The byte.</param>
    private static bool IsWhitespace(byte value) =>
        value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';

    /// <summary>Where the scanner is.</summary>
    private enum State
    {
        /// <summary>Looking for the quoted property name.</summary>
        SearchingName,

        /// <summary>Found it; expecting the colon.</summary>
        ExpectingColon,

        /// <summary>Found that; expecting the quote that opens the value.</summary>
        ExpectingQuote,

        /// <summary>Inside the value, writing it out.</summary>
        CopyingValue,
    }
}
