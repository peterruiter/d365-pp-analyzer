namespace PowerPete.Analyzer.Domain;

/// <summary>
/// A language model, asked one question at a time.
/// </summary>
/// <remarks>
/// The second thing in this product that calls a model, and deliberately the narrower of the
/// two. The estimator asks a model what work would cost; this asks it to read text that no
/// pattern can read, and nothing else.
///
/// What it is not for is deciding whether something is a finding. Forty-eight of the rules
/// in the catalogue key on a setting, a count or a date, and their value is that a consultant
/// can defend the number in a room. "A model thought your architecture was poor" cannot be
/// defended, so it is not a rule here. The one rule that uses this reads a description and
/// says a model judged it, in the finding, every time.
///
/// Returning null is how every failure is expressed. A model outage, a refusal, a timeout and
/// a deployment with no model configured are the same thing to a caller: the evidence was not
/// reached, and the rule is reported as not assessed rather than as passing.
/// </remarks>
public interface IReviewModel
{
    /// <summary>Which model, for the record kept against anything it produced.</summary>
    string Name { get; }

    /// <summary>
    /// Asks one question and returns the answer as text.
    /// </summary>
    /// <param name="prompt">The rendered prompt.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The answer, or null where there was not one.</returns>
    Task<string?> AskAsync(string prompt, CancellationToken cancellationToken);
}
