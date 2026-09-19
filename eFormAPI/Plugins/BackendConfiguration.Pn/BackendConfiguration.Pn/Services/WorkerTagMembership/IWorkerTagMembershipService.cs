using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BackendConfiguration.Pn.Services.WorkerTagMembership;

/// <summary>
/// The single owner of the "is this site a live member of this worker tag (team)?"
/// rule. See <see cref="WorkerTagMembershipService"/> for the rule itself and why each
/// of its clauses exists — do not re-implement any part of it at a call site.
/// <para>
/// <b>Property scope (#1295 / #1256).</b> A team reaches only its members linked to the
/// event's property. Every production consumer that resolves a team FOR AN EVENT — deploy
/// (resolver, <c>EventDeployService</c>), display (week view, task list, task tracker,
/// compliance report) and the worker filters over them — uses the <c>…OnProperty…</c> /
/// <c>…ByProperty…</c> lookups. The installation-wide lookups
/// (<see cref="GetLiveMemberSiteIdsAsync"/>, <see cref="GetLiveMemberSiteIdsByTagAsync"/>,
/// <see cref="GetTagIdsForSitesAsync"/>) remain for callers with no event/property in
/// play; <see cref="GetTagIdsWithLiveMembersAsync"/> backs the unscoped teams list that
/// only supplies team NAMES.
/// </para>
/// </summary>
public interface IWorkerTagMembershipService
{
    /// <summary>
    /// Forward lookup: the SDK site ids that are live members of any of
    /// <paramref name="tagIds"/>. Returns an empty set for a null/empty input without
    /// querying.
    /// </summary>
    Task<HashSet<int>> GetLiveMemberSiteIdsAsync(
        IReadOnlyCollection<int> tagIds, CancellationToken ct = default);

    /// <summary>
    /// The same forward lookup as <see cref="GetLiveMemberSiteIdsAsync"/>, but keeping
    /// per-tag attribution instead of flattening: tag id → that tag's live member site
    /// ids. One database round trip for the whole set of tags.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists because the flat overload forces a caller that needs to know WHICH tag
    /// a site came from into a query per tag, and five call sites needed exactly that:
    /// <c>BackendConfigurationCalendarService</c>'s <c>GetTasksForWeek</c>, <c>Index</c>
    /// and <c>GetTaskTrackerList</c>, <c>BackendConfigurationTaskTrackerHelper</c>'s
    /// Workers column, and the compliance report's <c>ResolveWorkerSiteIdsByArpId</c>. On
    /// the week view — which <c>calendar-container.component.ts</c> reloads on every
    /// property selection and every week navigation — that would be one round trip per
    /// distinct worker tag in the response, issued sequentially, where before #1236 there
    /// were none.
    /// </para>
    /// <para>
    /// <b>Every requested tag id is a key of the result</b>, mapping to an empty set when
    /// it has no live members. Callers therefore never have to distinguish "absent" from
    /// "empty" — both mean the same thing and neither occurs for a requested id. Ids not
    /// asked for are never keys.
    /// </para>
    /// <para>
    /// Returns an empty dictionary for a null/empty input without querying — the same
    /// short-circuit as the other lookups, and what lets a caller with no team-assigned
    /// row avoid touching the SDK context at all.
    /// </para>
    /// </remarks>
    Task<Dictionary<int, HashSet<int>>> GetLiveMemberSiteIdsByTagAsync(
        IReadOnlyCollection<int> tagIds, CancellationToken ct = default);

    /// <summary>
    /// Reverse lookup: the worker tag ids that any of <paramref name="siteIds"/> is a
    /// live member of. Returns an empty set for a null/empty input without querying.
    /// </summary>
    Task<HashSet<int>> GetTagIdsForSitesAsync(
        IReadOnlyCollection<int> siteIds, CancellationToken ct = default);

