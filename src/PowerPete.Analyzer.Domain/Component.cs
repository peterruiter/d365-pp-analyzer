namespace PowerPete.Analyzer.Domain;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// One component found in an estate, normalised.
/// </summary>
/// <remarks>
/// Attributes are a dictionary rather than fifty properties because the set differs per
/// component type and is declared in the contract. A rule reads what it needs by name and
/// says so, which is also what lets a rule report itself as not assessed when the attribute
/// it needs was never filled.
/// </remarks>
/// <param name="ComponentId">This run's identifier.</param>
/// <param name="StableKey">Survives a re-extraction. What an override and a work item attach to.</param>
/// <param name="TypeId">The component type, from the catalogue.</param>
/// <param name="DisplayName">What a consultant would call it.</param>
/// <param name="SchemaName">The platform's own name, where there is one.</param>
/// <param name="PlatformId">The platform's identifier, usually a GUID.</param>
/// <param name="SolutionUniqueName">Which solution it was found in.</param>
/// <param name="IsManaged">Whether it arrived in a managed solution, which usually means it is not yours to fix.</param>
/// <param name="OwnerUpn">Who owns it, where the concept applies.</param>
/// <param name="Attributes">Everything the component type declares, as far as the extraction filled it.</param>
public sealed record DiscoveredComponent(
    Guid ComponentId,
    string StableKey,
    string TypeId,
    string DisplayName,
    string? SchemaName,
    string? PlatformId,
    string? SolutionUniqueName,
    bool IsManaged,
    string? OwnerUpn,
    IReadOnlyDictionary<string, object?> Attributes)
{
    /// <summary>The component type from the catalogue, or null when the extraction produced one nobody declared.</summary>
    public ComponentType? Type => ComponentCatalogue.Find(TypeId);

    /// <summary>
    /// Reads an attribute, or the default when it was never filled.
    /// </summary>
    /// <remarks>
    /// Returns a nullable rather than a default value on purpose. A rule that cannot tell
    /// "the extraction did not fill this" from "the value is zero" will eventually report
    /// that every flow has no actions.
    /// </remarks>
    /// <typeparam name="T">What the caller expects.</typeparam>
    /// <param name="name">The attribute name, as declared in the contract.</param>
    public T? Attribute<T>(string name)
    {
        if (!Attributes.TryGetValue(name, out var value) || value is null) return default;
        if (value is T typed) return typed;

        try
        {
            return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException)
        {
            return default;
        }
    }

    /// <summary>Whether an attribute was filled at all, which is a different question from its value.</summary>
    /// <param name="name">The attribute name.</param>
    public bool Has(string name) => Attributes.TryGetValue(name, out var value) && value is not null;
}

/// <summary>
/// The key that survives a re-extraction.
/// </summary>
/// <remarks>
/// Built from the component type and the platform's own identifier, never from a display
/// name. Somebody renaming a flow must not orphan the estimate a workshop spent an hour
/// agreeing, and display names are renamed constantly.
///
/// Where a platform identifier is genuinely absent, which happens with components read only
/// from a solution file, the schema name is used and the key records that it is weaker. A
/// weak key is worth having and is worth knowing about.
/// </remarks>
public static class StableKeys
{
    /// <summary>Builds a key for one component.</summary>
    /// <param name="typeId">The component type.</param>
    /// <param name="platformId">The platform's identifier, where there is one.</param>
    /// <param name="schemaName">The fallback, where there is not.</param>
    public static string ForComponent(string typeId, string? platformId, string? schemaName)
    {
        if (!string.IsNullOrWhiteSpace(platformId))
        {
            return $"{typeId}:id:{platformId.Trim().ToLowerInvariant()}";
        }

        if (!string.IsNullOrWhiteSpace(schemaName))
        {
            return $"{typeId}:name:{schemaName.Trim().ToLowerInvariant()}";
        }

        throw new ArgumentException(
            $"A component of type '{typeId}' arrived with neither a platform identifier nor a schema name. " +
            "It cannot be given a stable key, which means no override and no work item could ever attach to it. " +
            "Fix the extractor rather than generating a key from something that changes.",
            nameof(platformId));
    }

    /// <summary>
    /// Builds a key for one finding, which is a rule against a component or against a scope.
    /// </summary>
    /// <remarks>
    /// A rule with no component used to key on the rule alone, which held only while every
    /// such rule produced at most one finding. Several of them do not: prefix sprawl fires
    /// per solution, orphaned columns per table, inconsistent naming per component type. Each
    /// of those produced two or more findings carrying one key, the database refused the
    /// second on its unique constraint, and the whole run failed at the point of saving
    /// rather than at the point of making them.
    ///
    /// The scope is the thing the finding is about when it is not about a component: a
    /// solution's unique name, a table's schema name, a component type. It is part of the key
    /// rather than only part of the evidence because an override and a published work item
    /// attach to the key, and "the naming in the flows" and "the naming in the tables" are
    /// two findings somebody will want to answer differently.
    /// </remarks>
    /// <param name="ruleId">The rule.</param>
    /// <param name="componentKey">The component's stable key, or null for a rule with a wider scope.</param>
    /// <param name="scope">What the finding is about, when it is not one component. Null for a rule that genuinely fires once.</param>
    public static string ForFinding(string ruleId, string? componentKey, string? scope = null)
    {
        if (componentKey is not null) return $"{ruleId}|{componentKey}";

        return string.IsNullOrWhiteSpace(scope)
            ? $"{ruleId}:solution"
            : $"{ruleId}:scope:{scope.Trim().ToLowerInvariant()}";
    }

    /// <summary>
    /// The deterministic identifier a published work item carries, so a second publish updates
    /// rather than duplicates.
    /// </summary>
    /// <remarks>
    /// Hashed rather than concatenated because it goes into a tag, and a tag holding a full
    /// component path is unreadable in a board filter. Truncated to sixteen characters, which
    /// is well short of a collision risk at the volumes this product produces and short enough
    /// to read on a card.
    /// </remarks>
    /// <param name="engagementId">Which engagement.</param>
    /// <param name="backlogKey">The backlog item's own key, usually rule plus component.</param>
    public static string ForWorkItem(Guid engagementId, string backlogKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{engagementId:N}|{backlogKey}"));
        return "ppa-" + Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}

/// <summary>One component pointing at another.</summary>
/// <param name="FromKey">The component that refers.</param>
/// <param name="ToKey">The component referred to.</param>
/// <param name="Kind">What kind of reference: registeredOn, usesConnection, filtersOn, readsColumn.</param>
public sealed record ComponentLink(string FromKey, string ToKey, string Kind);

/// <summary>
/// A reference to something that is not in the inventory.
/// </summary>
/// <remarks>
/// Kept rather than dropped. "This flow calls a child flow that is in no solution" is a
/// finding, and a dropped row is not.
/// </remarks>
/// <param name="FromKey">The component that refers.</param>
/// <param name="Kind">What kind of reference.</param>
/// <param name="TargetDescription">Whatever the definition said, so a person can go and look.</param>
public sealed record UnresolvedLink(string FromKey, string Kind, string TargetDescription);
