namespace PowerPete.Analyzer.Analysis.Handlers;

using PowerPete.Analyzer.Domain;

/// <summary>
/// Dynamics 365 Contact Center: whether a conversation that arrives reaches somebody.
/// </summary>
/// <remarks>
/// Every handler here reads a column the reader took straight from the environment, and
/// every one of them treats unknown as unknown. A queue whose member count did not come back
/// is not a queue with nobody in it, and a capacity profile whose holders could not be
/// counted is not one nobody holds. Reporting either would put a critical finding on
/// configuration that is fine because of a permission on a table nobody thinks about, and the
/// people reading these findings are the ones who will know immediately that it is wrong.
///
/// Inactive configuration is skipped throughout. A deactivated queue with no members is
/// tidy, not broken.
/// </remarks>
public sealed class QueueNoMembersHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "contactCenter.queueNoMembers";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var queue in context.OfType("ccQueue"))
        {
            if (queue.Attribute<bool?>("isActive") != true) continue;

            // Zero, not null. Null is a count this product could not take.
            if (queue.Attribute<int?>("memberCount") != 0) continue;

            yield return Fire.At(RuleId, queue,
                ("memberCount", 0),
                ("queueType", queue.Attribute<string>("queueType")),
                ("assignmentStrategy", queue.Attribute<string>("assignmentStrategy")));
        }
    }
}

/// <summary>A routing queue whose assignment strategy is to assign nothing.</summary>
public sealed class QueueNoAssignmentHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "contactCenter.queueNoAssignment";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var queue in context.OfType("ccQueue"))
        {
            if (queue.Attribute<bool?>("isActive") != true) continue;
            if (queue.Attribute<bool?>("assignsNothing") != true) continue;

            // A queue with nobody in it is the louder finding and already says the work goes
            // nowhere. Saying it twice for the same queue is noise on the one that matters.
            if (queue.Attribute<int?>("memberCount") == 0) continue;

            yield return Fire.At(RuleId, queue,
                ("assignmentStrategy", queue.Attribute<string>("assignmentStrategy")),
                ("memberCount", queue.Attribute<int?>("memberCount")));
        }
    }
}

/// <summary>A capacity profile that work requires and nobody holds.</summary>
public sealed class CapacityProfileUnheldHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "contactCenter.capacityProfileUnheld";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var profile in context.OfType("ccCapacityProfile"))
        {
            if (profile.Attribute<bool?>("isActive") != true) continue;

            // Both halves have to be known, and both have to be the bad answer. A profile
            // nothing requires is unused, which is tidiness; a profile whose holders or
            // requirers could not be read is unknown, which is not a finding at all.
            if (profile.Attribute<int?>("requiredByCount") is not > 0) continue;
            if (profile.Attribute<int?>("agentCount") != 0) continue;

            yield return Fire.At(RuleId, profile,
                ("agentCount", 0),
                ("requiredBy", profile.Attribute<string>("requiredBy")),
                ("evidenceValue", profile.Attribute<string>("requiredBy")),
                ("whatToCheckFirst", "The queues those workstreams route to, for work that has been waiting "
                    + "since this profile was added."));
        }
    }
}

/// <summary>A workstream with nowhere for unmatched work to land.</summary>
public sealed class WorkstreamNoDefaultQueueHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "contactCenter.workstreamNoDefaultQueue";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var stream in context.OfType("ccWorkstream"))
        {
            if (stream.Attribute<bool?>("isActive") != true) continue;
            if (stream.Attribute<bool?>("hasDefaultQueue") != false) continue;

            yield return Fire.At(RuleId, stream,
                ("hasDefaultQueue", false),
                ("channel", stream.Attribute<string>("channel")),
                ("distribution", stream.Attribute<string>("distribution")));
        }
    }
}

/// <summary>A routing queue that has not said what happens when it cannot keep up.</summary>
public sealed class QueueNoOverflowHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "contactCenter.queueNoOverflow";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var queue in context.OfType("ccQueue"))
        {
            if (queue.Attribute<bool?>("isActive") != true) continue;

            if (queue.Attribute<bool?>("hasPreQueueOverflow") != false) continue;
            if (queue.Attribute<bool?>("hasInQueueOverflow") != false) continue;

            // Nobody in it is the louder finding, and overflow is the least of that queue's
            // problems.
            if (queue.Attribute<int?>("memberCount") == 0) continue;

            yield return Fire.At(RuleId, queue,
                ("hasPreQueueOverflow", false),
                ("hasInQueueOverflow", false),
                ("hasOperatingHours", queue.Attribute<bool?>("hasOperatingHours")),
                ("maxQueueSize", queue.Attribute<int?>("maxQueueSize")));
        }
    }
}

/// <summary>A routing queue that is open every hour as far as routing knows.</summary>
public sealed class QueueNoOperatingHoursHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "contactCenter.queueNoOperatingHours";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var queue in context.OfType("ccQueue"))
        {
            if (queue.Attribute<bool?>("isActive") != true) continue;
            if (queue.Attribute<bool?>("hasOperatingHours") != false) continue;
            if (queue.Attribute<int?>("memberCount") == 0) continue;

            yield return Fire.At(RuleId, queue,
                ("hasOperatingHours", false),
                ("queueType", queue.Attribute<string>("queueType")));
        }
    }
}

/// <summary>A routing queue whose service level measures against nothing.</summary>
public sealed class QueueNoServiceLevelHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "contactCenter.queueNoServiceLevel";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var queue in context.OfType("ccQueue"))
        {
            if (queue.Attribute<bool?>("isActive") != true) continue;

            // Null is unset. Zero is a threshold somebody typed, odd as it is, and not this
            // finding.
            if (queue.Attribute<int?>("serviceLevelSeconds") is not null) continue;
            if (queue.Attribute<int?>("memberCount") == 0) continue;

            yield return Fire.At(RuleId, queue,
                ("serviceLevelSeconds", null));
        }
    }
}

/// <summary>A capacity profile that caps an agent for the rest of the day.</summary>
public sealed class CapacityProfileDailyBlockHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "contactCenter.capacityProfileDailyBlock";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var profile in context.OfType("ccCapacityProfile"))
        {
            if (profile.Attribute<bool?>("isActive") != true) continue;
            if (profile.Attribute<bool?>("blocksAssignment") != true) continue;
            if (profile.Attribute<bool?>("resetsDaily") != true) continue;

            yield return Fire.At(RuleId, profile,
                ("defaultMaxUnits", profile.Attribute<int?>("defaultMaxUnits")),
                ("blocksAssignment", true),
                ("resetsDaily", true));
        }
    }
}
