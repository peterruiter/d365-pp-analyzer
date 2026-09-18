namespace PowerPete.Analyzer.Analysis;

using PowerPete.Analyzer.Domain;

/// <summary>The numbers on the front page of a report.</summary>
/// <param name="ComponentsTotal">Everything found.</param>
/// <param name="ByCraft">Counts per craft level, including the ones outside the ratio.</param>
/// <param name="ByDomain">Counts per domain.</param>
/// <param name="ByLifecycle">Counts per lifecycle state.</param>
/// <param name="LowCodeShare">Low code as a share of the counted components, or null when nothing counts.</param>
/// <param name="RatioDefinition">Travels with the number into every export, because the number gets quoted without it.</param>
/// <param name="FindingsBySeverity">Counts per severity.</param>
/// <param name="FindingsByCategory">Counts per category.</param>
/// <param name="DebtByDomain">Estimated hours per domain, so a client sees where the money goes.</param>
/// <param name="TotalLowHours">Sum of the finding estimates.</param>
/// <param name="TotalHighHours">Sum of the finding estimates.</param>
/// <param name="FixedCostLowHours">Per engagement costs, added once and shown separately.</param>
/// <param name="FixedCostHighHours">Per engagement costs, added once and shown separately.</param>
/// <param name="NotAssessed">Every rule that could not run.</param>
/// <param name="Caveats">What a reader has to know before believing any of the above.</param>
public sealed record RunScore(
    int ComponentsTotal,
    IReadOnlyDictionary<string, int> ByCraft,
    IReadOnlyDictionary<string, int> ByDomain,
    IReadOnlyDictionary<string, int> ByLifecycle,
    decimal? LowCodeShare,
    string RatioDefinition,
    IReadOnlyDictionary<string, int> FindingsBySeverity,
    IReadOnlyDictionary<string, int> FindingsByCategory,
    IReadOnlyDictionary<string, decimal> DebtByDomain,
    decimal TotalLowHours,
    decimal TotalHighHours,
    decimal FixedCostLowHours,
    decimal FixedCostHighHours,
    IReadOnlyList<NotAssessed> NotAssessed,
    IReadOnlyList<string> Caveats);

/// <summary>One fixed cost from the contract.</summary>
/// <param name="Id">Its identifier.</param>
/// <param name="Name">What it is called in the report.</param>
/// <param name="Low">Lower bound in hours.</param>
/// <param name="High">Upper bound in hours.</param>
public sealed record FixedCost(string Id, string Name, decimal Low, decimal High);

/// <summary>
/// Computes the score.
/// </summary>
/// <remarks>
/// Every number here gets quoted in a room without the sentence that defines it, so the
/// definition is carried as data rather than written in a report template. The caveats are
/// the same idea: a reader who does not know that a third of the rules never ran will read
/// this page as a clean bill of health.
/// </remarks>
public sealed class Scorer
{
    private const string Definition =
        "Low code as a share of low code, pro code and external components. Configuration and content are " +
        "counted and shown separately, never inside the ratio: a solution with four hundred columns and one " +
        "plugin is not ninety-nine percent low code in any sense worth defending. Unweighted, because a " +
        "weighted ratio needs a defensible weight per component type and this product does not have one.";