    /// <summary>
    /// Every worker tag id that has at least one live member. This is the whole of the
    /// "is this SDK Tag a worker group rather than an eForm template tag?" test — the
    /// SDK has one <c>Tags</c> table and no discriminator column (#1213).
    /// </summary>
    Task<HashSet<int>> GetTagIdsWithLiveMembersAsync(CancellationToken ct = default);

    /// <summary>
    /// Property-scoped forward lookup (#1295, resolves #1256): the live members of any of
    /// <paramref name="tagIds"/> that are ALSO linked to <paramref name="propertyId"/>
    /// (an active <c>PropertyWorker</c> row). This is what a team deploys to: a team
    /// picked on a property never reaches a member who only works on another property.
    /// Returns an empty set for a null/empty input without querying.
    /// </summary>
    Task<HashSet<int>> GetLiveMemberSiteIdsOnPropertyAsync(
        IReadOnlyCollection<int> tagIds, int propertyId, CancellationToken ct = default);

    /// <summary>
    /// Every worker tag with at least one live member linked to
    /// <paramref name="propertyId"/>, mapped to exactly those property-linked live members.
    /// Tags with no such member are not keys. Feeds the property-scoped teams list and
    /// its per-team member ids (the task modal resolves member languages from them).
    /// </summary>
    Task<Dictionary<int, HashSet<int>>> GetLiveMemberSiteIdsByTagOnPropertyAsync(
        int propertyId, CancellationToken ct = default);

    /// <summary>
    /// Batched, property-scoped, attribution-preserving forward lookup (#1256): for each
    /// requested <c>(PropertyId, TagId)</c> pair, the live members of that tag that are
    /// linked to that property. This is the display twin of what
    /// <see cref="GetLiveMemberSiteIdsOnPropertyAsync"/> deploys to, for callers that
    /// render many events across one or more properties at once (the calendar week view
    /// and task list, the task tracker, the compliance report): each event's team is
    /// expanded against ITS OWN property, in a fixed number of round trips for the whole
    /// set — one plugin-db statement for the property links and at most one SDK statement
    /// for the memberships — never one per event, tag or property.
    /// <para>
    /// <b>Every requested pair is a key of the result</b>, mapping to an empty set when
    /// the tag has no live member on that property. Returns an empty dictionary for a
    /// null/empty input without querying.
    /// </para>
    /// </summary>
    Task<Dictionary<(int PropertyId, int TagId), HashSet<int>>> GetLiveMemberSiteIdsByPropertyAndTagAsync(
        IReadOnlyCollection<(int PropertyId, int TagId)> propertyTagPairs, CancellationToken ct = default);

    /// <summary>
    /// Property-scoped reverse lookup (#1256): the worker tag ids that any of
    /// <paramref name="siteIds"/> is a live member of, counting only the sites linked to
    /// <paramref name="propertyId"/>. Equivalently: the tags T for which some requested
    /// site is in <c>GetLiveMemberSiteIdsOnPropertyAsync([T], propertyId)</c>. Used where a
    /// worker filter or the deploy path asks "which of this property's teams is this site
    /// in?". Returns an empty set for a null/empty input without querying, and without an
    /// SDK round trip when none of the sites is linked to the property.
    /// </summary>
    Task<HashSet<int>> GetTagIdsForSitesOnPropertyAsync(
        IReadOnlyCollection<int> siteIds, int propertyId, CancellationToken ct = default);

    /// <summary>
    /// Multi-property reverse lookup (#1256): property id → the worker tag ids that any of
    /// <paramref name="siteIds"/> is a live member of WHILE linked to that property.
    /// For worker filters over lists that span properties (the task list, the task
    /// tracker, the compliance report): an event on property P assigned to team T matches
    /// a filtered site exactly when <c>T ∈ result[P]</c>. Properties none of the sites is
    /// linked to are not keys. Returns an empty dictionary for a null/empty input without
    /// querying.
    /// </summary>
    Task<Dictionary<int, HashSet<int>>> GetTagIdsForSitesByPropertyAsync(
        IReadOnlyCollection<int> siteIds, CancellationToken ct = default);
}
