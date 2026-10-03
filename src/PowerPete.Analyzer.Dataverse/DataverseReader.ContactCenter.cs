namespace PowerPete.Analyzer.Dataverse;

using System.Net;
using System.Text.Json;
using PowerPete.Analyzer.Domain;

/// <summary>
/// Dynamics 365 Contact Center: the workstreams, queues and capacity profiles that decide
/// whether a conversation reaches anybody.
/// </summary>
/// <remarks>
/// Every column named here was checked against Microsoft's table reference before it was
/// written, because the last reader added to this class guessed two column names on
/// msdyn_aimodel and read every AI Builder model in the estate as a single 400. A lookup is
/// selected as _name_value, which is the other half of the same lesson.
///
/// Most environments are not contact centres, and that has to read as "nothing to check"
/// rather than "could not check". A table that is not installed answers 404 on its entity
/// set, which the generic path records as a failed read, which puts every contact centre
/// rule on the not-assessed list of an ordinary sales environment as though something had
/// gone wrong there. So the question is asked first, of the metadata, where a missing table
/// is an unambiguous 404 rather than a typo in an entity set name that looks the same.
/// </remarks>
public sealed partial class DataverseReader
{
    /// <summary>The component types this half of the reader produces.</summary>
    /// <remarks>
    /// Named once, because the not-installed path has to record a read for exactly these and
    /// a fourth type added to the readers and not to this list would be the one that turned
    /// up as not assessed on every environment without a contact centre in it.
    /// </remarks>
    internal static readonly string[] ContactCenterTypes = ["ccWorkstream", "ccQueue", "ccCapacityProfile"];

    /// <summary>
    /// What a contact centre read records when there is no contact centre.
    /// </summary>
    /// <remarks>
    /// A note on a successful read, not a failure: nothing was wrong, there was simply
    /// nothing to look at. It is the only thing that tells this apart from a contact centre
    /// that is installed and empty, which records the same zero with no reason.
    /// </remarks>
    internal const string NotInstalled =
        "Dynamics 365 Contact Center is not installed in this environment, so there was nothing here to check.";

    /// <summary>A record queue: cases, email, voicemail. 192350000 is messaging, 192350002 voice.</summary>
    internal const int RecordQueue = 192350001;

    /// <summary>Push. The other value, 192350001, is pick.</summary>
    private const int DistributionPush = 192350000;

    /// <summary>Profile based capacity. Unit based is 192350000.</summary>
    private const int CapacityProfileBased = 192360000;

    /// <summary>Record routing, as opposed to a conversation channel.</summary>
    private const int StreamSourceEntityRecords = 192350000;

    /// <summary>A queue whose assignment strategy is to assign nothing.</summary>
    private const int AssignmentNone = 192350005;

    /// <summary>A capacity profile whose counter resets once a day rather than immediately.</summary>
    private const int ResetDaily = 192350001;

    /// <summary>
    /// Whether Dynamics 365 Contact Center is installed here, or why that is not known.
    /// </summary>
    /// <remarks>
    /// Asked of EntityDefinitions rather than of the entity set. A table that does not exist
    /// answers 404 on its metadata and that is the whole of its meaning there; on the entity
    /// set a 404 could equally be this reader asking for the wrong name, and the two would be
    /// indistinguishable in a run.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Installed, and the reason it could not be told where it could not.</returns>
    internal async Task<(bool Installed, string? FailureReason)> DetectContactCenterAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetAsync(
                new Uri(Api + "EntityDefinitions(LogicalName='msdyn_liveworkstream')?$select=LogicalName", UriKind.Relative),
                cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound) return (false, null);

            if (!response.IsSuccessStatusCode)
            {
                return (false,
                    $"Whether this is a contact centre environment could not be determined: Dataverse answered "
                    + $"{(int)response.StatusCode}. Every contact centre rule is reported as not assessed rather than "
                    + "as passing.");
            }

