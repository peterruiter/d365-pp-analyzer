namespace PowerPete.Analyzer.Api;

using System.Buffers.Binary;
using System.Globalization;
using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

/// <summary>
/// Where an exported solution file goes on its way to the worker.
/// </summary>
/// <remarks>
/// The offline mode is the one that gets past a security review in week one, and until now
/// it had nowhere to put the file: the wizard collected a file name in a text box and the
/// worker looked for a blob nobody had written.
///
/// The file goes to a private container the product's managed identity reaches through
/// Entra. The API writes it and the worker reads it back; neither holds an account key,
/// because the account has shared key access turned off and there is no key to hold.
/// </remarks>
/// <param name="containerUri">The uploads container, or null where none is configured.</param>
public sealed class SolutionUploads(Uri? containerUri)
{
    /// <summary>
    /// The largest file accepted.
    /// </summary>
    /// <remarks>
    /// A solution export is measured in megabytes. Two hundred is far above anything real and
    /// far below anything that would trouble the container, and the reader loads the whole
    /// zip into memory to seek in it, so an unbounded upload is an unbounded allocation.
    /// </remarks>
    public const long MaximumBytes = 200L * 1024 * 1024;

    /// <summary>Whether this deployment has anywhere to put a file.</summary>
    public bool IsConfigured => containerUri is not null;

    /// <summary>What an upload attempt produced.</summary>
    /// <param name="BlobName">What to store in the connection's settings.</param>
    /// <param name="Bytes">How big it was.</param>
    /// <param name="Error">Why it was refused, when it was.</param>
    public sealed record Result(string? BlobName, long Bytes, string? Error);

    /// <summary>
    /// Stores one exported solution.
    /// </summary>
    /// <remarks>
    /// Named for the engagement and the moment rather than for the file. Two consultants
    /// uploading "solution.zip" on the same engagement must not overwrite each other, and a
    /// client's own file name is not something to trust as a blob path.
    /// </remarks>
    /// <param name="engagementId">Which engagement it belongs to.</param>
    /// <param name="content">The uploaded stream.</param>
    /// <param name="originalName">What the browser called it, kept only as metadata.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<Result> StoreAsync(
        Guid engagementId,
        Stream content,
        string? originalName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (containerUri is null)
        {
            return new Result(null, 0, "This deployment has no upload container configured, so there is nowhere to put the file.");
        }

        // Read it once, into memory, because the check below needs the first four bytes and a
        // browser upload stream does not seek. Bounded by the size limit above.
        var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        if (buffer.Length == 0) return new Result(null, 0, "The file is empty.");

        if (buffer.Length > MaximumBytes)
        {
            return new Result(null, buffer.Length, string.Create(CultureInfo.InvariantCulture,
                $"The file is {buffer.Length / (1024 * 1024)} MB. The limit is {MaximumBytes / (1024 * 1024)} MB, which is far above any real solution export."));
        }

        buffer.Position = 0;

        if (!LooksLikeZip(buffer))
        {
            return new Result(null, buffer.Length,
                "That is not a zip file. An exported solution is the .zip the environment produced; unpacking it first does not help.");
        }

        buffer.Position = 0;

        var blobName = $"{engagementId:N}/{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip";
        var container = new BlobContainerClient(containerUri, new DefaultAzureCredential());
        var blob = container.GetBlobClient(blobName);

        try
        {
            await blob.UploadAsync(
                buffer,
                new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = "application/zip" },

                    // The name the browser sent, as metadata rather than as the path. It is
                    // useful when somebody is working out which of three uploads was which, and
                    // it is not something to build a path out of.
                    Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["engagementId"] = engagementId.ToString(),
                        ["originalName"] = Sanitise(originalName)
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }

        // Answered rather than thrown.
        //
        // Everything above this line returns a sentence a consultant can act on, and the
        // one call that actually touches Azure did not: a container the identity cannot
        // write to threw straight out of the endpoint, so the screen showed 500 with
        // nothing on it. The three that happen in practice are a missing role assignment,
        // a container that does not exist and a storage account firewall, and they need
        // three different people to fix, so they are told apart here.
        catch (RequestFailedException failure)
        {
            return new Result(null, buffer.Length, failure.ErrorCode switch
            {
                "AuthorizationPermissionMismatch" or "AuthenticationFailed" =>
                    "This deployment cannot write to its upload container. The identity it runs as needs Storage "
                    + "Blob Data Contributor on it, which is an Azure role assignment rather than anything in "
                    + "this product.",
                "ContainerNotFound" =>
                    $"The upload container at {containerUri} does not exist. It is created by the infrastructure "
                    + "deployment, so this usually means the setting points somewhere the deployment did not make.",
                _ => $"The file could not be stored: {failure.ErrorCode ?? failure.Status.ToString(CultureInfo.InvariantCulture)}."
            });
        }
        catch (Exception failure) when (failure is IOException or TaskCanceledException)
        {
            return new Result(null, buffer.Length, $"The file could not be stored: {failure.Message}");
        }

        return new Result(blobName, buffer.Length, null);
    }

    /// <summary>
    /// Whether the bytes begin the way a zip does.
    /// </summary>
    /// <remarks>
    /// Checked rather than trusting the extension or the content type, both of which the
    /// browser takes from the file name. The failure this avoids is not malicious, it is
    /// somebody uploading the unpacked folder's manifest and getting a stack trace out of the
    /// zip reader an hour later on the worker.
    /// </remarks>
    /// <param name="content">The uploaded bytes.</param>
    private static bool LooksLikeZip(Stream content)
    {
        Span<byte> header = stackalloc byte[4];

        if (content.Read(header) != 4) return false;

        // PK\x03\x04 for an ordinary archive, PK\x05\x06 for an empty one.
        var signature = BinaryPrimitives.ReadUInt32LittleEndian(header);

        return signature is 0x04034B50 or 0x06054B50;
    }

    /// <summary>Blob metadata takes ASCII, so anything else is dropped rather than failing the upload.</summary>
    /// <param name="value">The browser's file name.</param>
    private static string Sanitise(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unnamed";

        var cleaned = new string([.. value
            .Where(character => character is >= ' ' and <= '~')
            .Take(200)]);

        return cleaned.Length == 0 ? "unnamed" : cleaned;
    }
}
