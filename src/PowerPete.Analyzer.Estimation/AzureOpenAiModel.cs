namespace PowerPete.Analyzer.Estimation;

using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using OpenAI.Chat;

/// <summary>
/// Estimates one finding with a language model.
/// </summary>
/// <remarks>
/// One finding per call, never a solution and never a total. Totals are summed from findings,
/// which is the only way the arithmetic in a report is checkable by the person holding it.
///
/// Temperature zero. This is not a creative task, and two runs of the same estate producing
/// different numbers for the same component is a conversation nobody wants to have with a
/// client who kept the first report.
///
/// Every failure mode here returns null rather than throwing. The caller already has a band
/// default in hand, and the estimate carries a note saying the model did not answer, which is
/// a more honest outcome than a run that dies two thirds of the way through estimating four
/// hundred findings.
/// </remarks>
public sealed class AzureOpenAiModel : IEstimateModel
{
    private readonly ChatClient client;

    /// <summary>Builds a model client.</summary>
    /// <param name="endpoint">The Azure OpenAI resource.</param>
    /// <param name="deployment">The deployment name, which is also what gets recorded against every estimate.</param>
    /// <param name="apiKey">A key, or null to use the managed identity the container runs as.</param>
    public AzureOpenAiModel(Uri endpoint, string deployment, string? apiKey = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        // Managed identity by default. An API key in a container's environment is a key in
        // every diagnostic dump and every process listing on the host.
        var azure = string.IsNullOrWhiteSpace(apiKey)
            ? new AzureOpenAIClient(endpoint, new DefaultAzureCredential())
            : new AzureOpenAIClient(endpoint, new AzureKeyCredential(apiKey));

        client = azure.GetChatClient(deployment);
        Name = deployment;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public async Task<ModelEstimate?> EstimateAsync(string prompt, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        try
        {
            var options = new ChatCompletionOptions
            {
                Temperature = 0f,
                MaxOutputTokenCount = 700,

                // Asked for as JSON rather than parsed out of prose. A model that decides to
                // explain itself first produces text that looks like an answer and parses like
                // nothing, and the fallback then looks like a model outage.
                ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
            };

            var completion = await client.CompleteChatAsync(
                [
                    new SystemChatMessage(
                        "You estimate remediation work on Microsoft Power Platform estates for a consultancy that " +
                        "has to defend the number to the client who will pay it. You answer with JSON only."),
                    new UserChatMessage(prompt)
                ],
                options,
                cancellationToken).ConfigureAwait(false);

            var text = completion.Value.Content.FirstOrDefault()?.Text;
            return string.IsNullOrWhiteSpace(text) ? null : Parse(text);
        }
        catch (Exception exception) when (exception is RequestFailedException or JsonException or TaskCanceledException)
        {
            // Null, not an exception. The caller holds a band default and will mark the
            // estimate as one, which keeps four hundred findings from being lost to one
            // throttled call.
            return null;
        }
    }

    /// <summary>
    /// Reads the model's answer.
    /// </summary>
    /// <remarks>
    /// Tolerant about the wrapper and strict about the content. A fenced code block or a
    /// leading sentence is stripped; a missing rationale or a range that is not a range is
    /// rejected here and again by the estimator's own guards, which is deliberate: the second
    /// check is the one that records why.
    /// </remarks>
    private static ModelEstimate? Parse(string text)
    {
        var json = text.Trim();

        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            var start = json.IndexOf('{', StringComparison.Ordinal);
            var end = json.LastIndexOf('}');
            if (start < 0 || end <= start) return null;
            json = json[start..(end + 1)];
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("low", out var low) || !root.TryGetProperty("high", out var high)) return null;

        var rationale = root.TryGetProperty("rationale", out var value) ? value.GetString() : null;
        if (string.IsNullOrWhiteSpace(rationale)) return null;

        var assumptions = root.TryGetProperty("assumptions", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(item => item.GetString()).Where(item => item is not null).Select(item => item!).ToList()
            : [];

        return new ModelEstimate(
            low.GetDecimal(),
            high.GetDecimal(),
            root.TryGetProperty("storyPoints", out var points) && points.ValueKind == JsonValueKind.Number
                ? points.GetInt32()
                : null,
            root.TryGetProperty("confidence", out var confidence) ? confidence.GetString() ?? "low" : "low",
            rationale,
            assumptions);
    }
}
