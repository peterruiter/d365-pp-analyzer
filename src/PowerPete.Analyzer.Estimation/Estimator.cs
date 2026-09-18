namespace PowerPete.Analyzer.Estimation;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PowerPete.Analyzer.Domain;

/// <summary>What a model returned for one finding, before anything checked it.</summary>
/// <param name="Low">Lower bound in hours.</param>
/// <param name="High">Upper bound in hours.</param>
/// <param name="StoryPoints">Fibonacci, from complexity.</param>
/// <param name="Confidence">high, medium or low.</param>
/// <param name="Rationale">Two or three sentences on what the number assumes.</param>
/// <param name="Assumptions">What would change it.</param>
public sealed record ModelEstimate(
    decimal Low,
    decimal High,
    int? StoryPoints,
    string Confidence,
    string Rationale,
    IReadOnlyList<string> Assumptions);

/// <summary>An override a consultant set for this engagement.</summary>
/// <param name="Scope">rule, ruleAndComponentType or finding.</param>
/// <param name="RuleId">Which rule, on the first two scopes.</param>
/// <param name="ComponentTypeId">Which component type, on the second.</param>
/// <param name="FindingKey">Which finding, on the third.</param>
/// <param name="Low">Lower bound.</param>
/// <param name="High">Upper bound.</param>
/// <param name="StoryPoints">Points, or null.</param>
/// <param name="Rationale">Why. Required, for the same reason the model's is.</param>
/// <param name="SetBy">Who set it.</param>
public sealed record Override(
    string Scope,
    string? RuleId,
    string? ComponentTypeId,
    string? FindingKey,
    decimal Low,
    decimal High,
    int? StoryPoints,
    string Rationale,
    string SetBy);

/// <summary>Calls a model for one finding at a time.</summary>
public interface IEstimateModel
{
    /// <summary>Which model, for the provenance record.</summary>
    string Name { get; }

    /// <summary>
    /// Estimates one finding.
    /// </summary>
    /// <param name="prompt">The rendered prompt.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<ModelEstimate?> EstimateAsync(string prompt, CancellationToken cancellationToken);
}

/// <summary>
/// Turns findings into estimates.
/// </summary>
/// <remarks>
/// Three layers with a fixed precedence and no blending. Blending produces a number nobody
/// can explain, and explaining the number is most of what this product is for.
///
/// The guards around the model layer are not decoration. A model will produce a confident
/// figure for a component it has not understood, and every check here exists to make that
/// visible rather than to pretend it does not happen.
/// </remarks>
public sealed class Estimator
{
    private readonly IEstimateModel? model;
    private readonly IReadOnlyDictionary<string, EstimateBand> bands;
    private readonly IReadOnlyList<Override> overrides;
    private readonly string promptVersion;

    /// <summary>Builds an estimator.</summary>
    /// <param name="bands">The band table from the contract.</param>
    /// <param name="overrides">Every override on this engagement.</param>
    /// <param name="model">The model, or null to run on band defaults alone.</param>
    /// <param name="promptVersion">Recorded against every model call, so a changed number is investigable.</param>
    public Estimator(
        IReadOnlyDictionary<string, EstimateBand> bands,
        IReadOnlyList<Override> overrides,
        IEstimateModel? model = null,
        string promptVersion = "1.0.0")
    {
        this.bands = bands;
        this.overrides = overrides;
        this.model = model;
        this.promptVersion = promptVersion;
    }

    /// <summary>What one estimate cost to produce, for the provenance record.</summary>
    /// <param name="Model">Which model, or null when none was called.</param>
    /// <param name="PromptVersion">Which prompt.</param>
    /// <param name="PromptHash">Hash of the rendered prompt.</param>
    /// <param name="PayloadHash">Hash of the finding as it was sent.</param>
    /// <param name="Accepted">Whether the model's answer was used.</param>
    /// <param name="RejectionReason">Why not, when it was not.</param>
    public sealed record Provenance(
        string? Model,
        string PromptVersion,
        string PromptHash,
        string PayloadHash,
        bool Accepted,
        string? RejectionReason);

    /// <summary>An estimate and how it came about.</summary>
    /// <param name="Estimate">The estimate.</param>
    /// <param name="Provenance">Null when no model was called.</param>
    public sealed record Result(Estimate Estimate, Provenance? Provenance);

    /// <summary>
    /// Estimates one finding.
    /// </summary>
    /// <param name="finding">The finding.</param>
    /// <param name="component">The component it is about, for the prompt.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<Result> EstimateAsync(
        Finding finding,
        DiscoveredComponent? component,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(finding);

