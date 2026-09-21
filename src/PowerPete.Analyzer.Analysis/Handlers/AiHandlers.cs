namespace PowerPete.Analyzer.Analysis.Handlers;

using System.Globalization;
using PowerPete.Analyzer.Domain;

/// <summary>
/// What an estate has built on AI, and whether anybody is still looking after it.
/// </summary>
/// <remarks>
/// Three component types — Copilot Studio agents, AI Builder models and AI prompts — were
/// declared in the component model with their attributes named, counted toward the low code
/// ratio, and covered by no rule at all. An estate with six agents in it got six components
/// of a type nothing looked at.
///
/// None of these handlers asks a model anything. They read settings, dates and counts, which
/// is what makes a finding here defensible in front of the team that built the agent: the
/// evidence is a switch somebody set, not an opinion about the quality of a conversation.
/// </remarks>
public sealed class AgentGenerativeNoGroundingHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "ai.agentGenerativeNoGrounding";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var agent in context.OfType("copilotStudioAgent"))
        {
            // Both halves have to be known. An agent whose configuration could not be read
            // is not an agent with no knowledge source, and reporting it as one would be a
            // finding about a gap in this product's reading.
            if (agent.Attribute<bool?>("hasGenerativeAnswers") != true) continue;

            var sources = agent.Attribute<int?>("knowledgeSourceCount");
            if (sources is null or > 0) continue;

            yield return Fire.At(RuleId, agent,
                ("hasGenerativeAnswers", true),
                ("knowledgeSourceCount", 0),
                ("topicCount", agent.Attribute<int?>("topicCount")),
                ("publishedState", agent.Attribute<string>("publishedState")),
                ("whatHappens", "The model answers from its own training, in the organisation's name, "
                    + "and nothing in the answer says where it came from."),
                ("beforeEstimating", "Check whether the knowledge arrives through a tool or a flow instead. "
                    + "An agent grounded that way is configured differently and is not this finding."));
        }
    }
}

/// <summary>An agent where everything is generated, including the answers that have a right one.</summary>
public sealed class AgentNoTopicsHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "ai.agentNoTopics";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var agent in context.OfType("copilotStudioAgent"))
        {
            var topics = agent.Attribute<int?>("topicCount");
            if (topics is null or > 0) continue;

            yield return Fire.At(RuleId, agent,
                ("topicCount", 0),
                ("hasGenerativeAnswers", agent.Attribute<bool?>("hasGenerativeAnswers")),
                ("publishedState", agent.Attribute<string>("publishedState")),
                ("whatToAuthorFirst", "Escalation to a person, opening hours, and whichever three questions "
                    + "the service desk answers most. Those have one right answer and a topic is the only "
                    + "part of an agent that can be held to it."));
        }
    }
}

/// <summary>An agent somebody built and nobody turned on.</summary>
public sealed class AgentUnpublishedHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "ai.agentUnpublished";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var agent in context.OfType("copilotStudioAgent"))
        {
            var state = agent.Attribute<string>("publishedState");
            if (!string.Equals(state, "unpublished", StringComparison.OrdinalIgnoreCase)) continue;

            yield return Fire.At(RuleId, agent,
                ("publishedState", "unpublished"),
                ("topicCount", agent.Attribute<int?>("topicCount")),
                ("knowledgeSourceCount", agent.Attribute<int?>("knowledgeSourceCount")),
                ("whatThisIs", "Built and reachable by nobody. The work is deciding, not doing."));
        }
    }
}

/// <summary>
/// A model still answering from a year-old view of the data.
/// </summary>
/// <remarks>
/// Measured from the last modification rather than the last training, because the platform
/// does not expose a training date on the model. The evidence says which it used, so nobody
/// reads a proxy as the thing itself.
/// </remarks>
public sealed class AiModelStaleHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "ai.modelStale";

    /// <summary>Twelve months. A year is the point at which nobody remembers the training set.</summary>
    private static readonly TimeSpan Stale = TimeSpan.FromDays(365);

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var model in context.OfType("aiBuilderModel"))
        {
            // Published only. An unpublished model answers nothing, and saying it is out of
            // date is a finding about something nobody is relying on.
            if (model.Attribute<int?>("statecode") != 1) continue;

            var modified = model.Attribute<string>("lastModifiedUtc");

            if (!DateTimeOffset.TryParse(modified, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal, out var last))
            {
                continue;
            }

            var age = DateTimeOffset.UtcNow - last;
            if (age < Stale) continue;

            yield return Fire.At(RuleId, model,
                ("lastModifiedUtc", last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ("monthsSince", (int)(age.TotalDays / 30)),
                ("measuredBy", "The model's last modification. The platform does not expose a training date, "
                    + "so this is a proxy and is named as one."),
                ("modelType", model.Attribute<string>("modelType")),
                ("whatToCheckFirst", "Score it against a current sample before retraining. A model that still "
                    + "performs is a date, not a problem."));
        }
    }
}

/// <summary>A model that consumed credits and answers nothing.</summary>
public sealed class AiModelUnpublishedHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "ai.modelUnpublished";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var model in context.OfType("aiBuilderModel"))
        {
            var state = model.Attribute<int?>("statecode");

            // 0 is draft, 1 is published. Anything else is a state this reader does not
            // understand, and guessing at it would invent a finding.
            if (state != 0) continue;

            yield return Fire.At(RuleId, model,
                ("statecode", 0),
                ("statuscode", model.Attribute<int?>("statuscode")),
                ("modelType", model.Attribute<string>("modelType")),
                ("lastModifiedUtc", model.Attribute<string>("lastModifiedUtc")));
        }
    }
}

/// <summary>
/// Voice, counted rather than judged.
/// </summary>
/// <remarks>
/// Information severity, like the premium connector rule and for the same reason: this
/// product cannot see what the tenant is paying, so it says what is there and stops.
/// </remarks>
public sealed class AgentRealtimeVoiceHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "ai.realtimeVoiceInUse";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var agent in context.OfType("copilotStudioAgent"))
        {
            if (agent.Attribute<bool?>("usesRealtimeVoice") != true) continue;

            yield return Fire.At(RuleId, agent,
                ("usesRealtimeVoice", true),
                ("publishedState", agent.Attribute<string>("publishedState")),
                ("whyThisIsInformation", "Voice is metered differently from text. What it costs depends on "
                    + "licences this product cannot see."));
        }
    }
}
