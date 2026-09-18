namespace PowerPete.Analyzer.Data;

using Dapper;
using Microsoft.Data.SqlClient;

/// <summary>
/// What one person may do.
/// </summary>
/// <param name="IsKnown">Whether they have been admitted to the product at all.</param>
/// <param name="HasAccess">Whether they may do anything: a global admin, or somebody with at least one engagement.</param>
/// <param name="IsGlobalAdmin">Whether they see every engagement and may admit other people.</param>
/// <param name="Roles">Their role in each engagement they have been given.</param>
public sealed record UserAccess(
    bool IsKnown,
    bool HasAccess,
    bool IsGlobalAdmin,
    IReadOnlyDictionary<Guid, string> Roles)
{
    /// <summary>Nobody, for a caller who has signed in and been admitted to nothing.</summary>
    public static UserAccess None { get; } = new(false, false, false, new Dictionary<Guid, string>());

    /// <summary>
    /// Everything, for a developer machine with no tenant.
    /// </summary>
    /// <remarks>
    /// There is nobody to admit them and nothing to admit them to. The alternative is a
    /// developer who cannot open the product they are building.
    /// </remarks>
    public static UserAccess Everything { get; } = new(true, true, true, new Dictionary<Guid, string>());

    /// <summary>
    /// Whether they hold at least the role named, in that engagement.
    /// </summary>
    /// <remarks>
    /// Ranked rather than compared, so a check for Contributor is satisfied by an Admin.
    /// Comparing strings would mean every caller listing the roles that count, and one of
    /// them eventually forgetting Admin.
    /// </remarks>
    /// <param name="engagementId">Which engagement.</param>
    /// <param name="role">The least they must be: Admin, Contributor or Viewer.</param>
    public bool Holds(Guid engagementId, string role)
    {
        if (IsGlobalAdmin) return true;
        if (!Roles.TryGetValue(engagementId, out var held)) return false;

        return Rank(held) >= Rank(role);
    }

    private static int Rank(string role) => EngagementRoles.Rank(role);
}

/// <summary>
/// The three levels somebody can hold on an engagement.
/// </summary>
/// <remarks>
/// The same three words Intent Miner uses, deliberately. The two products sit beside each
/// other in the same suite and a consultant has usually already used the other one; two
/// products calling the same level by different names is a support conversation every time
/// somebody is granted access.
///
/// Compared without regard to case, because a role arrives from a select box in a browser
/// and from a JSON body written by hand, and rejecting Viewer because somebody typed viewer
/// teaches people that the product is fussy rather than that they made a mistake.
/// </remarks>
public static class EngagementRoles
{
    /// <summary>Full control of one engagement, including who else is on it.</summary>
    public const string Admin = "Admin";

    /// <summary>Can do the work: connect sources, run discoveries, produce a plan.</summary>
    public const string Contributor = "Contributor";

    /// <summary>Can read everything and change nothing.</summary>
    public const string Viewer = "Viewer";

    /// <summary>All three, strongest first, which is the order the picker shows them in reverse.</summary>
    public static IReadOnlyList<string> All { get; } = [Admin, Contributor, Viewer];

