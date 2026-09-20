namespace PowerPete.Analyzer.Data;

/// <summary>
/// One setting the deployment supplies, and every spelling of it that is accepted.
/// </summary>
/// <param name="Name">
/// The canonical name, in configuration form. An environment variable spells it with a double
/// underscore where this has a colon, which <see cref="DeploymentSettings.FromEnvironment"/>
/// does for the callers that have no configuration system.
/// </param>
/// <param name="Aliases">
/// Older or shorter spellings that still work. Kept because a deployed container holds the
/// name it was deployed with, and a rename that breaks a running deployment to tidy a string
/// is not worth it.
/// </param>
/// <param name="Purpose">What the setting is for, as an operator would need it explained.</param>
/// <param name="Deployed">
/// Whether the infrastructure is expected to write it. False for the ones a developer sets on
/// their own machine and a container never has.
/// </param>
public sealed record DeploymentSetting(
    string Name,
    IReadOnlyList<string> Aliases,
    string Purpose,
    bool Deployed = true);

/// <summary>
/// Every setting the API and the worker read from their host, named once.
/// </summary>
/// <remarks>
/// This exists because the alternative was tried and failed silently for the whole life of the
/// deployment. The infrastructure wrote <c>KeyVault__Uri</c>. The system health page read
/// <c>KeyVault:Uri</c> and reported a healthy, writable vault. The API read <c>KeyVaultUri</c>,
/// found nothing, and built the secret store that refuses. The worker read
/// <c>ANALYZER_KEYVAULT_URI</c> and found nothing either. Three readers, three spellings, one
/// writer, and the only symptom was a 503 at the end of a sign-in wizard saying no vault was
/// configured while the operations page said one was.
///
/// The comment on <see cref="SecretStore.For"/> predicted exactly this and put the decision in
/// one place. It was the right idea applied one layer too low: the name of the setting is as
/// much a part of the decision as the choice of store.
///
/// So the names live here, the readers ask this, and a test holds the list against what the
/// bicep actually writes. A setting the infrastructure sets and nothing reads, or a setting the
/// code needs and nothing sets, now fails a build rather than a deployment.
/// </remarks>
public static class DeploymentSettings
{
    /// <summary>Where engagements are stored.</summary>
    public static readonly DeploymentSetting SqlConnection = new(
        "ConnectionStrings:Analyzer",
        ["ANALYZER_SQL_CONNECTION"],
        "The analyzer database. Nothing starts without it.");

    /// <summary>Where client credentials are kept.</summary>
    public static readonly DeploymentSetting KeyVaultUri = new(
        "KeyVault:Uri",
        ["KeyVaultUri", "ANALYZER_KEYVAULT_URI"],
        "The vault that holds client credentials. Without it a connection needing one is refused "
        + "rather than stored in the database.");

    /// <summary>Where an uploaded solution file goes.</summary>
    public static readonly DeploymentSetting UploadContainer = new(
        "Uploads:ContainerUri",
        ["ANALYZER_UPLOAD_CONTAINER"],
        "The blob container an uploaded solution is written to and the worker reads back.");

    /// <summary>Where the keys that sign a sign-in are kept.</summary>
    /// <remarks>
    /// Without it ASP.NET writes them to a directory inside the container, where they die
    /// with the replica and are shared with none of the others. The API scales to three, so
    /// a sign-in begun on one replica and returned to another could not be unprotected, and
    /// every restart invalidated every sign-in in flight. It fails intermittently and the
    /// error reads like a tampered request rather than a missing key.
    /// </remarks>
    public static readonly DeploymentSetting DataProtectionBlob = new(
        "DataProtection:BlobUri",
        [],
        "The blob holding the keys that sign an interactive sign-in. Shared between replicas "
        + "and outliving them, which the default is neither.");

    /// <summary>What those keys are encrypted with before they are written.</summary>
    public static readonly DeploymentSetting DataProtectionKey = new(
        "DataProtection:KeyUri",
        [],
        "The vault key the data protection keys are wrapped with. The blob role is scoped to "
        + "the whole storage account, so unencrypted they would be readable by anything granted "
        + "access to the uploads container.");

    /// <summary>Who is admitted before anybody can admit anybody.</summary>
    public static readonly DeploymentSetting InitialGlobalAdmin = new(
        "Access:InitialGlobalAdminUpn",
        ["InitialGlobalAdmin", "ANALYZER_INITIAL_ADMIN"],
        "The first global administrator, seeded on start. Without it, if nobody has been admitted "
        + "yet then nobody can be.");

    /// <summary>Who a locked-out user is told to write to.</summary>
    public static readonly DeploymentSetting AdminContact = new(
        "Support:AdminContact",
        ["Access:GlobalAdminContactEmail", "AdminContactEmail"],
        "The address shown to somebody the product will not let in.");

    /// <summary>The name shown when there is no Entra application.</summary>
    public static readonly DeploymentSetting LocalSignInDisplayName = new(
        "LocalSignIn:DisplayName",
        ["ANALYZER_LOCAL_DISPLAY_NAME"],
        "The name the local sign-in presents. Only reached when no Entra application is configured.",
        Deployed: false);

    /// <summary>How often the worker looks for a command.</summary>
    public static readonly DeploymentSetting PollSeconds = new(
        "Worker:PollSeconds",
        ["ANALYZER_POLL_SECONDS"],
        "Seconds between polls. Ten if unset, which is right for every deployment so far.",
        Deployed: false);

    /// <summary>Which worker claimed a run.</summary>
    public static readonly DeploymentSetting WorkerId = new(
        "Worker:Id",
        ["ANALYZER_WORKER_ID"],
        "The name a worker claims runs under. The machine name if unset.",
        Deployed: false);

