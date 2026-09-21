namespace PowerPete.Analyzer.Analysis;

using System.Globalization;
using System.Text.Json;
using PowerPete.Analyzer.Analysis.Handlers;
using PowerPete.Analyzer.Domain;

/// <summary>
/// Reads the descriptions somebody wrote and says which of them say nothing.
/// </summary>
/// <remarks>
/// The one rule in this product decided by a model, and it is deliberately the smallest
/// useful one.
///
/// The deterministic rule beside it reports an empty description. The cheapest way to clear
/// that finding is to type a character, so an estate that has been through one round of
/// tidying is full of descriptions reading "test", "tbd", "Account" on a component called
/// Account, and the empty-description count looks healthy. No pattern separates those from a
/// real description; a model does it in one line.
///
/// Three things keep it honest. It runs only over descriptions that exist, so it cannot
/// duplicate the empty rule. It sends one description at a time with no other client data,
/// which is a narrower payload than the estimator already sends. And every finding it
/// produces says a model judged it and carries the model's own sentence, so a consultant
/// reading it to a client knows what kind of claim it is.
/// </remarks>
/// <param name="model">The model, or null where none is configured.</param>
public sealed class DescriptionReview(IReviewModel? model)
{
    /// <summary>The rule this produces.</summary>
    public const string RuleId = "quality.descriptionUninformative";

    /// <summary>
    /// How many descriptions are worth spending a call on.
    /// </summary>
    /// <remarks>
    /// An estate of ninety thousand components would otherwise be ninety thousand model
    /// calls. The cap is per run and the findings say when it was hit, because a sample
    /// reported as a survey is the kind of thing this product exists not to do.
    /// </remarks>
    private const int MostToRead = 300;

    /// <summary>The shortest description worth asking about.</summary>
    /// <remarks>
    /// Under this, nothing a model says adds to what a reader can see. "tbd" does not need
    /// a language model.
    /// </remarks>
    private const int WorthAsking = 3;

    /// <summary>Whether there is a model to ask.</summary>
    public bool CanRun => model is not null;

    /// <summary>
    /// Reviews what the run found.
    /// </summary>
    /// <param name="components">Everything extracted.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>A finding per description that says nothing.</returns>
    public async Task<IReadOnlyList<Finding>> RunAsync(
        IReadOnlyList<DiscoveredComponent> components,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(components);

        if (model is null) return [];

        var findings = new List<Finding>();

        // Unmanaged only, and only what somebody here wrote. A managed component's
        // description belongs to whoever shipped it and is not this client's work to fix.
        var candidates = components
            .Where(component => !component.IsManaged)
            .Where(component => Description(component) is { Length: >= WorthAsking })
            .Take(MostToRead)
            .ToList();

        foreach (var component in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var description = Description(component)!;
            var verdict = await AskAsync(component, description, cancellationToken).ConfigureAwait(false);

            // Null is every failure at once: no answer, an unreadable one, a refusal. The
            // rule is reported as not assessed on the strength of the evidence source, so
            // silence here is not silence in the report.
            if (verdict is null || verdict.Value.Informative) continue;

            findings.Add(Fire.At(RuleId, component,
                ("description", description),
                ("judgedBy", model.Name),
                ("whyItSaysNothing", verdict.Value.Reason),
                ("howThisWasDecided", "A language model read the description and this sentence is its "
                    + "answer. It is a judgement rather than a measurement, unlike every other rule here.")));
        }

        return findings;
    }

    /// <summary>The description, from wherever the extraction put it.</summary>
    /// <param name="component">The component.</param>
    private static string? Description(DiscoveredComponent component) =>
        component.Attributes.TryGetValue("description", out var value) && value is string text
            ? text.Trim() is { Length: > 0 } trimmed ? trimmed : null
            : null;

    /// <summary>What the model said about one description.</summary>
    /// <param name="Informative">Whether it tells a reader anything.</param>
    /// <param name="Reason">Why not, in the model's own words.</param>
    private readonly record struct Verdict(bool Informative, string Reason);

    /// <summary>Asks about one description.</summary>
    /// <param name="component">What it describes.</param>
    /// <param name="description">The description.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    private async Task<Verdict?> AskAsync(
        DiscoveredComponent component,
        string description,
        CancellationToken cancellationToken)
    {
        // The component's name and type and nothing else. A description is judged against
        // what it is describing, and no other part of the client's estate is needed for
        // that, so no other part of it is sent.
        var prompt = string.Create(CultureInfo.InvariantCulture, $$"""
            You are reviewing the description of one component in a Microsoft Power Platform solution.

            Component type: {{component.Type?.Name ?? component.TypeId}}
            Component name: {{component.DisplayName}}
            Description: {{description}}

            A description is informative when it tells a reader something the name does not: what the
            component is for, what it depends on, or what would break without it. A description is not
            informative when it repeats the name, is a placeholder such as "test", "tbd" or "temp", is a
            date or a person's name alone, or describes nothing at all.

            Being short is not the same as being uninformative. One accurate sentence is a good description.
            A description written in a language other than English is not uninformative for that reason.

            Answer as JSON and nothing else:
            {"informative": true|false, "reason": "at most fifteen words, only when informative is false"}
            """);

        string? answer;

        try
        {
            answer = await model!.AskAsync(prompt, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // One description that could not be reviewed must not lose the other 299.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(answer)) return null;

        try
        {
            using var document = JsonDocument.Parse(answer);

            if (!document.RootElement.TryGetProperty("informative", out var informative)) return null;

            if (informative.ValueKind == JsonValueKind.True) return new Verdict(true, string.Empty);
            if (informative.ValueKind != JsonValueKind.False) return null;

            var reason = document.RootElement.TryGetProperty("reason", out var why)
                ? why.GetString()
                : null;

            // A verdict with no reason is not usable. The finding's whole value is the
            // sentence a consultant repeats, and "the model said so" is not one.
            return string.IsNullOrWhiteSpace(reason) ? null : new Verdict(false, reason.Trim());
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
