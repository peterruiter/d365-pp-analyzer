namespace PowerPete.Analyzer.Data;

using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;

/// <summary>
/// Writes what a run found and reads it back for the screens.
/// </summary>
/// <remarks>
/// Written rather than recomputed, for the reason the sibling products store their
/// assessments: a figure that quietly changes because a catalogue was retuned three weeks
/// later is a figure nobody can be held to, and the number in a statement of work has to be
/// the number the tool produced on the day.
/// </remarks>
public sealed class AnalysisStore(string connectionString)
{
    private SqlConnection Connect() => new(connectionString);

    /// <summary>
    /// Records one entity read, successful or not.
    /// </summary>
    /// <remarks>
    /// Called for every attempt rather than for every success. A read that returned nothing and
    /// a read that was refused are different facts, the database has a constraint that makes
    /// them impossible to confuse, and everything downstream depends on telling them apart.
    /// </remarks>
    /// <param name="runId">Which run.</param>
    /// <param name="componentTypeId">What was being read.</param>
    /// <param name="evidenceSource">Where from.</param>
    /// <param name="succeeded">Whether it worked.</param>
    /// <param name="recordCount">How many, on success.</param>
    /// <param name="failureReason">Why not, on failure.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task RecordEntityReadAsync(
        Guid runId,
        string componentTypeId,
        string evidenceSource,
        bool succeeded,
        int? recordCount,
        string? failureReason,
        CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        await connection.ExecuteAsync(new CommandDefinition(
            """
            MERGE stg.EntityRead AS target
            USING (SELECT @runId AS RunId, @componentTypeId AS ComponentTypeId, @evidenceSource AS EvidenceSource) AS source
                ON target.RunId = source.RunId
                AND target.ComponentTypeId = source.ComponentTypeId
                AND target.EvidenceSource = source.EvidenceSource
            WHEN MATCHED THEN UPDATE SET
                Succeeded = @succeeded, RecordCount = @recordCount, FailureReason = @failureReason, ReadUtc = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (RunId, ComponentTypeId, EvidenceSource, Succeeded, RecordCount, FailureReason)
                VALUES (@runId, @componentTypeId, @evidenceSource, @succeeded, @recordCount, @failureReason);
            """,
            new { runId, componentTypeId, evidenceSource, succeeded, recordCount, failureReason },
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Writes the inventory.
    /// </summary>
    /// <remarks>
    /// Craft, lifecycle and domain are copied in rather than joined at read time. The catalogue
    /// moves between releases and a report has to keep saying what it said on the day it was
    /// issued.
    /// </remarks>
    /// <param name="runId">Which run.</param>
    /// <param name="components">Everything found, already normalised.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task WriteComponentsAsync(
        Guid runId,
        IReadOnlyList<(Guid ComponentId, string StableKey, string TypeId, string DisplayName, string? SchemaName,
            string? PlatformId, string? SolutionUniqueName, string Craft, string Lifecycle, string Domain,
            bool IsManaged, bool IsCustom, string? OwnerUpn, string AttributesJson)> components,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(components);
        if (components.Count == 0) return;

        await using var connection = Connect();

        // Merged on the stable key rather than inserted. Normalising twice updates rather than
        // duplicating, so a mapping defect is fixed and replayed without going back to the
        // client's environment, which on a delegated connection means going back to a person.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            MERGE inv.Component AS target
            USING (SELECT @runId AS RunId, @StableKey AS StableKey) AS source
                ON target.RunId = source.RunId AND target.StableKey = source.StableKey
            WHEN MATCHED THEN UPDATE SET
                DisplayName = @DisplayName, SchemaName = @SchemaName, PlatformId = @PlatformId,
                Craft = @Craft, Lifecycle = @Lifecycle, Domain = @Domain,
                IsManaged = @IsManaged, IsCustom = @IsCustom, OwnerUpn = @OwnerUpn, AttributesJson = @AttributesJson,
                SolutionId = (SELECT TOP 1 SolutionId FROM inv.Solution
                              WHERE RunId = @runId AND UniqueName = @SolutionUniqueName)
            WHEN NOT MATCHED THEN
                INSERT (ComponentId, RunId, SolutionId, ComponentTypeId, StableKey, PlatformId, DisplayName,
                        SchemaName, Craft, Lifecycle, Domain, IsManaged, IsCustom, OwnerUpn, AttributesJson)
                VALUES (@ComponentId, @runId,
                        (SELECT TOP 1 SolutionId FROM inv.Solution
                         WHERE RunId = @runId AND UniqueName = @SolutionUniqueName),
                        @TypeId, @StableKey, @PlatformId, @DisplayName,
                        @SchemaName, @Craft, @Lifecycle, @Domain, @IsManaged, @IsCustom, @OwnerUpn, @AttributesJson);
            """,
            components.Select(component => new
            {
                runId,
                component.ComponentId,
                component.StableKey,
                component.TypeId,
                component.DisplayName,
                component.SchemaName,
                component.PlatformId,
                component.SolutionUniqueName,
                component.Craft,
                component.Lifecycle,
                component.Domain,
                component.IsManaged,
                component.IsCustom,
                component.OwnerUpn,
                component.AttributesJson
            }),
            cancellationToken: cancellationToken));
    }

    /// <summary>Writes a finding and its estimate together.</summary>
    /// <param name="runId">Which run.</param>
    /// <param name="engagementId">Which engagement.</param>
    /// <param name="findings">Findings with estimates.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task WriteFindingsAsync(
        Guid runId,
        Guid engagementId,
        IReadOnlyList<(Guid FindingId, string StableKey, string RuleId, string? ComponentKey, string Severity,
            string Category, string Origin, string? CheckerRuleId, string EvidenceJson,
            decimal LowHours, decimal HighHours, int? StoryPoints, string Confidence, string Layer,
            string Rationale, string? AssumptionsJson, string? FlaggedReason)> findings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(findings);
        if (findings.Count == 0) return;

        await using var connection = Connect();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO findings.Finding
                (FindingId, RunId, EngagementId, RuleId, ComponentId, StableKey, Severity, Category, Origin, CheckerRuleId, EvidenceJson)
            SELECT @FindingId, @runId, @engagementId, @RuleId, c.ComponentId, @StableKey, @Severity, @Category, @Origin, @CheckerRuleId, @EvidenceJson
            FROM (SELECT 1 AS x) AS dummy
            LEFT JOIN inv.Component c ON c.RunId = @runId AND c.StableKey = @ComponentKey;
            """,
            findings.Select(entry => new
            {
                runId,
                engagementId,
                entry.FindingId,
                entry.RuleId,
                entry.ComponentKey,
                entry.StableKey,
                entry.Severity,
                entry.Category,
                entry.Origin,
                entry.CheckerRuleId,
                entry.EvidenceJson
            }),
            transaction, cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO findings.Estimate
                (FindingId, RunId, Layer, LowHours, HighHours, StoryPoints, Confidence, Rationale, AssumptionsJson, FlaggedReason)
            VALUES
                (@FindingId, @runId, @Layer, @LowHours, @HighHours, @StoryPoints, @Confidence, @Rationale, @AssumptionsJson, @FlaggedReason);
            """,
            findings.Select(entry => new
            {
                runId,
                entry.FindingId,
                entry.Layer,
                entry.LowHours,
                entry.HighHours,
                entry.StoryPoints,
                entry.Confidence,
                entry.Rationale,
                entry.AssumptionsJson,
                entry.FlaggedReason
            }),
            transaction, cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Records every rule that could not run.</summary>
    /// <param name="runId">Which run.</param>
    /// <param name="notAssessed">Rule id, reason and the missing evidence source.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task WriteNotAssessedAsync(
        Guid runId,
        IReadOnlyList<(string RuleId, string Reason, string? MissingEvidence)> notAssessed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notAssessed);
        if (notAssessed.Count == 0) return;

        await using var connection = Connect();

        await connection.ExecuteAsync(new CommandDefinition(
            """
            MERGE inv.NotAssessed AS target
            USING (SELECT @runId AS RunId, @RuleId AS RuleId) AS source
                ON target.RunId = source.RunId AND target.RuleId = source.RuleId
            WHEN MATCHED THEN UPDATE SET Reason = @Reason, MissingEvidence = @MissingEvidence
            WHEN NOT MATCHED THEN INSERT (RunId, RuleId, Reason, MissingEvidence)
                VALUES (@runId, @RuleId, @Reason, @MissingEvidence);
            """,
            notAssessed.Select(entry => new { runId, entry.RuleId, entry.Reason, entry.MissingEvidence }),
            cancellationToken: cancellationToken));
    }

    /// <summary>The overrides on an engagement, which beat everything the model produces.</summary>
    /// <param name="engagementId">Which engagement.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<IReadOnlyList<(string Scope, string? RuleId, string? ComponentTypeId, string? FindingKey,
        decimal Low, decimal High, int? StoryPoints, string Rationale, string SetBy)>> GetOverridesAsync(
        Guid engagementId,
        CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        var rows = await connection.QueryAsync<(string, string?, string?, string?, decimal, decimal, int?, string, string)>(
            new CommandDefinition(
                """
                SELECT Scope, RuleId, ComponentTypeId, FindingKey, LowHours, HighHours, StoryPoints, Rationale, SetBy
                FROM findings.EngagementOverride
                WHERE EngagementId = @engagementId;
                """,
                new { engagementId },
                cancellationToken: cancellationToken));

        return [.. rows];
    }

    /// <summary>Stores the score, with the breakdown and the definitions as they stood on the day.</summary>
    /// <param name="runId">Which run.</param>
    /// <param name="engagementId">Which engagement.</param>
    /// <param name="score">The numbers.</param>
    /// <param name="breakdown">Everything the charts read, as JSON.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task WriteScoreAsync(
        Guid runId,
        Guid engagementId,
        (int SolutionsAnalysed, int SolutionsTotal, int ComponentsTotal, int LowCode, int ProCode, int External,
         int Config, int Content, int Ageing, int FindingsTotal, int Critical, int High, int NotAssessed,
         decimal TotalLow, decimal TotalHigh, decimal FixedLow, decimal FixedHigh) score,
        object breakdown,
        CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        await connection.ExecuteAsync(new CommandDefinition(
            """
            MERGE findings.RunScore AS target
            USING (SELECT @runId AS RunId) AS source ON target.RunId = source.RunId
            WHEN MATCHED THEN UPDATE SET BreakdownJson = @breakdownJson, CreatedUtc = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (RunId, EngagementId, SolutionsAnalysed, SolutionsTotal, ComponentsTotal,
                        LowCodeCount, ProCodeCount, ExternalCount, ConfigCount, ContentCount, AgeingCount,
                        FindingsTotal, CriticalCount, HighCount, NotAssessedCount,
                        TotalLowHours, TotalHighHours, FixedCostLowHours, FixedCostHighHours, BreakdownJson)
                VALUES (@runId, @engagementId, @SolutionsAnalysed, @SolutionsTotal, @ComponentsTotal,
                        @LowCode, @ProCode, @External, @Config, @Content, @Ageing,
                        @FindingsTotal, @Critical, @High, @NotAssessed,
                        @TotalLow, @TotalHigh, @FixedLow, @FixedHigh, @breakdownJson);
            """,
            new
            {
                runId,
                engagementId,
                score.SolutionsAnalysed,
                score.SolutionsTotal,
                score.ComponentsTotal,
                score.LowCode,
                score.ProCode,
                score.External,
                score.Config,
                score.Content,
                score.Ageing,
                score.FindingsTotal,
                score.Critical,
                score.High,
                score.NotAssessed,
                score.TotalLow,
                score.TotalHigh,
                score.FixedLow,
                score.FixedHigh,
                breakdownJson = JsonSerializer.Serialize(breakdown)
            },
            cancellationToken: cancellationToken));
    }

    /// <summary>Writes the backlog.</summary>
    /// <param name="runId">Which run.</param>
    /// <param name="engagementId">Which engagement.</param>
    /// <param name="items">The items, parents first.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task WriteBacklogAsync(
        Guid runId,
        Guid engagementId,
        IReadOnlyList<(Guid BacklogItemId, Guid? ParentItemId, string WorkItemType, string Title, string DescriptionHtml,
            string AcceptanceCriteria, string TestRequirement, int Priority, int? StoryPoints,
            decimal? LowHours, decimal? HighHours, string TagsJson, string DeterministicKey)> items,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0) return;

        await using var connection = Connect();

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO findings.BacklogItem
                (BacklogItemId, RunId, EngagementId, ParentItemId, WorkItemType, Title, DescriptionHtml,
                 AcceptanceCriteria, TestRequirement, Priority, StoryPoints, LowHours, HighHours, TagsJson, DeterministicKey)
            VALUES
                (@BacklogItemId, @runId, @engagementId, @ParentItemId, @WorkItemType, @Title, @DescriptionHtml,
                 @AcceptanceCriteria, @TestRequirement, @Priority, @StoryPoints, @LowHours, @HighHours, @TagsJson, @DeterministicKey);
            """,
            items.Select(item => new
            {
                runId,
                engagementId,
                item.BacklogItemId,
                item.ParentItemId,
                item.WorkItemType,
                item.Title,
                item.DescriptionHtml,
                item.AcceptanceCriteria,
                item.TestRequirement,
                item.Priority,
                item.StoryPoints,
                item.LowHours,
                item.HighHours,
                item.TagsJson,
                item.DeterministicKey
            }),
            cancellationToken: cancellationToken));
    }

    /// <summary>Records what a publish created, so a publish into the wrong project can be found rather than hunted.</summary>
    /// <param name="runId">Which run.</param>
    /// <param name="published">What was created or updated.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task WritePublishedAsync(
        Guid runId,
        IReadOnlyList<(Guid BacklogItemId, string Organisation, string Project, int WorkItemId, string Url, string Action)> published,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(published);
        if (published.Count == 0) return;

        await using var connection = Connect();

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO findings.PublishedWorkItem
                (PublishedWorkItemId, RunId, BacklogItemId, Organisation, Project, WorkItemId, Url, Action)
            VALUES
                (NEWID(), @runId, @BacklogItemId, @Organisation, @Project, @WorkItemId, @Url, @Action);
            """,
            published.Select(entry => new
            {
                runId,
                entry.BacklogItemId,
                entry.Organisation,
                entry.Project,
                entry.WorkItemId,
                entry.Url,
                entry.Action
            }),
            cancellationToken: cancellationToken));
    }

    // ------------------------------------------------------------------ reading --
    //
    // Everything above writes what a run found. Everything below reads it back for the
    // screens and the exports.
    //
    // There is one rule they all share and it is not optional: a query that returns nothing
    // must be distinguishable from a query that could not run. Every read below either
    // returns rows or throws, and none of them swallows an error into an empty list, because
    // an empty findings list renders as a clean estate on the one screen where that conclusion
    // gets made.

    /// <summary>One finding, joined to its estimate and its component, as a screen needs it.</summary>
    /// <param name="FindingId">Its identifier.</param>
    /// <param name="StableKey">What an override attaches to.</param>
    /// <param name="RuleId">Which rule.</param>
    /// <param name="Severity">How much it matters.</param>
    /// <param name="Category">Which part of the report it appears under.</param>
    /// <param name="Origin">This catalogue or the Power Apps checker.</param>
    /// <param name="CheckerRuleId">Microsoft's identifier, where the checker produced it.</param>
    /// <param name="EvidenceJson">What triggered it.</param>
    /// <param name="ComponentName">What it is about.</param>
    /// <param name="ComponentTypeId">What kind of thing that is.</param>
    /// <param name="SolutionName">Which solution it was found in.</param>
    /// <param name="IsManaged">Whether it is somebody else's to fix.</param>
    /// <param name="LowHours">The range.</param>
    /// <param name="HighHours">The range.</param>
    /// <param name="StoryPoints">Points, or null.</param>
    /// <param name="Confidence">How much to trust the estimate.</param>
    /// <param name="Layer">Which of the three layers produced it.</param>
    /// <param name="Rationale">Why this number.</param>
    /// <param name="FlaggedReason">Why it was questioned, where it was.</param>
    public sealed record FindingRow(
        Guid FindingId,
        string StableKey,
        string RuleId,
        string Severity,
        string Category,
        string Origin,
        string? CheckerRuleId,
        string EvidenceJson,
        string? ComponentName,
        string? ComponentTypeId,
        string? SolutionName,
        bool IsManaged,
        decimal LowHours,
        decimal HighHours,
        int? StoryPoints,
        string Confidence,
        string Layer,
        string Rationale,
        string? FlaggedReason);

    /// <summary>
    /// Every finding on a run, with its estimate and its component.
    /// </summary>
    /// <remarks>
    /// One query rather than three. A screen that fetched findings, then estimates, then
    /// components would show them disagreeing while somebody is reading them, and the
    /// disagreement would be transient and unreproducible.
    ///
    /// The join to the component is left, deliberately: a solution wide finding has no
    /// component and an inner join would silently drop exactly the findings that outrank
    /// everything else in the report.
    /// </remarks>
    /// <param name="runId">Which run.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<IReadOnlyList<FindingRow>> GetFindingsAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        var rows = await connection.QueryAsync<FindingRow>(new CommandDefinition(
            """
            SELECT
                f.FindingId, f.StableKey, f.RuleId, f.Severity, f.Category, f.Origin, f.CheckerRuleId, f.EvidenceJson,
                c.DisplayName AS ComponentName, c.ComponentTypeId, s.UniqueName AS SolutionName,
                ISNULL(c.IsManaged, 0) AS IsManaged,
                e.LowHours, e.HighHours, e.StoryPoints, e.Confidence, e.Layer, e.Rationale, e.FlaggedReason
            FROM findings.Finding f
            INNER JOIN findings.Estimate e ON e.FindingId = f.FindingId
            LEFT JOIN inv.Component c ON c.ComponentId = f.ComponentId
            LEFT JOIN inv.Solution s ON s.SolutionId = c.SolutionId
            WHERE f.RunId = @runId
            ORDER BY
                CASE f.Severity
                    WHEN 'critical' THEN 0 WHEN 'high' THEN 1 WHEN 'medium' THEN 2 WHEN 'low' THEN 3 ELSE 4
                END,
                f.RuleId;
            """,
            new { runId },
            cancellationToken: cancellationToken));

        return [.. rows];
    }

    /// <summary>Every rule that could not run on a run.</summary>
    /// <param name="runId">Which run.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<IReadOnlyList<(string RuleId, string Reason, string? MissingEvidence)>> GetNotAssessedAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        var rows = await connection.QueryAsync<(string, string, string?)>(new CommandDefinition(
            "SELECT RuleId, Reason, MissingEvidence FROM inv.NotAssessed WHERE RunId = @runId ORDER BY RuleId;",
            new { runId },
            cancellationToken: cancellationToken));

        return [.. rows];
    }

    /// <summary>The stored score, or null when the run never reached the score stage.</summary>
    /// <remarks>
    /// Null rather than a zeroed record. A run that failed during extraction has no score, and
    /// a screen showing zeros for one is showing an estate with nothing in it.
    /// </remarks>
    /// <param name="runId">Which run.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<(int ComponentsTotal, int FindingsTotal, int NotAssessedCount,
        decimal TotalLowHours, decimal TotalHighHours, decimal FixedCostLowHours, decimal FixedCostHighHours,
        string BreakdownJson)?> GetScoreAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        var row = await connection.QuerySingleOrDefaultAsync<(int, int, int, decimal, decimal, decimal, decimal, string)?>(
            new CommandDefinition(
                """
                SELECT ComponentsTotal, FindingsTotal, NotAssessedCount,
                       TotalLowHours, TotalHighHours, FixedCostLowHours, FixedCostHighHours, BreakdownJson
                FROM findings.RunScore WHERE RunId = @runId;
                """,
                new { runId },
                cancellationToken: cancellationToken));

        return row;
    }

    /// <summary>The latest run on an engagement that produced findings, or null.</summary>
    /// <remarks>
    /// Latest that got as far as scoring, not latest that was started. A screen defaulting to
    /// a run that died in extraction would show an empty estate and look like a finished one.
    /// </remarks>
    /// <param name="engagementId">Which engagement.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<Guid?> GetLatestScoredRunAsync(Guid engagementId, CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        return await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            """
            SELECT TOP (1) RunId FROM findings.RunScore
            WHERE EngagementId = @engagementId
            ORDER BY CreatedUtc DESC;
            """,
            new { engagementId },
            cancellationToken: cancellationToken));
    }

    /// <summary>The backlog a run produced.</summary>
    /// <param name="runId">Which run.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<IReadOnlyList<(Guid BacklogItemId, Guid? ParentItemId, string WorkItemType, string Title,
        string AcceptanceCriteria, string TestRequirement, int Priority, int? StoryPoints,
        decimal? LowHours, decimal? HighHours, string DeterministicKey)>> GetBacklogAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        var rows = await connection.QueryAsync<(Guid, Guid?, string, string, string, string, int, int?, decimal?, decimal?, string)>(
            new CommandDefinition(
                """
                SELECT BacklogItemId, ParentItemId, WorkItemType, Title, AcceptanceCriteria, TestRequirement,
                       Priority, StoryPoints, LowHours, HighHours, DeterministicKey
                FROM findings.BacklogItem
                WHERE RunId = @runId
                ORDER BY CASE WorkItemType
                    WHEN 'epic' THEN 0 WHEN 'feature' THEN 1 WHEN 'story' THEN 2 WHEN 'bug' THEN 3 ELSE 4 END,
                    Priority, Title;
                """,
                new { runId },
                cancellationToken: cancellationToken));

        return [.. rows];
    }

    /// <summary>
    /// Sets an override for this engagement.
    /// </summary>
    /// <remarks>
    /// Replaces any existing one at the same scope rather than adding a second. Two overrides
    /// competing for one finding would resolve by whichever the query returned first, which is
    /// a number nobody could explain and which could change between two runs.
    ///
    /// The rationale is checked here as well as by the database constraint and by the form.
    /// Three times, because it is the number that reaches a statement of work.
    /// </remarks>
    /// <param name="engagementId">Which engagement.</param>
    /// <param name="scope">rule, ruleAndComponentType or finding.</param>
    /// <param name="ruleId">Which rule, on the first two scopes.</param>
    /// <param name="componentTypeId">Which component type, on the second.</param>
    /// <param name="findingKey">The finding's stable key, on the third.</param>
    /// <param name="low">Lower bound.</param>
    /// <param name="high">Upper bound.</param>
    /// <param name="storyPoints">Points, or null.</param>
    /// <param name="rationale">Why. Required.</param>
    /// <param name="setBy">Who set it.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task SetOverrideAsync(
        Guid engagementId,
        string scope,
        string? ruleId,
        string? componentTypeId,
        string? findingKey,
        decimal low,
        decimal high,
        int? storyPoints,
        string rationale,
        string setBy,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rationale))
        {
            throw new ArgumentException(
                "An override needs a reason. It is the number that ends up in the statement of work, and one with " +
                "nothing behind it is indistinguishable from a typo three months later.",
                nameof(rationale));
        }

        if (low > high) throw new ArgumentException("That is not a range.", nameof(low));

        await using var connection = Connect();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM findings.EngagementOverride
            WHERE EngagementId = @engagementId
              AND Scope = @scope
              AND ISNULL(RuleId, '') = ISNULL(@ruleId, '')
              AND ISNULL(ComponentTypeId, '') = ISNULL(@componentTypeId, '')
              AND ISNULL(FindingKey, '') = ISNULL(@findingKey, '');
            """,
            new { engagementId, scope, ruleId, componentTypeId, findingKey },
            transaction, cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO findings.EngagementOverride
                (OverrideId, EngagementId, Scope, RuleId, ComponentTypeId, FindingKey,
                 LowHours, HighHours, StoryPoints, Rationale, SetBy)
            VALUES
                (NEWID(), @engagementId, @scope, @ruleId, @componentTypeId, @findingKey,
                 @low, @high, @storyPoints, @rationale, @setBy);
            """,
            new { engagementId, scope, ruleId, componentTypeId, findingKey, low, high, storyPoints, rationale, setBy },
            transaction, cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes an override, so the layer below it applies again.</summary>
    /// <param name="engagementId">Which engagement.</param>
    /// <param name="overrideId">Which override.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task RemoveOverrideAsync(Guid engagementId, Guid overrideId, CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM findings.EngagementOverride WHERE EngagementId = @engagementId AND OverrideId = @overrideId;",
            new { engagementId, overrideId },
            cancellationToken: cancellationToken));
    }

    /// <summary>One attempt to read one component type, as it was recorded.</summary>
    /// <param name="ComponentTypeId">What was being read.</param>
    /// <param name="EvidenceSource">Where it was being read from.</param>
    /// <param name="Succeeded">Whether it was read at all.</param>
    /// <param name="RecordCount">How many, on a success. Never set on a failure.</param>
    /// <param name="FailureReason">Why not, on a failure. Never absent on one.</param>
    public sealed record EntityReadRow(
        string ComponentTypeId,
        string EvidenceSource,
        bool Succeeded,
        int? RecordCount,
        string? FailureReason);

    /// <summary>
    /// What one run reached, and what it did not.
    /// </summary>
    /// <remarks>
    /// The failures are the reason this table exists. A component type that could not be read
    /// has to look different on a screen from one that was read and held nothing, and the two
    /// constraints on this table make the difference impossible to record wrongly: a success
    /// carries a count and a failure carries a reason.
    /// </remarks>
    /// <param name="runId">Which run.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<IReadOnlyList<EntityReadRow>> GetEntityReadsAsync(
        Guid runId, CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        var rows = await connection.QueryAsync<EntityReadRow>(new CommandDefinition(
            """
            SELECT ComponentTypeId, EvidenceSource, Succeeded, RecordCount, FailureReason
            FROM stg.EntityRead
            WHERE RunId = @runId
            ORDER BY Succeeded DESC, ComponentTypeId;
            """,
            new { runId },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    /// <summary>One solution a run read, as it was recorded.</summary>
    /// <param name="UniqueName">The name everything else keys on.</param>
    /// <param name="FriendlyName">What a person calls it.</param>
    /// <param name="Version">Its version, where the export carried one.</param>
    /// <param name="IsManaged">Whether it is managed, which decides whose it is to change.</param>
    /// <param name="PublisherPrefix">The prefix on its components.</param>
    /// <param name="PublisherName">Who publishes it.</param>
    public sealed record SolutionRow(
        string UniqueName,
        string FriendlyName,
        string? Version,
        bool IsManaged,
        string? PublisherPrefix,
        string? PublisherName);

    /// <summary>
    /// Records the solutions a run read.
    /// </summary>
    /// <remarks>
    /// Written before the components, because a component resolves its solution by unique name
    /// and a solution that is not here yet leaves every component in it unattributed. A report
    /// that cannot say which solution a finding is in is one nobody can act on.
    /// </remarks>
    /// <param name="runId">Which run.</param>
    /// <param name="solutions">What it read.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task WriteSolutionsAsync(
        Guid runId, IReadOnlyList<SolutionRow> solutions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(solutions);
        if (solutions.Count == 0) return;

        await using var connection = Connect();

        await connection.ExecuteAsync(new CommandDefinition(
            """
            MERGE inv.Solution AS target
            USING (SELECT @runId AS RunId, @UniqueName AS UniqueName) AS source
                ON target.RunId = source.RunId AND target.UniqueName = source.UniqueName
            WHEN MATCHED THEN UPDATE SET
                FriendlyName = @FriendlyName, Version = @Version, IsManaged = @IsManaged,
                PublisherPrefix = @PublisherPrefix, PublisherName = @PublisherName
            WHEN NOT MATCHED THEN
                INSERT (SolutionId, RunId, UniqueName, FriendlyName, Version, IsManaged,
                        PublisherPrefix, PublisherName)
                VALUES (NEWID(), @runId, @UniqueName, @FriendlyName, @Version, @IsManaged,
                        @PublisherPrefix, @PublisherName);
            """,
            solutions.Select(solution => new
            {
                runId,
                solution.UniqueName,
                solution.FriendlyName,
                solution.Version,
                solution.IsManaged,
                solution.PublisherPrefix,
                solution.PublisherName
            }),
            cancellationToken: cancellationToken));
    }

    /// <summary>The solutions one run read.</summary>
    /// <param name="runId">Which run.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<IReadOnlyList<SolutionRow>> GetSolutionsAsync(
        Guid runId, CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        var rows = await connection.QueryAsync<SolutionRow>(new CommandDefinition(
            """
            SELECT UniqueName, FriendlyName, Version, IsManaged, PublisherPrefix, PublisherName
            FROM inv.Solution WHERE RunId = @runId ORDER BY UniqueName;
            """,
            new { runId },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    /// <summary>One component, as it was recorded.</summary>
    /// <param name="ComponentId">Its identifier.</param>
    /// <param name="StableKey">What findings and overrides key on.</param>
    /// <param name="TypeId">Which kind of component.</param>
    /// <param name="DisplayName">What it is called.</param>
    /// <param name="SchemaName">The platform's own name.</param>
    /// <param name="PlatformId">Its identifier in the environment.</param>
    /// <param name="SolutionUniqueName">Which solution it came from.</param>
    /// <param name="IsManaged">Whether it is managed.</param>
    /// <param name="OwnerUpn">Who owns it, where anybody does.</param>
    /// <param name="AttributesJson">Everything the reader found, as it found it.</param>
    public sealed record ComponentRow(
        Guid ComponentId,
        string StableKey,
        string TypeId,
        string DisplayName,
        string? SchemaName,
        string? PlatformId,
        string? SolutionUniqueName,
        bool IsManaged,
        string? OwnerUpn,
        string AttributesJson);

    /// <summary>
    /// Every component one run found.
    /// </summary>
    /// <remarks>
    /// The inventory is the half of a report the findings do not cover. A client reading that
    /// they have nine findings wants to know nine out of what, and the answer is this.
    /// </remarks>
    /// <param name="runId">Which run.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<IReadOnlyList<ComponentRow>> GetComponentsAsync(
        Guid runId, CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        var rows = await connection.QueryAsync<ComponentRow>(new CommandDefinition(
            """
            SELECT c.ComponentId, c.StableKey, c.ComponentTypeId AS TypeId, c.DisplayName, c.SchemaName,
                   c.PlatformId, s.UniqueName AS SolutionUniqueName, c.IsManaged, c.OwnerUpn, c.AttributesJson
            FROM inv.Component c
            LEFT JOIN inv.Solution s ON s.SolutionId = c.SolutionId
            WHERE c.RunId = @runId
            ORDER BY c.ComponentTypeId, c.DisplayName;
            """,
            new { runId },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }
}
