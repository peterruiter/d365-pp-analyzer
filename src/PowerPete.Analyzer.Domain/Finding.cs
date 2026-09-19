namespace PowerPete.Analyzer.Domain;

/// <summary>
/// One thing a rule found, against one component.
/// </summary>
/// <param name="FindingId">This run's identifier.</param>
/// <param name="StableKey">Rule plus component. What an override attaches to across runs.</param>
/// <param name="RuleId">Which rule. A checker sourced finding carries the checker's id here too.</param>
/// <param name="ComponentKey">The component, or null for a solution wide finding.</param>
/// <param name="ComponentName">Carried so a report does not have to join to say what it is about.</param>
/// <param name="Severity">From the rule, unless the handler lowered it for a reason it records in the evidence.</param>
/// <param name="Origin">Whether this product's catalogue or the Power Apps checker produced it.</param>
/// <param name="CheckerRuleId">Microsoft's rule identifier, so a consultant can look it up.</param>
/// <param name="Evidence">What actually triggered it.</param>
public sealed record Finding(
    Guid FindingId,
    string StableKey,
    string RuleId,
    string? ComponentKey,
    string? ComponentName,
    Severity Severity,
    FindingOrigin Origin,
    string? CheckerRuleId,
    IReadOnlyDictionary<string, object?> Evidence)
{
    /// <summary>The rule, from the catalogue.</summary>
    public AnalysisRule? Rule => RuleCatalogue.Find(RuleId);

    /// <summary>
    /// A finding from this product's own catalogue.
    /// </summary>
    /// <param name="rule">The rule that fired.</param>
    /// <param name="component">The component it fired against, or null for a rule with a wider scope.</param>
    /// <param name="evidence">What triggered it. Never empty: a recommendation nobody can check is one nobody will act on.</param>
    /// <param name="scope">What it is about when it is not about one component: a solution, a table, a component type. Required of any rule that can fire more than once without a component.</param>
    public static Finding From(
        AnalysisRule rule,
        DiscoveredComponent? component,
        IReadOnlyDictionary<string, object?> evidence,
        string? scope = null)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(evidence);

        if (evidence.Count == 0)
        {
            throw new ArgumentException(
                $"Rule '{rule.Id}' produced a finding with no evidence. Every finding carries what triggered it, " +
                "because a claim a consultant cannot check in front of a client is a claim that loses the room.",
                nameof(evidence));
        }

        return new Finding(
            Guid.NewGuid(),
            StableKeys.ForFinding(rule.Id, component?.StableKey, scope),
            rule.Id,
            component?.StableKey,
            component?.DisplayName,
            rule.Severity,
            FindingOrigin.Catalogue,
            null,
            evidence);
    }
}

/// <summary>Where a finding came from.</summary>
public enum FindingOrigin
{
    /// <summary>This product's rule catalogue.</summary>
    Catalogue,

    /// <summary>The Power Apps checker service, mapped onto this product's categories.</summary>
    Checker
}

/// <summary>
/// A rule that could not run, and why.
/// </summary>
/// <remarks>
/// The most important record in this product. A report reads these and names them. Nothing
/// anywhere is allowed to report a rule as passing when its evidence could not be reached,
/// and this type is how that promise is kept rather than remembered.
/// </remarks>
/// <param name="RuleId">Which rule did not run.</param>
/// <param name="Reason">In a sentence a consultant reads aloud when a client asks why a section is empty.</param>
/// <param name="MissingEvidence">Which evidence source was unreachable, where that is the cause.</param>
public sealed record NotAssessed(string RuleId, string Reason, string? MissingEvidence);

/// <summary>
/// What an extraction reached, per evidence source.
/// </summary>
/// <remarks>
/// Built from the connections on the run rather than from the contract, because a service
/// principal that was supposed to reach runtime evidence and could not is the case this
/// exists for. The contract says what a mode should reach; this says what it did.
/// </remarks>
public sealed class Reach
{
    private readonly HashSet<EvidenceSource> reached;

    /// <summary>Records which sources an extraction actually reached.</summary>
    /// <param name="sources">The ones that returned data.</param>
    public Reach(IEnumerable<EvidenceSource> sources)
    {
        reached = [.. sources];
    }

    /// <summary>Whether a source was reached.</summary>
    /// <param name="source">The source.</param>
    public bool Has(EvidenceSource source) => reached.Contains(source);

    /// <summary>
    /// Whether a rule can run at all, and if not, which source is missing.
    /// </summary>
    /// <remarks>
    /// A rule declaring several sources needs only one of them. Most rules that name both
    /// metadata and solutionZip can work from either, and demanding both would make the
    /// offline mode useless for two thirds of the catalogue.
    /// </remarks>
    /// <param name="rule">The rule.</param>
    /// <param name="missing">The sources it needed and did not get, when it cannot run.</param>
    public bool CanRun(AnalysisRule rule, out string missing)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var needed = rule.Evidence
            .Select(name => Enum.TryParse<EvidenceSource>(name, ignoreCase: true, out var parsed) ? parsed : (EvidenceSource?)null)
            .Where(source => source is not null)
            .Select(source => source!.Value)
            .ToList();

        if (needed.Count == 0)
        {
            missing = string.Empty;
            return true;
        }

        if (needed.Any(Has))
        {
            missing = string.Empty;
            return true;
        }

        missing = string.Join(", ", needed);
        return false;
    }
}

