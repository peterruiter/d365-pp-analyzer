namespace PowerPete.Analyzer.Analysis;

using PowerPete.Analyzer.Domain;

/// <summary>How hard one component is to change, as opposed to what kind of thing it is.</summary>
public enum Complexity
{
    /// <summary>Mechanical. Moves without anybody reading it first.</summary>
    Simple,

    /// <summary>Somebody has to understand it before touching it.</summary>
    Medium,

    /// <summary>A design decision before any work starts.</summary>
    Complex,

    /// <summary>
    /// Not measurable from what this extraction reached.
    /// </summary>
    /// <remarks>
    /// Its own value rather than a default to Simple. A chart showing four hundred simple
    /// components and forty unread ones is not the same chart as one showing four hundred and
    /// forty simple ones, and the second one is the one that gets quoted.
    /// </remarks>
    Unrated
}

/// <summary>One complexity band from the contract.</summary>
/// <param name="UpTo">The upper bound of the measured value, or null for the top band.</param>
/// <param name="Level">What that lands on.</param>
public sealed record ComplexityBand(int? UpTo, Complexity Level);

/// <summary>One component type's complexity rule.</summary>
/// <param name="ComponentTypeId">Which type.</param>
/// <param name="Measure">The attribute to read, or null when the rule is flat.</param>
/// <param name="Bands">The bands, in order.</param>
public sealed record ComplexityRule(string ComponentTypeId, string? Measure, IReadOnlyList<ComplexityBand> Bands)
{
    /// <summary>
    /// The rules as the contract declares them.
    /// </summary>
    /// <remarks>
    /// Converted from the generated definitions at one boundary rather than parsed in two
    /// places. An unrecognised level becomes Unrated rather than throwing, because a contract
    /// that grows a level should not stop a run: the component is reported as unmeasured,
    /// which is true.
    /// </remarks>
    public static IReadOnlyList<ComplexityRule> FromContract() =>
    [
        .. EstimateCatalogue.ComplexityRules.Select(definition => new ComplexityRule(
            definition.ComponentTypeId,
            definition.Measure,
            [.. definition.Bands.Select(band => new ComplexityBand(
                band.UpTo,
                Enum.TryParse<Complexity>(band.Level, ignoreCase: true, out var level) ? level : Complexity.Unrated))]))
    ];
}

/// <summary>
/// Rates each component simple, medium or complex.
/// </summary>
/// <remarks>
/// The third axis, and the one the assessment chart is built on. Craft says what a component
/// is made of and lifecycle says how much life it has left; neither says how hard this
/// particular instance is to move, which is the question a technical audience asks first.
///
/// Measured rather than judged. Where the measure is missing the component is unrated, and
/// unrated is a column in the chart rather than a rounding down to simple.
/// </remarks>
public sealed class ComplexityRater
{
    private readonly Dictionary<string, ComplexityRule> rules;

    /// <summary>Builds a rater over the contract's rules.</summary>
    /// <param name="rules">From component-model.json.</param>
    public ComplexityRater(IEnumerable<ComplexityRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        this.rules = rules.ToDictionary(rule => rule.ComponentTypeId, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Rates one component.</summary>
    /// <param name="component">The component.</param>
    public Complexity Rate(DiscoveredComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);

        // A component type with no rule is simple, because a table is a table and a security
        // role is a security role. A type WITH a rule whose measure came back empty is a
        // different statement and gets the different answer.
        if (!rules.TryGetValue(component.TypeId, out var rule)) return Complexity.Simple;

        if (rule.Measure is null) return rule.Bands[^1].Level;

        var value = component.Attribute<int?>(rule.Measure) ?? (int?)component.Attribute<long?>(rule.Measure);
        if (value is null) return Complexity.Unrated;

        foreach (var band in rule.Bands)
        {
            if (band.UpTo is null || value <= band.UpTo) return band.Level;
        }

        return rule.Bands[^1].Level;
    }

    /// <summary>
    /// The chart: counts per component category, split by complexity.
    /// </summary>
    /// <remarks>
    /// Categories are component types with at least one component, sorted by total descending,
    /// which puts the thing there is most of at the top where somebody will look at it.
    /// </remarks>
    /// <param name="components">Everything found.</param>
    public IReadOnlyList<CustomisationRow> ByCustomisation(IReadOnlyList<DiscoveredComponent> components)
    {
        ArgumentNullException.ThrowIfNull(components);

        return [.. components
            .GroupBy(component => component.Type?.Name ?? component.TypeId, StringComparer.Ordinal)
            .Select(group =>
            {
                var rated = group.Select(Rate).ToList();
                return new CustomisationRow(
                    group.Key,
                    rated.Count(level => level == Complexity.Simple),
                    rated.Count(level => level == Complexity.Medium),
                    rated.Count(level => level == Complexity.Complex),
                    rated.Count(level => level == Complexity.Unrated));
            })
            .OrderByDescending(row => row.Total)
            .ThenBy(row => row.Category, StringComparer.Ordinal)];
    }
}

/// <summary>One bar in the components by customisation chart.</summary>
/// <param name="Category">The component type, by its display name.</param>
/// <param name="Simple">Count.</param>
/// <param name="Medium">Count.</param>
/// <param name="Complex">Count.</param>
/// <param name="Unrated">Count that could not be measured. Its own series.</param>
public sealed record CustomisationRow(string Category, int Simple, int Medium, int Complex, int Unrated)
{
    /// <summary>Everything in this category.</summary>
    public int Total => Simple + Medium + Complex + Unrated;
}

/// <summary>Where a finding sits on the roadmap grid.</summary>
/// <param name="Row">business or process.</param>
/// <param name="Column">architecture or technology.</param>
/// <param name="Band">unclutter, accelerate or innovate.</param>
public sealed record RoadmapPosition(string Row, string Column, string Band);

/// <summary>One item on the roadmap chart.</summary>
/// <param name="Label">What it says on the box.</param>
/// <param name="Position">Where it goes.</param>
/// <param name="FindingCount">How many findings are behind it.</param>
/// <param name="LowHours">The range, summed.</param>
/// <param name="HighHours">The range, summed.</param>
/// <param name="Severity">The worst severity behind it, which decides the colour.</param>
public sealed record RoadmapItem(
    string Label,
    RoadmapPosition Position,
    int FindingCount,
    decimal LowHours,
    decimal HighHours,
    Severity Severity);

/// <summary>
/// Places findings on the roadmap grid.
/// </summary>
/// <remarks>
/// The slide a client keeps after the rest of the report is filed. One box per rule rather
/// than per finding, because forty boxes reading "orphaned column" is not a roadmap.
///
/// Placement comes from the rule catalogue rather than being decided per engagement, so two
/// assessments of two clients are comparable and neither has been arranged to please anybody.
/// </remarks>
public sealed class RoadmapBuilder
{
    private readonly IReadOnlyDictionary<string, RoadmapPosition> positions;

