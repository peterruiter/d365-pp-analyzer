namespace PowerPete.Analyzer.Data;

using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

/// <summary>What happened to one migration file.</summary>
/// <param name="Name">File name, which is also the order it runs in.</param>
/// <param name="Applied">True when this run applied it, false when it was already applied.</param>
/// <param name="Batches">How many batches it contained.</param>
/// <param name="Elapsed">How long it took.</param>
public sealed record MigrationResult(string Name, bool Applied, int Batches, TimeSpan Elapsed);

/// <summary>
/// Applies the SQL migrations that ship with this assembly.
/// </summary>
/// <remarks>
/// The migrations are embedded rather than read from disk, so a container carries its own
/// schema and cannot be deployed against a database it does not know how to build.
///
/// Every migration is written to be idempotent on its own: it creates what is absent and
/// leaves what is present alone. The ledger below is therefore not what makes reruns safe,
/// it is what makes them fast and what enforces the rule that an applied migration is never
/// edited. A file whose content no longer hashes to what was recorded is refused, because a
/// migration that changed after it ran means two databases that both claim to be up to date
/// are not the same shape.
/// </remarks>
public sealed partial class DatabaseMigrator(string connectionString)
{
    /// <summary>Splits a script on batch separators, which are not SQL and must be handled here.</summary>
    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex BatchSeparator();

    /// <summary>Reads every embedded migration, in the order its name implies.</summary>
    /// <returns>File name and content, ordered.</returns>
    public static IReadOnlyList<(string Name, string Sql)> LoadMigrations()
    {
        var assembly = Assembly.GetExecutingAssembly();

        return [.. assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Select(name => (Name: ShortName(name), Resource: name))
            // Ordinal, so 0100 sorts before 0200 and a culture cannot reorder a schema.
            .OrderBy(pair => pair.Name, StringComparer.Ordinal)
            .Select(pair => (pair.Name, Sql: Read(assembly, pair.Resource)))];
    }

    /// <summary>The resource prefix the project file gives every embedded migration.</summary>
    private const string ResourcePrefix = "PowerPete.Analyzer.Data.Migrations.";