        var rule = finding.Rule
            ?? throw new InvalidOperationException($"Finding {finding.FindingId} names rule '{finding.RuleId}', which is not in the catalogue.");

        var band = bands.TryGetValue(rule.EstimateBand, out var found)
            ? found
            : throw new InvalidOperationException($"Rule '{rule.Id}' uses band '{rule.EstimateBand}', which is not in the contract.");

        // Layer one. Narrowest scope wins, and it beats everything below outright.
        var applied = FindOverride(finding, component);
        if (applied is not null)
        {
            return new Result(Estimate.Create(
                applied.Low,
                applied.High,
                applied.StoryPoints,
                Confidence.High,
                EstimateLayer.EngagementOverride,
                applied.Rationale), null);
        }

        // Layer three, when there is no layer two.
        if (model is null) return new Result(band.AsEstimate(), null);

        var prompt = EstimatePrompt.Render(finding, component, rule, band);
        var promptHash = Hash(prompt);
        var payloadHash = Hash(JsonSerializer.Serialize(finding.Evidence));

        var answer = await model.EstimateAsync(prompt, cancellationToken).ConfigureAwait(false);

        if (answer is null)
        {
            return new Result(
                band.AsEstimate() with { FlaggedReason = "The model did not answer. This is the band default." },
                new Provenance(model.Name, promptVersion, promptHash, payloadHash, false, "No answer."));
        }

        if (Reject(answer, band) is { } reason)
        {
            return new Result(
                band.AsEstimate() with { FlaggedReason = $"The model's answer was rejected: {reason} This is the band default." },
                new Provenance(model.Name, promptVersion, promptHash, payloadHash, false, reason));
        }

        var flag = Flag(answer, band);

        return new Result(Estimate.Create(
            answer.Low,
            answer.High,
            answer.StoryPoints,
            ParseConfidence(answer.Confidence),
            EstimateLayer.Model,
            answer.Rationale,
            answer.Assumptions,
            flag), new Provenance(model.Name, promptVersion, promptHash, payloadHash, true, null));
    }

    /// <summary>
    /// The narrowest override that matches, or null.
    /// </summary>
    /// <remarks>
    /// Matched on the finding's stable key rather than its id. An override keyed on an id
    /// would last exactly until the next extraction, which is the same as having none.
    /// </remarks>
    private Override? FindOverride(Finding finding, DiscoveredComponent? component)
    {
        var byFinding = overrides.FirstOrDefault(entry =>
            entry.Scope == "finding" && entry.FindingKey == finding.StableKey);
        if (byFinding is not null) return byFinding;

        if (component is not null)
        {
            var byBoth = overrides.FirstOrDefault(entry =>
                entry.Scope == "ruleAndComponentType"
                && entry.RuleId == finding.RuleId
                && entry.ComponentTypeId == component.TypeId);
            if (byBoth is not null) return byBoth;
        }

        return overrides.FirstOrDefault(entry => entry.Scope == "rule" && entry.RuleId == finding.RuleId);
    }

    /// <summary>
    /// Why an answer cannot be used, or null.
    /// </summary>
    /// <remarks>
    /// A range whose high is more than eight times its low is the model saying it does not
    /// know, in a format that looks like an answer. An empty rationale is the same thing with
    /// fewer words.
    /// </remarks>
    private static string? Reject(ModelEstimate answer, EstimateBand band)
    {
        if (answer.Low < 0 || answer.High < answer.Low) return "The range is not a range.";
        if (string.IsNullOrWhiteSpace(answer.Rationale)) return "No rationale.";
        if (answer.Low > 0 && answer.High / answer.Low > 8) return "The range is too wide to plan with.";
        if (answer.StoryPoints is not null && !Estimate.PointScale.Contains(answer.StoryPoints.Value))
        {
            return $"{answer.StoryPoints} is not on the point scale.";
        }

        // A model that answers four hundred hours for a band topping out at one is not
        // estimating, it is hallucinating a project. Rejected rather than flagged.
        if (band.High > 0 && answer.High > band.High * 20) return "Wildly outside the band for this rule.";

        return null;
    }

    /// <summary>
    /// Why an answer is worth questioning, or null.
    /// </summary>
    /// <remarks>
    /// Flagged rather than rejected. A finding genuinely can be four times its band, and a
    /// consultant reading the flag is a better outcome than the product silently overruling a
    /// number somebody will have to defend.
    /// </remarks>
    private static string? Flag(ModelEstimate answer, EstimateBand band)
    {
        if (answer.High > band.High * 4)
        {
            return $"Well above the {band.Id} band, which tops out at {band.High} hours. Worth reading before it goes in a plan.";
        }

        if (band.Low > 0 && answer.Low < band.Low / 4)
        {
            return $"Well below the {band.Id} band, which starts at {band.Low} hours.";
        }

        return null;
    }

    private static Confidence ParseConfidence(string value) => value?.Trim().ToLowerInvariant() switch
    {
        "high" => Confidence.High,
        "medium" => Confidence.Medium,
        _ => Confidence.Low
    };

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..32];
}

