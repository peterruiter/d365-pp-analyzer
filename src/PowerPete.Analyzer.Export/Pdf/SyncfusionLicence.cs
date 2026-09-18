using Syncfusion.Licensing;

namespace PowerPete.Analyzer.Export.Pdf;

/// <summary>
/// Registers the Syncfusion licence before anything renders a PDF.
///
/// This is deliberately loud when the key is missing. An unregistered Syncfusion renderer does
/// not fail, it stamps a trial banner across the output, so the failure would otherwise reach a
/// client as a watermark on a document they were asked to sign. Refusing to start is the
/// cheaper outcome.
///
/// The key is version locked to the packages in <c>Directory.Packages.props</c>. Upgrading the
/// major version means asking for a new key, not just bumping the package.
/// </summary>
public static class SyncfusionLicence
{
    /// <summary>The environment variable Syncfusion itself documents, used when no key is configured.</summary>
    public const string EnvironmentVariable = "SYNCFUSION_LICENSE";

    private static readonly Lock Gate = new();
    private static bool registered;

    /// <summary>
    /// Registers the licence once per process.
    /// </summary>
    /// <param name="key">
    /// The key, normally bound from configuration and supplied by Key Vault. When this is empty
    /// the environment variable is used instead, which is how tests and local runs supply it.
    /// </param>
    /// <param name="force">
    /// Replace a licence that is already registered. The system health page uses this to swap a
    /// key that turned out not to cover document processing, without waiting for a restart and
    /// without shipping watermarked documents in the meantime.
    /// </param>
    /// <exception cref="InvalidOperationException">No key was available from either source.</exception>
    public static void Register(string? key = null, bool force = false)
    {
        lock (Gate)
        {
            if (registered && !force)
            {
                return;
            }

            var licence = string.IsNullOrWhiteSpace(key)
                ? Environment.GetEnvironmentVariable(EnvironmentVariable)
                : key;

            if (string.IsNullOrWhiteSpace(licence))
            {
                throw new InvalidOperationException(
                    "No Syncfusion licence key is configured. Set Syncfusion:LicenseKey, which Key Vault "
                    + $"supplies in Azure, or the {EnvironmentVariable} environment variable for a local run. "
                    + "Without it every generated PDF carries a trial watermark.");
            }

            SyncfusionLicenseProvider.RegisterLicense(licence.Trim());
            registered = true;
        }
    }

    /// <summary>
    /// Whether a licence has been registered and it covers PDF generation.
    /// </summary>
    /// <remarks>
    /// Asked by the health page and by the reports list, which both need the answer without
    /// wanting the exception. Validating an unregistered provider is harmless, so this is
    /// simply the two questions in one and safe to call at any time.
    /// </remarks>
    public static bool IsRegisteredForPdf => registered && CoversPdf();

    /// <summary>Whether the registered licence actually covers PDF generation.</summary>
    /// <remarks>
    /// A key issued for the UI components alone validates for those platforms and not for
    /// document processing, which is the one mistake that would still produce a watermark after
    /// a successful registration.
    /// </remarks>
    public static bool CoversPdf() => SyncfusionLicenseProvider.ValidateLicense([Platform.PDF]);
}