    /// <summary>Builds a roadmap builder.</summary>
    /// <param name="positions">Keyed by rule id.</param>
    public RoadmapBuilder(IReadOnlyDictionary<string, RoadmapPosition> positions)
    {
        this.positions = positions;
    }

    /// <summary>Builds one over the positions the catalogue declares.</summary>
    public static RoadmapBuilder FromContract() =>
        new(RuleCatalogue.Roadmap.ToDictionary(
            entry => entry.Key,
            entry => new RoadmapPosition(entry.Value.Row, entry.Value.Column, entry.Value.Band),
            StringComparer.Ordinal));

    /// <summary>What the chart shows, plus what it could not place.</summary>
    /// <param name="Items">The boxes.</param>
    /// <param name="Unplaced">Rule ids with findings and no declared position. Should always be empty; the contract check enforces it.</param>
    public sealed record Result(IReadOnlyList<RoadmapItem> Items, IReadOnlyList<string> Unplaced);

    /// <summary>Builds the chart.</summary>
    /// <param name="findings">Findings with their estimates.</param>
    public Result Build(IReadOnlyList<(Finding Finding, Estimate Estimate)> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        var items = new List<RoadmapItem>();
        var unplaced = new List<string>();

        foreach (var group in findings.GroupBy(entry => entry.Finding.RuleId, StringComparer.Ordinal))
        {
            var rule = RuleCatalogue.Find(group.Key);
            if (rule is null) continue;

            if (!positions.TryGetValue(group.Key, out var position))
            {
                unplaced.Add(group.Key);
                continue;
            }

            items.Add(new RoadmapItem(
                group.Count() > 1 ? $"{rule.Name} ({group.Count()})" : rule.Name,
                position,
                group.Count(),
                group.Sum(entry => entry.Estimate.LowHours),
                group.Sum(entry => entry.Estimate.HighHours),
                group.Min(entry => entry.Finding.Severity)));
        }

        return new Result(
            [.. items.OrderBy(item => BandOrder(item.Position.Band)).ThenByDescending(item => item.HighHours)],
            unplaced);
    }

    /// <summary>
    /// How the bands are distributed, which is a finding about the report itself.
    /// </summary>
    /// <remarks>
    /// Most findings land in unclutter, and that is the correct shape for a technical debt
    /// assessment. A roadmap where everything lands in innovate has been drawn to please
    /// somebody rather than to describe an estate, and the distribution is printed so a reader
    /// can see which kind they are holding.
    /// </remarks>
    /// <param name="items">The placed items.</param>
    public static IReadOnlyDictionary<string, int> BandProfile(IReadOnlyList<RoadmapItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return items.GroupBy(item => item.Position.Band, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.FindingCount), StringComparer.Ordinal);
    }

    private static int BandOrder(string band) => band switch
    {
        "unclutter" => 0,
        "accelerate" => 1,
        _ => 2
    };
}
