namespace PowerPete.Analyzer.Analysis;

using PowerPete.Analyzer.Domain;

/// <summary>
/// One rule's detection logic.
/// </summary>
/// <remarks>
/// The split between contract and code. The contract owns what is reported, why it matters
/// and what to do about it. A handler owns only how it is found, which is the part that has
/// to be tested rather than read.
/// </remarks>
public interface IRuleHandler
{
    /// <summary>The rule in the catalogue this handler implements.</summary>
    string RuleId { get; }

    /// <summary>
    /// Finds every instance of this rule in the estate.
    /// </summary>
    /// <remarks>
    /// Returning an empty sequence means the rule ran and found nothing, which is a result.
    /// A handler that cannot run says so by not being called: the engine checks reach first.
    /// </remarks>
    /// <param name="context">Everything the handler is allowed to look at.</param>
    IEnumerable<Finding> Run(AnalysisContext context);
}

/// <summary>
/// Runs every rule against an estate.
/// </summary>
/// <remarks>
/// Three outcomes per rule and they are all different: findings, no findings, or not
/// assessed. Collapsing the last two is the failure this product exists to avoid, so the
/// engine returns them separately and nothing downstream is able to merge them by accident.
/// </remarks>
public sealed class RuleEngine
{
    private readonly Dictionary<string, IRuleHandler> handlers;

    /// <summary>
    /// Builds the engine over a set of handlers.
    /// </summary>
    /// <remarks>
    /// Throws when a handler names a rule that is not in the catalogue. A handler for a
    /// deleted rule produces findings nothing can explain, estimate or publish, and it does
    /// it quietly.
    /// </remarks>
    /// <param name="handlers">The handlers, usually everything in the assembly.</param>
    public RuleEngine(IEnumerable<IRuleHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);

        this.handlers = [];

        foreach (var handler in handlers)
        {
            if (RuleCatalogue.Find(handler.RuleId) is null)
            {
                throw new InvalidOperationException(
                    $"Handler {handler.GetType().Name} implements rule '{handler.RuleId}', which is not in the catalogue. " +
                    "Either the rule was renamed in the contract or the handler was never finished.");
            }

            if (!this.handlers.TryAdd(handler.RuleId, handler))
            {
                throw new InvalidOperationException($"Two handlers claim rule '{handler.RuleId}'.");
            }
        }
    }

    /// <summary>What one pass produced.</summary>
    /// <param name="Findings">Everything found.</param>
    /// <param name="NotAssessed">Every rule that could not run, with the reason.</param>
    /// <param name="Ran">How many rules ran, whether or not they found anything.</param>
    public sealed record Outcome(
        IReadOnlyList<Finding> Findings,
        IReadOnlyList<NotAssessed> NotAssessed,
        int Ran);

    /// <summary>
    /// Runs every rule in the catalogue.
    /// </summary>
    /// <remarks>
    /// Driven from the catalogue rather than from the handler list, deliberately. A rule with
    /// no handler has to appear as not assessed, because the alternative is a report that
    /// silently omits a whole category and looks complete.
    /// </remarks>
    /// <param name="context">The estate.</param>
    public Outcome Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var findings = new List<Finding>();
        var notAssessed = new List<NotAssessed>();
        var ran = 0;

        foreach (var rule in RuleCatalogue.All)
        {
            if (!context.Reach.CanRun(rule, out var missing))
            {
                // A key, not a sentence. Composed in English here and stored, it printed in
                // English on a German report, in the one section a client reads most
                // carefully. The evidence source it names is stored beside it already.
                notAssessed.Add(new NotAssessed(rule.Id, NotAssessedReasons.Unreachable, missing));
                continue;
            }

            if (!handlers.TryGetValue(rule.Id, out var handler))
            {
                notAssessed.Add(new NotAssessed(rule.Id, NotAssessedReasons.NoHandler, null));
                continue;
            }

            try
            {
                findings.AddRange(handler.Run(context));
                ran++;
            }
            catch (Exception exception) when (exception is InvalidOperationException or FormatException or KeyNotFoundException)
            {
                // One broken handler does not take the run with it, and does not quietly
                // become a clean result either.
                notAssessed.Add(new NotAssessed(
                    rule.Id,
                    $"The check failed while running: {exception.Message}",
                    null));
            }
        }

        return new Outcome(findings, notAssessed, ran);
    }
}