    /// <summary>The tenant the product's own application lives in.</summary>
    /// <remarks>
    /// The <c>AzureAd</c> settings are listed here as the inventory rather than because
    /// everything reads them through this class. Microsoft.Identity.Web binds the whole section
    /// itself, which is the right way round for the middleware. They are named here so the test
    /// that holds this list against the infrastructure covers them too: an Entra setting the
    /// bicep writes under a name nothing binds would turn the sign-in guard off silently, which
    /// is the most serious failure this product has.
    /// </remarks>
    public static readonly DeploymentSetting EntraTenantId = new(
        "AzureAd:TenantId",
        [],
        "The tenant the product's own application registration lives in. Without it sign-in is "
        + "not enforced at all.");

    /// <summary>The product's own application registration.</summary>
    public static readonly DeploymentSetting EntraClientId = new(
        "AzureAd:ClientId",
        [],
        "The product's own application registration.");

    /// <summary>The secret that registration signs with.</summary>
    public static readonly DeploymentSetting EntraClientSecret = new(
        "AzureAd:ClientSecret",
        [],
        "The client secret. Interactive sign-in needs it to redeem an authorization code.");

    /// <summary>Which cloud.</summary>
    public static readonly DeploymentSetting EntraInstance = new(
        "AzureAd:Instance",
        [],
        "The sign-in authority. The public cloud unless a client is somewhere else.");

    /// <summary>Where the authority sends the browser back to.</summary>
    public static readonly DeploymentSetting EntraCallbackPath = new(
        "AzureAd:CallbackPath",
        [],
        "Where Entra sends the browser back to. Bound by the middleware from the section.");

    /// <summary>When the client secret stops working.</summary>
    public static readonly DeploymentSetting EntraClientSecretExpires = new(
        "AzureAd:ClientSecretExpiresUtc",
        [],
        "When the client secret expires. Recorded rather than read from Entra, so the operations "
        + "page can count down to the day every sign-in fails at once.");

    /// <summary>The licence the PDF is rendered under.</summary>
    public static readonly DeploymentSetting SyncfusionLicenseKey = new(
        "Syncfusion:LicenseKey",
        ["SYNCFUSION_LICENSE"],
        "The Syncfusion licence. Without it every page of every report is watermarked rather "
        + "than refused, which is the worse of the two failures.");

    /// <summary>The model endpoint used to write the narrative.</summary>
    public static readonly DeploymentSetting OpenAiEndpoint = new(
        "OpenAi:Endpoint",
        ["ANALYZER_OPENAI_ENDPOINT"],
        "Azure OpenAI, for the written narrative. The report is composed without it and says so.",
        Deployed: false);

    /// <summary>The deployment name at that endpoint.</summary>
    public static readonly DeploymentSetting OpenAiDeployment = new(
        "OpenAi:Deployment",
        ["ANALYZER_OPENAI_DEPLOYMENT"],
        "The model deployment at that endpoint.",
        Deployed: false);

    /// <summary>All of them, for the test that holds this against the infrastructure.</summary>
    public static IReadOnlyList<DeploymentSetting> All { get; } =
    [
        SqlConnection,
        KeyVaultUri,
        UploadContainer,
        DataProtectionBlob,
        DataProtectionKey,
        InitialGlobalAdmin,
        AdminContact,
        EntraTenantId,
        EntraClientId,
        EntraClientSecret,
        EntraInstance,
        EntraCallbackPath,
        EntraClientSecretExpires,
        SyncfusionLicenseKey,
        LocalSignInDisplayName,
        PollSeconds,
        WorkerId,
        OpenAiEndpoint,
        OpenAiDeployment,
    ];

    /// <summary>
    /// Reads a setting through a host's own lookup, trying the canonical name and then each alias.
    /// </summary>
    /// <param name="setting">The setting.</param>
    /// <param name="lookup">The host's lookup, usually an indexer on its configuration.</param>
    /// <returns>The first non-empty value, or null.</returns>
    public static string? Read(DeploymentSetting setting, Func<string, string?> lookup)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentNullException.ThrowIfNull(lookup);

        foreach (var name in Names(setting))
        {
            var value = lookup(name);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        return null;
    }

    /// <summary>
    /// Reads a setting straight from the environment, for a host with no configuration system.
    /// </summary>
    /// <param name="setting">The setting.</param>
    /// <returns>The first non-empty value, or null.</returns>
    /// <remarks>
    /// The colon in a canonical name is a double underscore in an environment variable, which is
    /// the platform's convention and the exact detail the worker got wrong: the container was
    /// given <c>KeyVault__Uri</c> and the worker asked for <c>ANALYZER_KEYVAULT_URI</c>.
    /// </remarks>
    public static string? FromEnvironment(DeploymentSetting setting)
    {
        ArgumentNullException.ThrowIfNull(setting);

        return Read(setting, name => Environment.GetEnvironmentVariable(EnvironmentName(name)));
    }

    /// <summary>The environment variable form of a configuration name.</summary>
    /// <param name="name">A configuration name.</param>
    /// <returns>The same name with colons doubled to underscores.</returns>
    public static string EnvironmentName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return name.Replace(":", "__", StringComparison.Ordinal);
    }

    /// <summary>Every spelling of a setting, canonical first.</summary>
    /// <param name="setting">The setting.</param>
    /// <returns>The names to try, in order.</returns>
    public static IEnumerable<string> Names(DeploymentSetting setting)
    {
        ArgumentNullException.ThrowIfNull(setting);

        yield return setting.Name;

        foreach (var alias in setting.Aliases) yield return alias;
    }
}
