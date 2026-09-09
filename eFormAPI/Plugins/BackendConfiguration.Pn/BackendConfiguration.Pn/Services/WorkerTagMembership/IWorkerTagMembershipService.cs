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
