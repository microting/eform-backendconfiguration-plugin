using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BackendConfiguration.Pn.Services.WorkerTagMembership;

/// <summary>
/// The single owner of the "is this site a live member of this worker tag (team)?"
/// rule. See <see cref="WorkerTagMembershipService"/> for the rule itself and why each
/// of its clauses exists — do not re-implement any part of it at a call site.
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
}