    /// <summary>
    /// Turns an embedded resource name back into its file name.
    /// </summary>
    /// <remarks>
    /// Strips a known prefix rather than splitting on the last dot. A file called
    /// 0200_findings.sql has two dots, and splitting reduced it to
    /// "generated.sql", which both lost the ordering prefix and would have recorded the
    /// wrong name in the ledger.
    /// </remarks>
    private static string ShortName(string resourceName) =>
        resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal)
            ? resourceName[ResourcePrefix.Length..]
            : resourceName;

    /// <summary>
    /// Whether this migration is produced by the generator rather than written by hand.
    /// </summary>
    /// <remarks>
    /// Decided by name, because the alternative is a list that somebody forgets to add to.
    /// The generator names everything it writes <c>.generated.sql</c> and nothing else may
    /// use that suffix; a test holds the two in agreement.
    /// </remarks>
    /// <param name="name">The migration's file name.</param>
    private static bool IsGenerated(string name) =>
        name.EndsWith(".generated.sql", StringComparison.OrdinalIgnoreCase);

    private static string Read(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Applies every migration that has not been applied yet.
    /// </summary>
    /// <param name="progress">Called as each file is dealt with, so a long run says something.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>One result per migration file.</returns>
    public async Task<IReadOnlyList<MigrationResult>> ApplyAsync(
        Action<string>? progress,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await EnsureLedgerAsync(connection, cancellationToken);
        var applied = await ReadLedgerAsync(connection, cancellationToken);

        var results = new List<MigrationResult>();

        foreach (var (name, sql) in LoadMigrations())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var hash = Hash(sql);

            if (applied.TryGetValue(name, out var recordedHash))
            {
                // A hand written migration that changed after it ran is refused, because two
                // databases that both claim to be up to date would no longer be the same
                // shape. A generated one is the opposite case: it is recreated from the
                // contract on every build and written to be idempotent precisely so that
                // adding a field to the canonical model extends an existing database instead
                // of demanding a rebuild. Refusing it would make the documented way to add a
                // field impossible, and the advice in the message below, to write a 0900
                // migration instead, would mean hand maintaining what the generator exists to
                // produce. So it is re-applied and the ledger takes the new hash.
                if (recordedHash != hash && !IsGenerated(name))
                {
                    throw new InvalidOperationException(
                        $"Migration '{name}' has changed since it was applied to this database. " +
                        "An applied migration is never edited, because two databases that both claim to be up to " +
                        "date would no longer be the same shape. Write a new migration in the 0900 range instead, " +
                        "and revert this file.");
                }

                if (recordedHash == hash)
                {
                    results.Add(new MigrationResult(name, Applied: false, Batches: 0, Elapsed: TimeSpan.Zero));
                    progress?.Invoke($"  {name} already applied");
                    continue;
                }

                progress?.Invoke($"  {name} regenerated since it was applied, extending the schema");
            }

            progress?.Invoke($"  {name} applying");
            var started = DateTime.UtcNow;
            var batches = await ExecuteAsync(connection, sql, name, cancellationToken);
            await RecordAsync(connection, name, hash, cancellationToken);

            results.Add(new MigrationResult(name, Applied: true, batches, DateTime.UtcNow - started));
        }

        return results;
    }

    private static async Task<int> ExecuteAsync(
        SqlConnection connection,
        string sql,
        string name,
        CancellationToken cancellationToken)
    {
        // Batches are executed one at a time rather than inside a single transaction.
        //
        // That is deliberate and it is the opposite of what is usually right. Azure SQL
        // will not run several DDL statements against the same object inside one
        // transaction in the order these need, and a half applied migration is recoverable
        // here precisely because every statement checks for what it is about to create.
        // Wrapping them would trade a recoverable partial apply for an unrecoverable one.
        var batches = BatchSeparator().Split(sql)
            .Select(batch => batch.Trim())
            .Where(batch => batch.Length > 0)
            .ToList();

        for (var index = 0; index < batches.Count; index++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = batches[index];
            command.CommandTimeout = 300;

            try
            {
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (SqlException exception)
            {
                throw new InvalidOperationException(
                    string.Create(CultureInfo.InvariantCulture,
                        $"Migration '{name}' failed in batch {index + 1} of {batches.Count}: {exception.Message}\n\n" +
                        $"The batch was:\n{Excerpt(batches[index])}"),
                    exception);
            }
        }

        return batches.Count;
    }

    private static string Excerpt(string batch) =>
        batch.Length <= 800 ? batch : string.Concat(batch.AsSpan(0, 800), "\n...");

    private static async Task EnsureLedgerAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF SCHEMA_ID('ops') IS NULL EXEC('CREATE SCHEMA ops');
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        await using var ledger = connection.CreateCommand();
        ledger.CommandText = """
            IF OBJECT_ID('ops.SchemaMigration') IS NULL
                CREATE TABLE ops.SchemaMigration
                (
                    Name        NVARCHAR(200) NOT NULL CONSTRAINT PK_SchemaMigration PRIMARY KEY,
                    ContentHash NVARCHAR(64)  NOT NULL,
                    AppliedUtc  DATETIME2(3)  NOT NULL CONSTRAINT DF_SchemaMigration_AppliedUtc DEFAULT SYSUTCDATETIME()
                );
            """;
        await ledger.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Dictionary<string, string>> ReadLedgerAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        var applied = new Dictionary<string, string>(StringComparer.Ordinal);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Name, ContentHash FROM ops.SchemaMigration";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            applied[reader.GetString(0)] = reader.GetString(1);
        }

        return applied;
    }

    private static async Task RecordAsync(
        SqlConnection connection,
        string name,
        string hash,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            MERGE ops.SchemaMigration AS target
            USING (SELECT @name AS Name) AS source ON target.Name = source.Name
            WHEN MATCHED THEN UPDATE SET ContentHash = @hash, AppliedUtc = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT (Name, ContentHash) VALUES (@name, @hash);
            """;
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@hash", hash);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Hashes a migration's content, ignoring line endings.
    /// </summary>
    /// <remarks>
    /// Line endings are normalised because git rewrites them per platform, and a developer
    /// on Windows should not be told the schema changed because somebody on Linux committed
    /// the same file.
    /// </remarks>
    private static string Hash(string sql)
    {
        var normalised = sql.Replace("\r\n", "\n", StringComparison.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalised)));
    }
}
