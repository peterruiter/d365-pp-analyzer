namespace PowerPete.Analyzer.Tests;

using System.Text;
using FluentAssertions;
using PowerPete.Analyzer.Dataverse;
using Xunit;

/// <summary>
/// The scanner that pulls an exported solution out of a JSON response without holding it.
/// </summary>
/// <remarks>
/// Worth testing at this level of detail because of what it replaced. Reading the response
/// with a JSON parser was three lines and obviously correct; this is a state machine over a
/// byte stream, and the way a state machine over a byte stream fails is on the boundary
/// between two reads, in production, on the one solution large enough to need more than one.
///
/// So every case here is also run through a stream that hands over a few bytes at a time,
/// which is the shape of the defect this class could plausibly have.
/// </remarks>
public sealed class Base64PropertyTests
{
    /// <summary>A stream that refuses to hand over more than a few bytes at a time.</summary>
    /// <remarks>
    /// A network stream is under no obligation to fill the buffer it is given and regularly
    /// does not. A MemoryStream always does, so testing against one proves nothing about
    /// the case that matters: a value split across two reads.
    /// </remarks>
    /// <param name="content">What to serve.</param>
    /// <param name="chunk">How much to serve at once.</param>
    private sealed class DribblingStream(byte[] content, int chunk) : Stream
    {
        private int position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => content.Length;

        public override long Position { get => position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var take = Math.Min(Math.Min(chunk, count), content.Length - position);
            Array.Copy(content, position, buffer, offset, take);
            position += take;
            return take;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Runs one response through the scanner, whole and then a few bytes at a time.</summary>
    /// <param name="json">The response.</param>
    private static async Task<(bool Found, byte[] Written)> ReadAsync(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);

        using var whole = new MemoryStream();
        var foundWhole = await Base64Property.CopyAsync(
            new MemoryStream(bytes), "ExportSolutionFile", whole, CancellationToken.None);

        // Three bytes at a time: smaller than a base64 group, smaller than the property name,
        // so every boundary in the state machine is crossed mid-token at least once.
        using var dribbled = new MemoryStream();
        var foundDribbled = await Base64Property.CopyAsync(
            new DribblingStream(bytes, 3), "ExportSolutionFile", dribbled, CancellationToken.None);

        foundDribbled.Should().Be(foundWhole, "a slow stream carries the same response as a fast one");
        dribbled.ToArray().Should().Equal(whole.ToArray(), "a value split across reads is the same value");

        return (foundWhole, whole.ToArray());
    }

    [Fact]
    public async Task Decodes_the_file_a_solution_export_carries()
    {
        // A zip's first two bytes are PK, which is the thing the reader on the other end
        // looks for, so this is the byte sequence that matters most.
        var zip = Encoding.UTF8.GetBytes("PK this stands in for a solution");
        var encoded = Convert.ToBase64String(zip);

        var (found, written) = await ReadAsync($$"""{"ExportSolutionFile":"{{encoded}}"}""");

        found.Should().BeTrue();
        written.Should().Equal(zip);
    }

    [Fact]
    public async Task Finds_the_property_after_the_ones_that_come_first()
    {
        // Dataverse puts @odata.context first. A scanner that assumed the file was the first
        // property would work against a hand written fixture and against nothing else.
        var zip = new byte[] { 1, 2, 3, 4, 5 };
        var encoded = Convert.ToBase64String(zip);

        var (found, written) = await ReadAsync(
            $$"""{"@odata.context":"https://x.crm4.dynamics.com/api/data/v9.2/$metadata#Microsoft.Dynamics.CRM.ExportSolutionResponse","ExportSolutionFile":"{{encoded}}"}""");

        found.Should().BeTrue();
        written.Should().Equal(zip);
    }

    [Fact]
    public async Task Survives_a_value_longer_than_any_one_read()
    {
        // The case the buffer boundary exists for. Sixty kilobytes is larger than the three
        // byte dribble by four orders of magnitude and crosses the internal buffer too.
        var zip = new byte[60_000];
        for (var index = 0; index < zip.Length; index++) zip[index] = (byte)(index % 251);

        var (found, written) = await ReadAsync(
            $$"""{"ExportSolutionFile":"{{Convert.ToBase64String(zip)}}"}""");

        found.Should().BeTrue();
        written.Should().Equal(zip);
    }

    [Fact]
    public async Task Reads_the_value_through_the_whitespace_a_formatter_leaves()
    {
        var zip = new byte[] { 9, 8, 7 };

        var (found, written) = await ReadAsync(
            "{\n  \"ExportSolutionFile\" : \"" + Convert.ToBase64String(zip) + "\"\n}");

        found.Should().BeTrue();
        written.Should().Equal(zip);
    }

    [Fact]
    public async Task Keeps_a_forward_slash_that_arrived_escaped()
    {
        // A forward slash is a quarter of the base64 alphabet and JSON is allowed to write
        // it as \/. Feeding the backslash to the decoder would throw on a file that is
        // perfectly good, and it would only happen for some values, which is the worst way
        // for anything to fail.
        var zip = Convert.FromBase64String("//79/A==");

        var (found, written) = await ReadAsync("""{"ExportSolutionFile":"\/\/79\/A=="}""");

        found.Should().BeTrue();
        written.Should().Equal(zip);
    }

    [Fact]
    public async Task Says_nothing_was_there_when_the_property_is_missing()
    {
        // The environment answering without a file is a refusal, and the caller reports the
        // rules that needed it as not assessed. Silently writing nothing and claiming
        // success would report them as passing.
        var (found, written) = await ReadAsync("""{"@odata.context":"https://x/$metadata"}""");

        found.Should().BeFalse();
        written.Should().BeEmpty();
    }

    [Fact]
    public async Task Is_not_fooled_by_the_name_appearing_in_another_value()
    {
        var zip = new byte[] { 42 };

        var (found, written) = await ReadAsync(
            $$"""{"message":"ExportSolutionFile was not produced","ExportSolutionFile":"{{Convert.ToBase64String(zip)}}"}""");

        found.Should().BeTrue();
        written.Should().Equal(zip, "the name inside a message is not the property");
    }

    [Fact]
    public async Task Refuses_a_response_that_stopped_halfway()
    {
        // A dropped connection mid-file. The blob would otherwise be a truncated zip, which
        // reads as a corrupt solution rather than as a failed download, and a corrupt
        // solution is something somebody would go and investigate in the client's tenant.
        var encoded = Convert.ToBase64String(new byte[1_000]);
        var truncated = $$"""{"ExportSolutionFile":"{{encoded}}""";

        var reading = async () => await Base64Property.CopyAsync(
            new MemoryStream(Encoding.UTF8.GetBytes(truncated)),
            "ExportSolutionFile",
            new MemoryStream(),
            CancellationToken.None);

        await reading.Should().ThrowAsync<EndOfStreamException>();
    }
}