/// <summary>
/// The prompt.
/// </summary>
/// <remarks>
/// Rendered here rather than assembled in the caller so its version means something. Every
/// model call records the version and a hash of the rendered text, which is what makes a
/// number that halved between two runs investigable rather than mysterious.
///
/// One finding per call. The model never sees a total and never produces one: totals are
/// summed from findings, which is the only way the arithmetic in a report is checkable.
/// </remarks>
public static class EstimatePrompt
{
    /// <summary>The shape the model must answer in. Literal JSON, so it carries no interpolation.</summary>
    private const string ResponseSchema =
        """
        {"low": number, "high": number, "storyPoints": 1|2|3|5|8|13|21, "confidence": "high"|"medium"|"low",
          "rationale": "string", "assumptions": ["string"]}
        """;

    /// <summary>Renders the prompt for one finding.</summary>
    /// <param name="finding">The finding.</param>
    /// <param name="component">What it is about.</param>
    /// <param name="rule">The rule that fired.</param>
    /// <param name="band">The band, passed in as an anchor.</param>
    public static string Render(Finding finding, DiscoveredComponent? component, AnalysisRule rule, EstimateBand band)
    {
        ArgumentNullException.ThrowIfNull(finding);
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(band);

        var evidence = string.Join("\n", finding.Evidence
            .Where(pair => pair.Value is not null)
            .Select(pair => $"  {pair.Key}: {pair.Value}"));

        var attributes = component is null
            ? "  (a solution wide finding, not about one component)"
            : string.Join("\n", component.Attributes
                .Where(pair => pair.Value is not null && pair.Key != "content")
                .Take(25)
                .Select(pair => $"  {pair.Key}: {pair.Value}"));

        return $"""
            You are estimating one piece of remediation work on a Microsoft Power Platform estate,
            for a consultancy that has to defend the number in front of the client who will pay it.

            THE FINDING
            Rule: {rule.Name} ({rule.Id})
            Severity: {rule.Severity}
            Why it matters: {rule.Why}
            Recommended approach: {rule.Recommendation}

            THE COMPONENT
            Name: {component?.DisplayName ?? "(none)"}
            Type: {component?.TypeId ?? "(none)"}
            Managed: {(component?.IsManaged == true ? "yes, which usually means it is not theirs to change" : "no")}
            Attributes:
            {attributes}

            EVIDENCE THAT TRIGGERED IT
            {evidence}

            ANCHOR
            Similar findings of this rule usually take {band.Low} to {band.High} hours.
            Reason for that band: {band.Rationale}
            You may go outside it. An estimate more than four times the top of the band is flagged
            for a human to read, and one more than twenty times is rejected outright.

            WHAT TO ESTIMATE
            The hours one competent consultant needs to change this component, test the change, and
            hand it over. Include understanding the component first, which is usually most of the
            work on anything that has been in production for years. Exclude project management,
            environment access and the prioritisation workshop: those are counted once per
            engagement elsewhere and counting them here would double them.

            RULES
            - Answer with a range. The lower bound is a good day, the upper bound is what happens
              when the component turns out to do something nobody documented.
            - The rationale is read aloud in a room. Say what the number assumes, in two or three
              plain sentences. Do not restate the finding back.
            - Story points come from complexity, not from the hours. A team's velocity is
              calibrated against their own points, and points that are secretly hours are useless
              to them.
            - Confidence is low when the evidence is thin or somebody has to look at the component
              before anybody can size it. Say low when it is low. A low confidence estimate is
              still used, and it is marked, which is the honest outcome.
            - If the component is managed, say so in the rationale: the work is usually raising it
              with whoever ships the solution rather than changing anything.

            Answer as JSON only, with no other text:
            {ResponseSchema}
            """;
    }
}
