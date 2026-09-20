namespace PowerPete.Analyzer.Domain;

/// <summary>
/// Whether a solution is Microsoft's rather than the client's.
/// </summary>
/// <remarks>
/// Most of what a Dataverse environment contains was put there by Microsoft. Reading it is
/// the longest part of a run and it produces a report about Dynamics rather than about the
/// work the client paid somebody to do, so those solutions start unticked on the picker.
///
/// This is a judgement, not a fact the platform states, and it is deliberately arranged so
/// that being wrong is cheap. The only consequence of a wrong answer is a box that starts in
/// the wrong position, and the person looking at the list can see the publisher and the
/// component count next to it and tick it. Nothing is excluded without somebody confirming
/// the list, and everything found is recorded whether or not it was chosen.
///
/// The lists live in analysis-stages.json and are mirrored here rather than generated,
/// matching how the stages themselves are handled, with a contract test holding the two
/// together in both directions.
/// </remarks>
public static class FirstPartySolutions
{
    /// <summary>
    /// Publisher prefixes Microsoft ships under.
    /// </summary>
    /// <remarks>
    /// The strongest signal of the three. A publisher can be renamed and a solution can be
    /// renamed, but the prefix is stamped into the schema name of every component inside it
    /// and changing it is not a thing anybody does.
    /// </remarks>
    public static readonly IReadOnlyList<string> PublisherPrefixes =
    [
        "msdyn", "msdynce", "msdyn365", "msft", "mscrm",
        "microsoft", "msevtmgt", "msgxp", "msfp", "msdynmkt",
    ];

    /// <summary>Fragments of a publisher name that give it away.</summary>
    public static readonly IReadOnlyList<string> PublisherNameFragments =
    [
        "microsoft", "dynamics 365", "microsoftdynamics",
    ];

    /// <summary>
    /// Solutions named specifically, because their publisher does not give them away.
    /// </summary>
    /// <remarks>
    /// Active and Default are the two that matter. Every environment has them, they belong to
    /// the default publisher with the client's own prefix, and they hold whatever anybody
    /// customised outside a solution. Unticked by default because reading Active usually
    /// means reading the whole environment twice.
    /// </remarks>
    public static readonly IReadOnlyList<string> UniqueNames =
    [
        "Active", "Basic", "System", "Default", "ActivityFeeds", "FieldService",
        "msdynce_SalesPatch", "MicrosoftFlowExtensionsCore", "CustomerService", "SalesPatch",
    ];

    /// <summary>Whether this solution should start unticked.</summary>
    /// <param name="solution">A solution the environment reported.</param>
    /// <returns>True when it looks like Microsoft's rather than the client's.</returns>
    public static bool IsFirstParty(SolutionSummary solution)
    {
        ArgumentNullException.ThrowIfNull(solution);

        if (UniqueNames.Contains(solution.UniqueName, StringComparer.OrdinalIgnoreCase)) return true;

        if (solution.PublisherPrefix is { Length: > 0 } prefix
            && PublisherPrefixes.Contains(prefix, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (solution.PublisherName is { Length: > 0 } publisher)
        {
            foreach (var fragment in PublisherNameFragments)
            {
                if (publisher.Contains(fragment, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }

        return false;
    }
}