    /// <summary>
    /// The canonical spelling of a role, or null when it is not one.
    /// </summary>
    /// <param name="role">Whatever arrived.</param>
    public static string? Canonical(string? role) =>
        All.FirstOrDefault(known => known.Equals(role?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// How much a role carries, for comparing one against another.
    /// </summary>
    /// <remarks>
    /// Zero for anything unrecognised, so a role the check constraint somehow let through
    /// grants nothing rather than everything. Failing closed matters more here than anywhere
    /// else in the product.
    /// </remarks>
    /// <param name="role">The role.</param>
    public static int Rank(string? role) => Canonical(role) switch
    {
        Admin => 3,
        Contributor => 2,
        Viewer => 1,
        _ => 0
    };
}

/// <summary>One person admitted to the product, as the administration screen shows them.</summary>
/// <param name="UserId">Their normalised sign-in name, which is what everything else keys on.</param>
/// <param name="DisplayName">Their name.</param>
/// <param name="Email">Their address.</param>
/// <param name="IsGlobalAdmin">Whether they see everything.</param>
/// <param name="CreatedUtc">When they were admitted.</param>
/// <param name="CreatedBy">Who admitted them.</param>
public sealed record AdmittedUser(
    string UserId,
    string DisplayName,
    string? Email,
    bool IsGlobalAdmin,
    DateTime CreatedUtc,
    string CreatedBy);

/// <summary>One person on one engagement, as the access list shows them.</summary>
/// <param name="UserId">Their normalised sign-in name.</param>
/// <param name="DisplayName">Their name, so the list reads as people rather than addresses.</param>
/// <param name="Email">Their address.</param>
/// <param name="Role">Admin, Contributor or Viewer.</param>
public sealed record EngagementMember(string UserId, string DisplayName, string? Email, string Role);

/// <summary>One grant, for listing what somebody can reach.</summary>
/// <param name="UserId">Whose grant it is.</param>
/// <param name="EngagementId">Which engagement.</param>
/// <param name="EngagementName">Its name, so the administration screen needs no second query.</param>
/// <param name="Role">Admin, Contributor or Viewer.</param>
public sealed record UserGrant(string UserId, Guid EngagementId, string EngagementName, string Role);

/// <summary>
/// Who may use the product, and what they may do.
/// </summary>
/// <remarks>
/// Keyed on the sign-in name rather than the Entra object id, lowercased. That is what lets
/// the first administrator be created by configuration before they have ever signed in: an
/// object id cannot be known in advance, and a product where the first person cannot get in
/// is a product nobody can set up.
///
/// Signing in and being admitted are separate. Anybody in the tenant can do the first; only
/// somebody with a row here can do the second. A tool that reads a client's whole solution estate is not one that everybody with a mailbox should be able to open.
/// </remarks>
public sealed class AccessStore(string connectionString)
{
    /// <summary>
    /// The demonstration engagement, which everybody admitted may read.
    /// </summary>
    /// <remarks>
    /// Repeated here rather than referenced from the pipeline, because the data layer does
    /// not depend on the pipeline and should not start. The seeder owns the value; a test
    /// holds the two to each other.
    /// </remarks>
    public static readonly Guid DemoEngagementId = Guid.Parse("de300000-0000-4000-8000-000000000001");

    private SqlConnection Connect() => new(connectionString);

    /// <summary>The form a sign-in name is stored in.</summary>
    /// <remarks>
    /// Lowercased, because Entra is happy to present the same person as Peter@example.com
    /// one day and peter@example.com the next, and two rows for one person means somebody
    /// loses their engagements without anything having changed.
    /// </remarks>
    /// <param name="value">A sign-in name.</param>
    public static string Normalise(string value) => value.Trim().ToLowerInvariant();

    /// <summary>
    /// Makes sure the configured first administrator exists and is one.
    /// </summary>
    /// <remarks>
    /// Run on every access check rather than once at startup. A container that started
    /// before the setting was added would otherwise stay locked out until it was restarted,
    /// and the person locked out is by definition the one who cannot fix it.
    ///
    /// It only ever grants. Taking the role away is done from the administration screen by
    /// a person, because a configuration value that silently demotes somebody on the next
    /// deployment is a way to lose access to your own product.
    /// </remarks>
    /// <param name="upn">Sign-in name of the first administrator.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task EnsureInitialGlobalAdminAsync(string? upn, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(upn)) return;

        var userId = Normalise(upn);

        await using var connection = Connect();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            MERGE ops.SystemUser AS target
            USING (SELECT @userId AS UserId) AS source ON target.UserId = source.UserId
            WHEN MATCHED AND target.IsGlobalAdmin = 0 THEN UPDATE SET
                IsGlobalAdmin = 1, UpdatedUtc = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (UserId, DisplayName, Email, IsGlobalAdmin, CreatedBy)
                VALUES (@userId, @upn, @userId, 1, N'configuration');
            """,
            new { userId, upn },
            cancellationToken: cancellationToken));
    }

    /// <summary>What one person may do.</summary>
    /// <param name="userId">Their sign-in name.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<UserAccess> GetAccessAsync(string userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId)) return UserAccess.None;

        var normalised = Normalise(userId);

        await using var connection = Connect();

        var isGlobalAdmin = await connection.ExecuteScalarAsync<bool?>(new CommandDefinition(
            "SELECT IsGlobalAdmin FROM ops.SystemUser WHERE UserId = @normalised;",
            new { normalised },
            cancellationToken: cancellationToken));

        if (isGlobalAdmin is null) return UserAccess.None;

        var grants = await connection.QueryAsync<(Guid EngagementId, string Role)>(new CommandDefinition(
            """
            SELECT a.EngagementId, a.Role
            FROM ops.EngagementAccess a
            INNER JOIN ops.Engagement e ON e.EngagementId = a.EngagementId
            WHERE a.UserId = @normalised;
            """,
            new { normalised },
            cancellationToken: cancellationToken));

        var roles = grants.ToDictionary(grant => grant.EngagementId, grant => grant.Role);

        // Everybody admitted can read the demonstration engagement.
        //
        // Granted here rather than written as a row per person, so it cannot drift, cannot be
        // revoked by accident and does not need repairing when somebody new is admitted. It
        // is reader, never more: the demonstration is there to be explored, not edited, and a
        // consultant who changes it changes it for everyone.
        //
        // TryAdd, so a real grant on the same engagement wins. That only happens on a
        // deployment where somebody has deliberately given themselves more.
        roles.TryAdd(DemoEngagementId, EngagementRoles.Viewer);

        return new UserAccess(true, isGlobalAdmin.Value || roles.Count > 0, isGlobalAdmin.Value, roles);
    }

    /// <summary>Everybody admitted to the product.</summary>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<IReadOnlyList<AdmittedUser>> ListUsersAsync(CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        var rows = await connection.QueryAsync<AdmittedUser>(new CommandDefinition(
            """
            SELECT UserId, DisplayName, Email, IsGlobalAdmin, CreatedUtc, CreatedBy
            FROM ops.SystemUser
            ORDER BY IsGlobalAdmin DESC, UserId;
            """,
            cancellationToken: cancellationToken));

        return [.. rows];
    }

    /// <summary>Admits somebody, or changes whether they are a global administrator.</summary>
    /// <param name="upn">Their sign-in name.</param>
    /// <param name="displayName">What to call them until they sign in and tell us.</param>
    /// <param name="isGlobalAdmin">Whether they see everything.</param>
    /// <param name="admittedBy">Who is admitting them.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task AdmitAsync(
        string upn, string displayName, bool isGlobalAdmin, string admittedBy, CancellationToken cancellationToken)
    {
        var userId = Normalise(upn);

        await using var connection = Connect();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            MERGE ops.SystemUser AS target
            USING (SELECT @userId AS UserId) AS source ON target.UserId = source.UserId
            WHEN MATCHED THEN UPDATE SET
                IsGlobalAdmin = @isGlobalAdmin, UpdatedUtc = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (UserId, DisplayName, Email, IsGlobalAdmin, CreatedBy)
                VALUES (@userId, @displayName, @userId, @isGlobalAdmin, @admittedBy);
            """,
            new { userId, displayName, isGlobalAdmin, admittedBy },
            cancellationToken: cancellationToken));
    }

    /// <summary>Records what somebody is actually called, once they have signed in.</summary>
    /// <remarks>
    /// Separate from admitting them, and deliberately incapable of creating a row. Somebody
    /// signing in must not be able to admit themselves as a side effect of the product
    /// asking who they are.
    /// </remarks>
    /// <param name="userId">Their sign-in name.</param>
    /// <param name="displayName">What Entra says they are called.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task RecordDisplayNameAsync(string userId, string displayName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return;

        await using var connection = Connect();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE ops.SystemUser
            SET DisplayName = @displayName, UpdatedUtc = SYSUTCDATETIME()
            WHERE UserId = @userId AND DisplayName <> @displayName;
            """,
            new { userId = Normalise(userId), displayName },
            cancellationToken: cancellationToken));
    }

    /// <summary>Removes somebody from the product entirely.</summary>
    /// <param name="upn">Their sign-in name.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>How many people were removed, which is zero or one.</returns>
    public async Task<int> RemoveAsync(string upn, CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        // Their engagement grants go with them, by the cascade on EngagementAccess.
        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM ops.SystemUser WHERE UserId = @userId;",
            new { userId = Normalise(upn) },
            cancellationToken: cancellationToken));
    }

    /// <summary>How many global administrators there are.</summary>
    /// <remarks>
    /// Asked before removing one or taking the role away, because a product with no global
    /// administrator cannot admit anybody ever again. The only way back is the configuration
    /// setting, and on a deployment where nobody remembers it there is no way back at all.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<int> CountGlobalAdminsAsync(CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM ops.SystemUser WHERE IsGlobalAdmin = 1;",
            cancellationToken: cancellationToken));
    }

    /// <summary>Everybody who may see one engagement, and what they may do there.</summary>
    /// <param name="engagementId">Which engagement.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<IReadOnlyList<(string UserId, string Role)>> GetEngagementAccessAsync(
        Guid engagementId, CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        var rows = await connection.QueryAsync<(string UserId, string Role)>(new CommandDefinition(
            "SELECT UserId, Role FROM ops.EngagementAccess WHERE EngagementId = @engagementId ORDER BY UserId;",
            new { engagementId },
            cancellationToken: cancellationToken));

        return [.. rows];
    }

    /// <summary>
    /// Everybody on one engagement, with the names the administration screen shows.
    /// </summary>
    /// <remarks>
    /// Joined in the query rather than stitched together in the caller. An access list
    /// showing sign-in names is a list somebody has to translate into people in their head,
    /// and the two screens that show it both need the same answer.
    /// </remarks>
    /// <param name="engagementId">Which engagement.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<IReadOnlyList<EngagementMember>> GetEngagementMembersAsync(
        Guid engagementId, CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        var rows = await connection.QueryAsync<EngagementMember>(new CommandDefinition(
            """
            SELECT a.UserId, u.DisplayName, u.Email, a.Role
              FROM ops.EngagementAccess a
             INNER JOIN ops.SystemUser u ON u.UserId = a.UserId
             WHERE a.EngagementId = @engagementId
             ORDER BY u.DisplayName, a.UserId;
            """,
            new { engagementId },
            cancellationToken: cancellationToken));

        return [.. rows];
    }

    /// <summary>
    /// Every engagement grant anybody holds, for the user administration screen.
    /// </summary>
    /// <remarks>
    /// One query for everybody rather than one per person. The screen lists every admitted
    /// user with what they can reach, and doing that a row at a time is how a list of forty
    /// people becomes forty round trips.
    ///
    /// Archived engagements are excluded, because a grant on something nobody is working on is not
    /// access to anything and listing it only raises a question with no answer.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<IReadOnlyList<UserGrant>> ListAllGrantsAsync(CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        var rows = await connection.QueryAsync<UserGrant>(new CommandDefinition(
            """
            SELECT a.UserId, a.EngagementId, e.Name AS EngagementName, a.Role
              FROM ops.EngagementAccess a
             INNER JOIN ops.Engagement e ON e.EngagementId = a.EngagementId
             WHERE e.Status <> N'archived'
             ORDER BY e.Name;
            """,
            cancellationToken: cancellationToken));

        return [.. rows];
    }

    /// <summary>
    /// Admitted people who are not yet on one engagement.
    /// </summary>
    /// <remarks>
    /// What the grant picker offers. Somebody already on the engagement is left out because
    /// granting them again is a change of role rather than a grant, and the list they are
    /// already in is the place to do that.
    /// </remarks>
    /// <param name="engagementId">Which engagement.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<IReadOnlyList<AdmittedUser>> GetAvailableUsersAsync(
        Guid engagementId, CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        var rows = await connection.QueryAsync<AdmittedUser>(new CommandDefinition(
            """
            SELECT u.UserId, u.DisplayName, u.Email, u.IsGlobalAdmin, u.CreatedUtc, u.CreatedBy
              FROM ops.SystemUser u
             WHERE NOT EXISTS (
                   SELECT 1 FROM ops.EngagementAccess a
                    WHERE a.UserId = u.UserId AND a.EngagementId = @engagementId)
             ORDER BY u.DisplayName, u.UserId;
            """,
            new { engagementId },
            cancellationToken: cancellationToken));

        return [.. rows];
    }

    /// <summary>Gives somebody a role in an engagement, or changes the one they have.</summary>
    /// <param name="engagementId">Which engagement.</param>
    /// <param name="upn">Their sign-in name. They must already be admitted.</param>
    /// <param name="role">Admin, Contributor or Viewer.</param>
    /// <param name="grantedBy">Who granted it.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task GrantAsync(
        Guid engagementId, string upn, string role, string grantedBy, CancellationToken cancellationToken)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            MERGE ops.EngagementAccess AS target
            USING (SELECT @engagementId AS EngagementId, @userId AS UserId) AS source
                ON target.EngagementId = source.EngagementId AND target.UserId = source.UserId
            WHEN MATCHED THEN UPDATE SET Role = @role
            WHEN NOT MATCHED THEN
                INSERT (EngagementId, UserId, Role, GrantedBy)
                VALUES (@engagementId, @userId, @role, @grantedBy);
            """,
            new { engagementId, userId = Normalise(upn), role, grantedBy },
            cancellationToken: cancellationToken));
    }

    /// <summary>Takes somebody's access to one engagement away.</summary>
    /// <param name="engagementId">Which engagement.</param>
    /// <param name="upn">Their sign-in name.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>How many grants were removed, which is zero or one.</returns>
    public async Task<int> RevokeAsync(Guid engagementId, string upn, CancellationToken cancellationToken)
    {
        await using var connection = Connect();

        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM ops.EngagementAccess WHERE EngagementId = @engagementId AND UserId = @userId;",
            new { engagementId, userId = Normalise(upn) },
            cancellationToken: cancellationToken));
    }
}