            return (true, null);
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException)
        {
            return (false, $"Whether this is a contact centre environment could not be determined: {failure.Message}");
        }
    }

    /// <summary>
    /// Workstreams, with the settings that decide where their work goes.
    /// </summary>
    /// <param name="components">Where to put them.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    private async Task<int> ReadWorkstreamsAsync(List<DiscoveredComponent> components, CancellationToken cancellationToken)
    {
        var count = 0;

        await foreach (var stream in PageAsync(
            "msdyn_liveworkstreams?$select=msdyn_liveworkstreamid,msdyn_name,msdyn_workdistributionmode,"
            + "msdyn_capacityformat,msdyn_capacityrequired,msdyn_autocloseafterinactivity,"
            + "msdyn_maxconcurrentconnection,_msdyn_defaultqueue_value,msdyn_streamsource,statecode,ismanaged",
            cancellationToken).ConfigureAwait(false))
        {
            var id = Str(stream, "msdyn_liveworkstreamid");
            var name = Str(stream, "msdyn_name");
            if (id is null || name is null) continue;

            var distribution = Int(stream, "msdyn_workdistributionmode");
            var source = Int(stream, "msdyn_streamsource");

            components.Add(Component("ccWorkstream", id, name, name, Bool(stream, "ismanaged"), null,
                new Dictionary<string, object?>
                {
                    ["isActive"] = Int(stream, "statecode") == 0,
                    ["distribution"] = distribution switch
                    {
                        DistributionPush => "push",
                        null => null,
                        _ => "pick"
                    },
                    ["capacityFormat"] = Int(stream, "msdyn_capacityformat") == CapacityProfileBased ? "profile" : "unit",
                    ["capacityRequired"] = Int(stream, "msdyn_capacityrequired"),
                    ["autoCloseAfterInactivity"] = Int(stream, "msdyn_autocloseafterinactivity"),
                    ["maxConcurrent"] = Int(stream, "msdyn_maxconcurrentconnection"),

                    // Present or absent, which is all the rule needs. The queue's own name is
                    // read with the queues, and joining the two here would mean a workstream
                    // with a queue this connection cannot see reads as having none.
                    ["hasDefaultQueue"] = Str(stream, "_msdyn_defaultqueue_value") is { Length: > 0 },
                    ["isRecordRouting"] = source == StreamSourceEntityRecords,

                    // The label Dataverse gives the channel, for a person to read. Never
                    // compared against: it is localised, and a rule keyed on "Voice" would
                    // stop working in a Dutch organisation that calls it "Spraak".
                    ["channel"] = Str(stream, "msdyn_streamsource@OData.Community.Display.V1.FormattedValue"),
                }));

            count++;
        }

        return count;
    }

    /// <summary>
    /// The queues routing actually uses: the ones marked as omnichannel queues.
    /// </summary>
    /// <remarks>
    /// Filtered on msdyn_isomnichannelqueue rather than read whole. An environment has a
    /// queue per user and per team by default, none of which routing touches, and a rule that
    /// reported each of those as having no overflow would bury the four that matter.
    ///
    /// numberofmembers is a read-only column on the queue itself, which is why membership is
    /// one column rather than a second read across queuemembership.
    /// </remarks>
    /// <param name="components">Where to put them.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    private async Task<int> ReadContactCenterQueuesAsync(List<DiscoveredComponent> components, CancellationToken cancellationToken)
    {
        var count = 0;

        await foreach (var queue in PageAsync(
            "queues?$select=queueid,name,msdyn_queuetype,msdyn_assignmentstrategy,_msdyn_operatinghourid_value,"
            + "msdyn_operatinghoursbasedassignment,_msdyn_prequeueoverflowrulesetid_value,"
            + "_msdyn_inqueueoverflowrulesetid_value,msdyn_priority,msdyn_maxqueuesize,"
            + "msdyn_queueservicelevelthreshold,numberofmembers,msdyn_isdefaultqueue,statecode"
            + "&$filter=msdyn_isomnichannelqueue eq true",
            cancellationToken).ConfigureAwait(false))
        {
            var id = Str(queue, "queueid");
            var name = Str(queue, "name");
            if (id is null || name is null) continue;

            components.Add(Component("ccQueue", id, name, name, false, null, new Dictionary<string, object?>
            {
                ["isActive"] = Int(queue, "statecode") == 0,
                ["queueType"] = Str(queue, "msdyn_queuetype@OData.Community.Display.V1.FormattedValue"),
                ["assignsNothing"] = Int(queue, "msdyn_assignmentstrategy") == AssignmentNone,
                ["assignmentStrategy"] = Str(queue, "msdyn_assignmentstrategy@OData.Community.Display.V1.FormattedValue"),
                ["hasOperatingHours"] = Str(queue, "_msdyn_operatinghourid_value") is { Length: > 0 },
                ["hasPreQueueOverflow"] = Str(queue, "_msdyn_prequeueoverflowrulesetid_value") is { Length: > 0 },
                ["hasInQueueOverflow"] = Str(queue, "_msdyn_inqueueoverflowrulesetid_value") is { Length: > 0 },
                ["priority"] = Int(queue, "msdyn_priority"),
                ["maxQueueSize"] = Int(queue, "msdyn_maxqueuesize"),
                ["serviceLevelSeconds"] = Int(queue, "msdyn_queueservicelevelthreshold"),

                // Null where the column did not come back, which is not the same as nobody.
                // A rule reading zero here is a queue nobody answers; reading null is a
                // queue this product could not count, and it must not say the first thing
                // about the second.
                ["memberCount"] = Int(queue, "numberofmembers"),
                ["isDefault"] = Bool(queue, "msdyn_isdefaultqueue"),

                // The option value, not the label. The label is in the environment's
                // language, and a rule that compared it with "Entity" would treat every
                // record queue in a Dutch environment as a live channel.
                ["isRecordQueue"] = Int(queue, "msdyn_queuetype") is { } type ? type == RecordQueue : null,
            }));

            count++;
        }

        return count;
    }

    /// <summary>
    /// Capacity profiles, with who holds them and which workstreams need them.
    /// </summary>
    /// <remarks>
    /// The profile on its own says little. What matters is the join: a workstream requiring a
    /// profile no agent holds produces work nobody is ever eligible to take, and it sits in
    /// the queue with every other setting correct. That is the most expensive contact centre
    /// misconfiguration there is to find from the outside and the cheapest to find from here.
    ///
    /// The two joins are read separately from the profiles and their failure is recorded as
    /// unknown rather than as zero. A profile whose holders could not be counted is not a
    /// profile nobody holds, and saying it was would put a high severity finding on every
    /// profile in the estate because of a permission on a table nobody thinks about.
    /// </remarks>
    /// <param name="components">Where to put them.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    private async Task<int> ReadCapacityProfilesAsync(List<DiscoveredComponent> components, CancellationToken cancellationToken)
    {
        // Who holds what. Null where it could not be read, so every count below stays null.
        Dictionary<string, int>? held = null;

        try
        {
            held = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            await foreach (var unit in PageAsync(
                "msdyn_agentcapacityprofileunits?$select=_msdyn_capacityprofileid_value,_msdyn_agentid_value"
                + "&$filter=statecode eq 0",
                cancellationToken).ConfigureAwait(false))
            {
                if (Str(unit, "_msdyn_capacityprofileid_value") is not { Length: > 0 } profile) continue;
                if (Str(unit, "_msdyn_agentid_value") is not { Length: > 0 }) continue;

                held[profile] = held.GetValueOrDefault(profile) + 1;
            }
        }
        catch (Exception failure) when (failure is HttpRequestException or JsonException)
        {
            held = null;
        }

        // Which workstreams need which profile, by workstream id, named once the workstreams
        // are known. Null where it could not be read, for the same reason.
        Dictionary<string, List<string>>? requiredBy = null;

        try
        {
            requiredBy = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            await foreach (var link in PageAsync(
                "msdyn_liveworkstreamcapacityprofiles?$select=_msdyn_workstream_id_value,_msdyn_capacityprofile_id_value"
                + "&$filter=statecode eq 0",
                cancellationToken).ConfigureAwait(false))
            {
                if (Str(link, "_msdyn_capacityprofile_id_value") is not { Length: > 0 } profile) continue;

                var stream = Str(link, "_msdyn_workstream_id_value@OData.Community.Display.V1.FormattedValue")
                    ?? Str(link, "_msdyn_workstream_id_value");

                if (stream is not { Length: > 0 }) continue;

                if (!requiredBy.TryGetValue(profile, out var streams))
                {
                    requiredBy[profile] = streams = [];
                }

                streams.Add(stream);
            }
        }
        catch (Exception failure) when (failure is HttpRequestException or JsonException)
        {
            requiredBy = null;
        }

        var count = 0;

        await foreach (var profile in PageAsync(
            "msdyn_capacityprofiles?$select=msdyn_capacityprofileid,msdyn_name,msdyn_defaultmaxunits,"
            + "msdyn_blockassignment,msdyn_resetduration,statecode,ismanaged",
            cancellationToken).ConfigureAwait(false))
        {
            var id = Str(profile, "msdyn_capacityprofileid");
            var name = Str(profile, "msdyn_name");
            if (id is null || name is null) continue;

            components.Add(Component("ccCapacityProfile", id, name, name, Bool(profile, "ismanaged"), null,
                new Dictionary<string, object?>
                {
                    ["isActive"] = Int(profile, "statecode") == 0,
                    ["defaultMaxUnits"] = Int(profile, "msdyn_defaultmaxunits"),
                    ["blocksAssignment"] = Bool(profile, "msdyn_blockassignment"),
                    ["resetsDaily"] = Int(profile, "msdyn_resetduration") == ResetDaily,

                    // Null when the join could not be read. Zero only when it could and
                    // nobody holds it, which is the finding.
                    ["agentCount"] = held is null ? (int?)null : held.GetValueOrDefault(id),
                    ["requiredBy"] = requiredBy is null
                        ? null
                        : string.Join(", ", requiredBy.GetValueOrDefault(id) ?? []),
                    ["requiredByCount"] = requiredBy is null ? (int?)null : (requiredBy.GetValueOrDefault(id)?.Count ?? 0),
                }));

            count++;
        }

        return count;
    }
}