    /// <summary>
    /// Scores one run.
    /// </summary>
    /// <param name="components">Everything found.</param>
    /// <param name="findings">Findings with their estimates.</param>
    /// <param name="notAssessed">Rules that could not run.</param>
    /// <param name="fixedCosts">Per engagement costs from the contract.</param>
    /// <param name="solutionsAnalysed">How many solutions were in scope.</param>
    /// <param name="solutionsTotal">How many exist. Different numbers are a caveat, not a footnote.</param>
    public RunScore Score(
        IReadOnlyList<DiscoveredComponent> components,
        IReadOnlyList<(Finding Finding, Estimate Estimate)> findings,
        IReadOnlyList<NotAssessed> notAssessed,
        IReadOnlyList<FixedCost> fixedCosts,
        int solutionsAnalysed,
        int solutionsTotal)
    {
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(notAssessed);
        ArgumentNullException.ThrowIfNull(fixedCosts);

        var typed = components
            .Select(component => (Component: component, Type: component.Type))
            .Where(pair => pair.Type is not null)
            .ToList();

        var byCraft = typed
            .GroupBy(pair => pair.Type!.Craft.ToString())
            .ToDictionary(group => Camel(group.Key), group => group.Count(), StringComparer.Ordinal);

        var counted = typed.Where(pair => pair.Type!.CountsTowardRatio).ToList();
        var lowCode = counted.Count(pair => pair.Type!.Craft == Craft.LowCode);
        var share = counted.Count == 0 ? (decimal?)null : Math.Round((decimal)lowCode / counted.Count, 3);

        var debtByDomain = findings
            .Where(entry => entry.Finding.Rule is not null)
            .GroupBy(entry => DomainOf(components, entry.Finding))
            .ToDictionary(
                group => group.Key,
                group => Math.Round(group.Sum(entry => (entry.Estimate.LowHours + entry.Estimate.HighHours) / 2), 1),
                StringComparer.Ordinal);

        return new RunScore(
            components.Count,
            byCraft,
            typed.GroupBy(pair => pair.Type!.Domain).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            typed.GroupBy(pair => Camel(pair.Type!.Lifecycle.ToString())).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            share,
            Definition,
            findings.GroupBy(entry => Camel(entry.Finding.Severity.ToString())).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            findings.Where(entry => entry.Finding.Rule is not null)
                .GroupBy(entry => entry.Finding.Rule!.Category)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            debtByDomain,
            findings.Sum(entry => entry.Estimate.LowHours),
            findings.Sum(entry => entry.Estimate.HighHours),
            fixedCosts.Sum(cost => cost.Low),
            fixedCosts.Sum(cost => cost.High),
            notAssessed,
            [.. Caveats(findings, notAssessed, solutionsAnalysed, solutionsTotal, counted.Count)]);
    }

    /// <summary>
    /// What a reader has to know before believing the page they are looking at.
    /// </summary>
    /// <remarks>
    /// These go at the top of the report, not in a footnote. A reader who does not know that
    /// a third of the rules never ran will read a short findings list as a clean estate, and
    /// that is the one reading this product must never produce.
    /// </remarks>
    private static IEnumerable<string> Caveats(
        IReadOnlyList<(Finding Finding, Estimate Estimate)> findings,
        IReadOnlyList<NotAssessed> notAssessed,
        int solutionsAnalysed,
        int solutionsTotal,
        int countedComponents)
    {
        if (notAssessed.Count > 0)
        {
            yield return
                $"{notAssessed.Count} of {RuleCatalogue.All.Count} checks could not run. They are listed by name " +
                "with the reason. None of them is reported as passing, and a short findings list is not the same " +
                "thing as a clean estate.";
        }

        if (solutionsAnalysed < solutionsTotal)
        {
            yield return
                $"{solutionsAnalysed} of {solutionsTotal} solutions were analysed. Components outside them are " +
                "invisible to this report, including anything sitting in the default solution.";
        }

        if (countedComponents == 0)
        {
            yield return
                "No components counted toward the low code ratio, so there is no ratio. Either the estate is " +
                "configuration only or the extraction reached less than it should have.";
        }

        var bandOnly = findings.Count(entry => entry.Estimate.Layer == EstimateLayer.BandDefault);
        if (bandOnly > 0 && findings.Count > 0)
        {
            yield return
                $"{bandOnly} of {findings.Count} estimates are band defaults rather than individual estimates. " +
                "A band is what you get when nobody has looked at the specific component yet.";
        }

        var lowConfidence = findings.Count(entry => entry.Estimate.Confidence == Confidence.Low);
        if (lowConfidence > findings.Count / 4 && findings.Count > 0)
        {
            yield return
                $"{lowConfidence} estimates carry low confidence. Somebody needs to look at those components " +
                "before the total is used for planning.";
        }

        var flagged = findings.Count(entry => entry.Estimate.FlaggedReason is not null);
        if (flagged > 0)
        {
            yield return $"{flagged} estimates were flagged as implausible and are marked individually.";
        }
    }

    private static string DomainOf(IReadOnlyList<DiscoveredComponent> components, Finding finding)
    {
        if (finding.ComponentKey is null) return "platform";

        var component = components.FirstOrDefault(candidate => candidate.StableKey == finding.ComponentKey);
        return component?.Type?.Domain ?? "platform";
    }

    private static string Camel(string value) =>
        value.Length == 0 ? value : char.ToLowerInvariant(value[0]) + value[1..];
}
