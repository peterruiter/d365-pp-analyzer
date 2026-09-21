namespace PowerPete.Analyzer.Estimation;

using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using OpenAI.Chat;
using PowerPete.Analyzer.Domain;

/// <summary>
/// The review model, on the same deployment the estimator uses.
/// </summary>
/// <remarks>
/// Here rather than in the analysis project because this is where the Azure OpenAI package
/// lives, and the analysis project has no business knowing which vendor answers. It sees the
/// port in the domain and nothing else.
///
/// The same deployment as the estimator deliberately. Two deployments would be two things to
/// keep consented, two costs to explain and two answers to a question about where a client's
/// text goes.
/// </remarks>
public sealed class AzureOpenAiReviewer : IReviewModel
{
    private readonly ChatClient client;

    /// <summary>Builds a reviewer.</summary>
    /// <param name="endpoint">The Azure OpenAI resource.</param>
    /// <param name="deployment">The deployment name, which is what gets recorded on every finding it produces.</param>
    /// <param name="apiKey">A key, or null to use the managed identity the container runs as.</param>
    public AzureOpenAiReviewer(Uri endpoint, string deployment, string? apiKey = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var azure = string.IsNullOrWhiteSpace(apiKey)
            ? new AzureOpenAIClient(endpoint, new DefaultAzureCredential())
            : new AzureOpenAIClient(endpoint, new AzureKeyCredential(apiKey));

        client = azure.GetChatClient(deployment);
        Name = deployment;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public async Task<string?> AskAsync(string prompt, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        try
        {
            var options = new ChatCompletionOptions
            {
                // Zero, for the same reason the estimator uses zero: the same estate
                // reviewed twice has to produce the same findings, or a client who kept the
                // first report has a question nobody can answer.
                Temperature = 0f,

                // Short on purpose. The answer is a boolean and a clause; anything longer
                // is the model explaining itself into a finding nobody can quote.
                MaxOutputTokenCount = 120,
                ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat(),
            };

            var completion = await client.CompleteChatAsync(
                [
                    new SystemChatMessage(
                        "You review documentation quality in Microsoft Power Platform solutions for a "
                        + "consultancy that has to defend every finding to the team that wrote it. You are "
                        + "strict about placeholders and generous about brevity. You answer with JSON only."),
                    new UserChatMessage(prompt),
                ],
                options,
                cancellationToken).ConfigureAwait(false);

            return completion.Value.Content.FirstOrDefault()?.Text;
        }
        catch (Exception exception) when (exception is RequestFailedException or JsonException or TaskCanceledException)
        {
            // Null, never an exception. One throttled call must not cost the run the other
            // two hundred and ninety nine descriptions, and the rule reports as not
            // assessed rather than as passing when nothing came back at all.
            return null;
        }
    }
}
