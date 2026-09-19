namespace PowerPete.Analyzer.Api;

/// <summary>
/// Where the contract files are, wherever this is running from.
/// </summary>
/// <remarks>
/// The API serves three screens straight out of <c>build/contracts</c>: the connection
/// wizard from the extraction sources, and the narrative and maturity screens from the
/// report model. The container copies that folder next to the application, so
/// <see cref="AppContext.BaseDirectory"/> found it there and four call sites said so.
///
/// From a repository run the base directory is <c>bin/Debug/net9.0</c> and the folder is
/// four levels above it, so every one of those endpoints answered 500 and the screens that
/// depend on them could not be opened at all on a developer's machine. The connections page
/// showed "500 Internal Server Error" where the wizard should be, with nothing in the log,
/// because a missing file is not an exception until something reads it.
///
/// The same walk the localiser already does for its bundles, in one place rather than in
/// each caller, so a fifth endpoint cannot get it wrong a fifth time.
/// </remarks>
public static class ContractFiles
{
    /// <summary>The folder, resolved once.</summary>
    public static string Directory { get; } = Find();

    /// <summary>One contract file by name.</summary>
    /// <param name="name">The file, for example <c>report-model.json</c>.</param>
    public static string Path(string name) => System.IO.Path.Combine(Directory, name);

    private static string Find()
    {
        var relative = System.IO.Path.Combine("build", "contracts");

        foreach (var start in new[] { AppContext.BaseDirectory, System.IO.Directory.GetCurrentDirectory() })
        {
            var directory = new DirectoryInfo(start);

            while (directory is not null)
            {
                var candidate = System.IO.Path.Combine(directory.FullName, relative);

                if (System.IO.Directory.Exists(candidate)) return candidate;

                directory = directory.Parent;
            }
        }

        // Nothing found. The callers all check the file exists and answer with an empty
        // list, which is the same behaviour as before and better than refusing to start.
        return System.IO.Path.Combine(AppContext.BaseDirectory, relative);
    }
}