/// <summary>
/// One issue the Power Apps checker reported.
/// </summary>
/// <remarks>
/// Kept in Microsoft's own vocabulary rather than translated at the point it arrives. The
/// mapping onto this product's categories happens once, where it can be read, and the
/// original identifier travels with the finding so a consultant can look the rule up.
/// </remarks>
/// <param name="CheckerRuleId">Microsoft's identifier, for example web-unsupported-syntax.</param>
/// <param name="Category">The checker's own category.</param>
/// <param name="Severity">The checker's own severity.</param>
/// <param name="Message">What it said.</param>
/// <param name="ComponentKey">Which component it is about, where this product could match one.</param>
/// <param name="FilePath">The file it named, which is often the only handle on a web resource.</param>
/// <param name="Line">Where in the file.</param>
public sealed record CheckerIssue(
    string CheckerRuleId,
    string Category,
    string Severity,
    string Message,
    string? ComponentKey,
    string? FilePath,
    int? Line);

/// <summary>
/// Everything a rule handler is given.
/// </summary>
/// <remarks>
/// Built once per run and passed to every handler. Handlers do not read the database and do
/// not call anything: they take this and return findings, which is what makes them testable
/// without an environment and what keeps a rule's logic readable next to its contract entry.
/// </remarks>
public sealed class AnalysisContext
{
    /// <summary>Builds the context for one run.</summary>
    /// <param name="components">Every component in scope.</param>
    /// <param name="links">Resolved references between them.</param>
    /// <param name="unresolved">References to things outside the inventory.</param>
    /// <param name="reach">What the extraction actually reached.</param>
    /// <param name="environmentRole">What the environment is for. Several rules only fire against production.</param>
    /// <param name="checkerIssues">What the Power Apps checker returned, where it ran.</param>
    /// <param name="environmentCount">How many environments this engagement has connected. One rule needs more than one.</param>
    public AnalysisContext(
        IReadOnlyList<DiscoveredComponent> components,
        IReadOnlyList<ComponentLink> links,
        IReadOnlyList<UnresolvedLink> unresolved,
        Reach reach,
        string environmentRole,
        IReadOnlyList<CheckerIssue>? checkerIssues = null,
        int environmentCount = 1)
    {
        Components = components;
        Links = links;
        Unresolved = unresolved;
        Reach = reach;
        EnvironmentRole = environmentRole;
        CheckerIssues = checkerIssues ?? [];
        EnvironmentCount = environmentCount;

        byType = components.GroupBy(component => component.TypeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<DiscoveredComponent>)[.. group], StringComparer.OrdinalIgnoreCase);

        inboundCounts = links.GroupBy(link => link.ToKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    }

    private readonly Dictionary<string, IReadOnlyList<DiscoveredComponent>> byType;
    private readonly Dictionary<string, int> inboundCounts;

    /// <summary>Every component in scope.</summary>
    public IReadOnlyList<DiscoveredComponent> Components { get; }

    /// <summary>Resolved references.</summary>
    public IReadOnlyList<ComponentLink> Links { get; }

    /// <summary>References to things outside the inventory.</summary>
    public IReadOnlyList<UnresolvedLink> Unresolved { get; }

    /// <summary>What the extraction reached.</summary>
    public Reach Reach { get; }

    /// <summary>development, test, acceptance, production or unknown.</summary>
    public string EnvironmentRole { get; }

    /// <summary>What the Power Apps checker returned. Empty when it did not run.</summary>
    public IReadOnlyList<CheckerIssue> CheckerIssues { get; }

    /// <summary>How many environments are connected to this engagement.</summary>
    public int EnvironmentCount { get; }

    /// <summary>The checker issues matching one of its rule identifiers, or a prefix of one.</summary>
    /// <param name="checkerRuleIdPrefix">The identifier, or the start of a family of them.</param>
    public IReadOnlyList<CheckerIssue> Checker(string checkerRuleIdPrefix) =>
        [.. CheckerIssues.Where(issue => issue.CheckerRuleId.StartsWith(checkerRuleIdPrefix, StringComparison.OrdinalIgnoreCase))];

    /// <summary>One component by its stable key, or null.</summary>
    /// <param name="stableKey">The key.</param>
    public DiscoveredComponent? ByKey(string? stableKey) =>
        stableKey is null ? null : Components.FirstOrDefault(component => component.StableKey == stableKey);

    /// <summary>Whether this is a production environment. False for unknown, deliberately.</summary>
    /// <remarks>
    /// Rules scoped to production do not fire against an environment whose role nobody set,
    /// and report themselves as not assessed instead. Assuming production would produce
    /// critical findings on somebody's sandbox, which is how a report gets dismissed.
    /// </remarks>
    public bool IsProduction => EnvironmentRole.Equals("production", StringComparison.OrdinalIgnoreCase);

    /// <summary>Every component of one type.</summary>
    /// <param name="typeId">The component type.</param>
    public IReadOnlyList<DiscoveredComponent> OfType(string typeId) =>
        byType.TryGetValue(typeId, out var found) ? found : [];

    /// <summary>How many components point at this one.</summary>
    /// <param name="stableKey">The component's key.</param>
    public int InboundCount(string stableKey) =>
        inboundCounts.TryGetValue(stableKey, out var count) ? count : 0;

    /// <summary>What points at this one, of a given kind.</summary>
    /// <param name="stableKey">The component's key.</param>
    /// <param name="kind">The link kind, or null for all of them.</param>
    public IReadOnlyList<ComponentLink> Inbound(string stableKey, string? kind = null) =>
        [.. Links.Where(link => link.ToKey == stableKey && (kind is null || link.Kind == kind))];

    /// <summary>What this one points at.</summary>
    /// <param name="stableKey">The component's key.</param>
    /// <param name="kind">The link kind, or null for all of them.</param>
    public IReadOnlyList<ComponentLink> Outbound(string stableKey, string? kind = null) =>
        [.. Links.Where(link => link.FromKey == stableKey && (kind is null || link.Kind == kind))];
}
