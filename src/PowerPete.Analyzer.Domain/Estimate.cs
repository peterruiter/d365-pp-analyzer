namespace PowerPete.Analyzer.Domain;

/// <summary>
/// What a finding is expected to cost.
/// </summary>
/// <remarks>
/// A range, always. There is no constructor taking one number, the database has no column
/// for one, and a consultant who wants a single figure can take the midpoint and own that
/// decision themselves.
/// </remarks>
/// <param name="LowHours">Lower bound.</param>
/// <param name="HighHours">Upper bound.</param>
/// <param name="StoryPoints">Fibonacci, from complexity rather than from the hours.</param>
/// <param name="Confidence">How much to trust it.</param>
/// <param name="Layer">Which of the three layers produced it.</param>
/// <param name="Rationale">What the number assumes. Goes into the work item and gets read aloud.</param>
/// <param name="Assumptions">The things that would change it if they turn out differently.</param>
/// <param name="FlaggedReason">Set when it failed a sanity check and was reported rather than suppressed.</param>
public sealed record Estimate(
    decimal LowHours,
    decimal HighHours,
    int? StoryPoints,
    Confidence Confidence,
    EstimateLayer Layer,
    string Rationale,
    IReadOnlyList<string> Assumptions,
    string? FlaggedReason = null)
{
    /// <summary>The valid story point values. Anything else is a mistake somewhere upstream.</summary>
    public static IReadOnlyList<int> PointScale { get; } = [1, 2, 3, 5, 8, 13, 21];

    /// <summary>
    /// Builds one, refusing the two things that must never reach a report.
    /// </summary>
    /// <remarks>
    /// Both checks exist in the database as constraints as well. Twice, deliberately: the
    /// constraint catches a code path nobody thought about, and this catches it at the point
    /// where the error message can name the rule.
    /// </remarks>
    /// <param name="low">Lower bound.</param>
    /// <param name="high">Upper bound.</param>
    /// <param name="points">Story points, or null.</param>
    /// <param name="confidence">How much to trust it.</param>
    /// <param name="layer">Which layer produced it.</param>
    /// <param name="rationale">Why this number.</param>
    /// <param name="assumptions">What it assumes.</param>
    /// <param name="flaggedReason">Why it was flagged, when it was.</param>
    public static Estimate Create(
        decimal low,
        decimal high,
        int? points,
        Confidence confidence,
        EstimateLayer layer,
        string rationale,
        IReadOnlyList<string>? assumptions = null,
        string? flaggedReason = null)
    {
        if (low > high)
        {
            throw new ArgumentException($"An estimate of {low} to {high} hours is not a range.", nameof(low));
        }

        if (string.IsNullOrWhiteSpace(rationale))
        {
            throw new ArgumentException(
                "Every estimate carries a rationale, whichever layer produced it. It is what goes in the work item " +
                "and what gets read aloud in the room, and a number with nothing behind it cannot be defended three " +
                "months later by whoever inherits it.",
                nameof(rationale));
        }

        if (points is not null && !PointScale.Contains(points.Value))
        {
            throw new ArgumentException($"{points} is not on the point scale.", nameof(points));
        }

        return new Estimate(low, high, points, confidence, layer, rationale.Trim(), assumptions ?? [], flaggedReason);
    }

    /// <summary>The midpoint, for the one Azure DevOps field that holds a single number.</summary>
    /// <remarks>
    /// The only place in this product where a range becomes a figure, and it happens at the
    /// boundary rather than anywhere a decision gets made. The range and its rationale go
    /// into the description of the same work item, so the midpoint is never the only record.
    /// </remarks>
    public decimal Midpoint => Math.Round((LowHours + HighHours) / 2, 2);
}

/// <summary>Which of the three layers produced an estimate.</summary>
public enum EstimateLayer
{
    /// <summary>A consultant set it for this engagement. Beats everything.</summary>
    EngagementOverride,

    /// <summary>A model produced it for this finding, with its prompt version recorded.</summary>
    Model,

    /// <summary>The band the rule declares. What you get when nobody has looked at the component yet.</summary>
    BandDefault
}

/// <summary>How much to trust an estimate.</summary>
public enum Confidence
{
    /// <summary>Attributes complete, and a fix that is well understood.</summary>
    High,

    /// <summary>Something missing, or the fix depends on a choice between options.</summary>
    Medium,

    /// <summary>Somebody needs to look at the component before anybody can size it. Presented as a question, not a number to plan with.</summary>
    Low
}

/// <summary>
/// One band from the contract.
/// </summary>
/// <param name="Id">trivial, small, medium, large or none.</param>
/// <param name="Low">Lower bound in hours.</param>
/// <param name="High">Upper bound in hours.</param>
/// <param name="Rationale">Used verbatim when a finding falls back to the band, which is why it reads as a sentence.</param>
public sealed record EstimateBand(string Id, decimal Low, decimal High, string Rationale)
{
    /// <summary>Turns the band into an estimate for a finding nobody has looked at individually.</summary>
    public Estimate AsEstimate() =>
        Estimate.Create(Low, High, null, Confidence.Low, EstimateLayer.BandDefault, Rationale);
}
