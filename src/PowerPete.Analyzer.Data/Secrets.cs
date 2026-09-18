namespace PowerPete.Analyzer.Data;

using System.Text.RegularExpressions;
using Azure;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

/// <summary>
/// Where a client's credentials live.
/// </summary>
/// <remarks>
/// Not the database. A connection row is read by every screen, written to every export and
/// printed in every diagnostic, and a client's application secret has no business being
/// in any of them. The row carries a reference; this carries the value.
/// </remarks>
public interface ISecretStore
{
    /// <summary>
    /// Whether a vault has been configured.
    /// </summary>
    /// <remarks>
    /// Read by the API so it can refuse to create a connection that needs a secret, rather
    /// than accepting one and discovering at the first discovery run that the credential
    /// was never stored anywhere.
    /// </remarks>
    bool IsConfigured { get; }

    /// <summary>Stores a secret and returns the reference the connection row carries.</summary>
    /// <param name="name">Secret name, unique within the vault.</param>
    /// <param name="value">The credential itself.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<string> SetAsync(string name, string value, CancellationToken cancellationToken);

    /// <summary>Reads a secret back, for a connector about to use it.</summary>
    /// <param name="reference">What the connection row carries.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<string?> GetAsync(string reference, CancellationToken cancellationToken);

    /// <summary>Removes a secret, for a connection or engagement being deleted.</summary>
    /// <param name="reference">What the connection row carries.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task DeleteAsync(string reference, CancellationToken cancellationToken);
}

/// <summary>
/// Builds the names secrets are stored under.
/// </summary>
/// <remarks>
/// A connection row carries one reference, but several connectors take more than one
/// secret: Anywhere365 has a client secret and a SQL connection string, ServiceNow has a
/// client secret and a password. So the reference is a prefix and each secret hangs off it
/// by setting name, which keeps one column and still stores each credential separately.
///
/// Key Vault allows letters, digits and hyphens and nothing else. Deriving the name rather
/// than letting a caller choose one means a connection called "Client's tenant (prod)"
/// cannot produce a name the vault rejects at the last step of a wizard.
/// </remarks>
public static partial class SecretNames
{
    [GeneratedRegex("[^A-Za-z0-9-]", RegexOptions.CultureInvariant)]
    private static partial Regex Disallowed { get; }

    /// <summary>
    /// A fresh prefix for one connection's credentials.
    /// </summary>
    /// <remarks>
    /// Its own identifier rather than the connection's, so re entering a rotated credential
    /// can write a new prefix and leave the old secrets to be deleted afterwards, instead of
    /// overwriting the only copy of something that might still be in use by a running job.
    /// </remarks>
    public static string NewPrefix() => $"cc-{Guid.NewGuid():N}";

    /// <summary>Names one secret within a connection's prefix.</summary>
    /// <param name="prefix">What the connection row carries.</param>
    /// <param name="settingName">Which setting.</param>
    public static string For(string prefix, string settingName) =>
        Disallowed.Replace($"{prefix}-{settingName}", "-");
}

/// <summary>Key Vault.</summary>
/// <remarks>
/// Authenticates as the deployed application's managed identity, or as the signed in
/// developer locally. There is no connection string and no key, so nothing to leak from
/// configuration.
/// </remarks>
public sealed class KeyVaultSecretStore : ISecretStore
{
    private readonly SecretClient _client;

    /// <summary>Connects to a vault.</summary>
    /// <param name="vaultUri">The vault's URI.</param>
    public KeyVaultSecretStore(Uri vaultUri) =>
        _client = new SecretClient(vaultUri, new DefaultAzureCredential());

    /// <inheritdoc />
    public bool IsConfigured => true;

    /// <inheritdoc />
    public async Task<string> SetAsync(string name, string value, CancellationToken cancellationToken)
    {
        await _client.SetSecretAsync(name, value, cancellationToken);
        return name;
    }

    /// <inheritdoc />
    public async Task<string?> GetAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var secret = await _client.GetSecretAsync(reference, cancellationToken: cancellationToken);
            return secret.Value.Value;
        }
        catch (RequestFailedException failure) when (failure.Status == 404)
        {
            // A reference with nothing behind it. The caller reports a connection that needs
            // its credentials re entering, which is true and fixable, rather than crashing.
            return null;
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var operation = await _client.StartDeleteSecretAsync(reference, cancellationToken);
            await operation.WaitForCompletionAsync(cancellationToken);
        }
        catch (RequestFailedException failure) when (failure.Status == 404)
        {
            // Already gone. Deleting an engagement twice should not fail the second time.
        }
    }
}

/// <summary>
/// What runs when no vault is configured.
/// </summary>
/// <remarks>
/// Refuses rather than falling back to the database or to a file. A development fallback
/// that quietly stores a client's credential in plaintext is the kind of convenience that
/// reaches production, and refusing costs a developer one environment variable.
/// </remarks>
public sealed class UnconfiguredSecretStore : ISecretStore
{
    /// <inheritdoc />
    public bool IsConfigured => false;

    /// <inheritdoc />
    public Task<string> SetAsync(string name, string value, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(
            "No Key Vault is configured, so there is nowhere to put this credential. Set KeyVault:Uri. " +
            "Secrets are not written to the database, because a connection row is read by every screen " +
            "and included in every export.");

    /// <inheritdoc />
    public Task<string?> GetAsync(string reference, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(null);

    /// <inheritdoc />
    public Task DeleteAsync(string reference, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Picks the store a deployment gets.
/// </summary>
/// <remarks>
/// The decision is here rather than at each call site because there are several and they must
/// agree. A host that resolved a vault one way and the system health page another would report
/// a configured vault to an operator and refuse the next credential they tried to save.
/// </remarks>
public static class SecretStore
{
    /// <summary>
    /// The store for a configured vault, or the one that refuses.
    /// </summary>
    /// <param name="vaultUri">
    /// The vault, from configuration. Absent or empty in a local run, which is not an error:
    /// everything except storing a credential works without one.
    /// </param>
    /// <exception cref="InvalidOperationException">The URI was set and is not a URI.</exception>
    public static ISecretStore For(string? vaultUri)
    {
        if (string.IsNullOrWhiteSpace(vaultUri))
        {
            return new UnconfiguredSecretStore();
        }

        // A malformed vault URI fails here, at startup, naming the setting. Left to the first
        // secret it would fail inside a wizard, on the step after the one that was wrong.
        if (!Uri.TryCreate(vaultUri, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                $"KeyVaultUri is '{vaultUri}', which is not an absolute URI. It should look like " +
                "https://something.vault.azure.net/.");
        }

        return new KeyVaultSecretStore(uri);
    }
}
