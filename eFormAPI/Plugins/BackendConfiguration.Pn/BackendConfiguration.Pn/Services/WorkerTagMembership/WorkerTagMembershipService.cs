using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;

namespace BackendConfiguration.Pn.Services.WorkerTagMembership;

/// <summary>
/// The ONE definition of live worker-tag ("team") membership.
///
/// <para>
/// This class is an EXTRACTION, not a redefinition. The predicate below is adopted
/// clause for clause from <c>BackendConfigurationWorkerTagsService.GetWorkerTags</c> —
/// the clause SET is identical, though the text is not literally the same: the correlated
/// <c>st.TagId == t.Id</c> became <c>st.TagId != null</c> plus a call-site filter, and
/// <c>AsNoTracking()</c> was added. That service's copy was in turn
/// clause-for-clause identical to the copy inlined in
/// <c>BackendConfigurationCalendarService.GetTasksForWeek</c>. The third copy,
/// <c>CalendarAssignmentReconciliation.CalendarAssignmentResolver</c>, was missing the
/// <c>Site.WorkflowState</c> clause that the other two both had — and that gap was
/// already written down as knowingly deferred, in the old
/// <c>BackendConfigurationWorkerTagsService</c> remarks ("That is tracked separately and
/// is not fixed here"), rather than being an unnoticed divergence. So: two copies agreed,
/// one was a clause short — a gap that was known and deferred, not undiscovered. Note
/// what was decided: the fix was deferred, not the omission chosen. Collapsing them to
/// one closes that
/// known gap, and its ongoing value is that the three consumers can no longer be edited
/// apart in FUTURE — not that it repairs a past drift.
/// </para>
///
/// <para>
/// <b>The rule.</b> An SDK <c>SiteTags</c> row counts as live membership when all of:
/// <list type="number">
/// <item><c>TagId</c> and <c>SiteId</c> are both set — both columns are nullable in the
/// SDK schema, and a row missing either end joins nothing.</item>
/// <item>the <c>SiteTag</c> row itself is not <c>Removed</c> — removing a worker from a
/// team soft-deletes this row and nothing else.</item>
/// <item>the <c>Site</c> exists and is not <c>Removed</c>.</item>
/// <item>the site has no <c>SiteWorker</c> pointing at a resigned <c>Worker</c>.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why clause 3 is load-bearing, not defensive.</b> <c>Core.SiteDelete</c>
/// soft-removes the <c>Site</c> and its <c>Worker</c> (<c>PnBase.Delete</c> goes through
/// <c>UpdateInternal</c>: it sets <c>WorkflowState</c> to <c>Removed</c>, increments
/// <c>Version</c>, stamps <c>UpdatedAt</c> and inserts a <c>*Version</c> audit row — the
/// point being that the row survives) but leaves the <c>SiteTags</c> rows behind — nothing in
/// the SDK ever deletes one. Without this clause a deleted device user keeps their team
/// alive forever, and the deploy path keeps handing a removed site to the deployer.
/// Note the deleted worker is only <c>WorkflowState</c>-removed and NOT
/// <c>Resigned</c>, so clause 4 does NOT cover this case: the two clauses are
/// independent and neither implies the other. <c>CalendarAssignmentResolver</c> was
/// missing clause 3 before this extraction; adopting the shared rule is what fixes it,
/// and it is the only change to PRODUCTION behaviour in this extraction. It is not the
/// only change overall: the calendar filter's membership dependency also went from
/// optional to required (see the <c>effectiveWorkerTagIds</c> block in
/// <c>BackendConfigurationCalendarService</c>), which is visible to test fixtures and
/// not to a running system.
/// </para>
///
/// <para>
/// <b>Why clause 4 is the negated form.</b> "The site has no resigned worker" and "the
/// site has at least one live worker" differ only for a site carrying several
/// <c>SiteWorker</c> rows. Rewriting it into the positive form
/// (<c>Any(sw =&gt; !sw.Worker.Resigned)</c>) was raised and dropped IN THE CHANGE THAT
/// CREATED THIS CLASS — this is not an older decision and it is recorded nowhere else,
/// so do not go looking for it. It was dropped on the product owner's statement that
/// <c>Site</c> is 1:1 with <c>Worker</c> in this product, which makes the two forms
/// equivalent on real data and leaves the negated form — the one that has actually
/// shipped — with nothing to gain from a rewrite. The technical reasoning that made
/// dropping it the safe call stands: the forms differ only on shapes nobody has reasoned
/// about (most obviously a site with zero <c>SiteWorker</c> rows, which the negated form
/// includes and the positive form would silently drop), and dropping recipients is the
/// unsafe direction. So do not "fix" this into the positive form without a product
/// decision. Note also there is deliberately no
/// <c>SiteWorker.WorkflowState</c> filter: adding one under the negated form would make
/// a soft-deleted link to a resigned worker stop counting, i.e. would show MORE.
/// </para>
///
/// <para>
/// Each method here opens its own SDK <c>MicrotingDbContext</c> and translates to a
/// single SQL statement; none of them evaluates the predicate client-side or loops per
/// id. That is a statement about THIS class, not about a request end to end:
/// <c>BackendConfigurationWorkerTagsService.GetWorkerTags</c> now opens a context for its
/// own <c>Tags</c> query and triggers a second one in here, so that endpoint costs two
/// round trips where it previously did one correlated <c>EXISTS</c>. That is an accepted
/// cost of having a single owner for the rule, written down so it is not mistaken for an
/// oversight.
/// </para>
///
/// <para>
/// Keeping that loop out of the callers is also why
/// <see cref="GetLiveMemberSiteIdsByTagAsync"/> exists beside
/// <see cref="GetLiveMemberSiteIdsAsync"/>: a consumer that needs to know WHICH tag a
/// site came from would otherwise have to call the flat lookup once per tag, moving the
/// loop out of here and into the caller. Two callers were doing exactly that before the
/// by-tag overload existed — <c>BackendConfigurationTaskTrackerHelper</c>'s Workers
/// column and the compliance report's <c>ResolveWorkerSiteIdsByArpId</c>. Three further
/// call sites added in #1236 — <c>BackendConfigurationCalendarService</c>'s
/// <c>GetTasksForWeek</c>, <c>Index</c> and <c>GetTaskTrackerList</c> — would have
/// needed the same loop had this overload not existed; they never wrote one.
/// </para>
/// </summary>
public class WorkerTagMembershipService(IEFormCoreService coreHelper) : IWorkerTagMembershipService
{
    /// <summary>
    /// THE predicate. It exists exactly once in the codebase, on purpose. Every clause
    /// is explained in the class remarks above — none of them is redundant, and clause 3
    /// in particular looks droppable and is not.
    /// </summary>
    private static IQueryable<SiteTag> LiveMemberships(MicrotingDbContext sdkDbContext) =>
        sdkDbContext.SiteTags
            .AsNoTracking()
            .Where(st => st.TagId != null
                         && st.SiteId != null
                         && st.WorkflowState != Constants.WorkflowStates.Removed
                         && sdkDbContext.Sites.Any(s => s.Id == st.SiteId
                                                        && s.WorkflowState != Constants.WorkflowStates.Removed)
                         && !sdkDbContext.SiteWorkers.Any(sw => sw.SiteId == st.SiteId
                                                                && sw.Worker.Resigned));

    public async Task<HashSet<int>> GetLiveMemberSiteIdsAsync(
        IReadOnlyCollection<int> tagIds, CancellationToken ct = default)
    {
        if (tagIds == null || tagIds.Count == 0)
        {
            return [];
        }

        // Materialised to a List so EF gets a plain parameterised IN (...) rather than
        // having to translate the caller's collection type.
        var tagIdList = tagIds.Distinct().ToList();

        var core = await coreHelper.GetCore().ConfigureAwait(false);
        await using var sdkDbContext = core.DbContextHelper.GetDbContext();

        var siteIds = await LiveMemberships(sdkDbContext)
            .Where(st => tagIdList.Contains(st.TagId.Value))
            .Select(st => st.SiteId.Value)
            .Distinct()
            .ToListAsync(ct).ConfigureAwait(false);

        return [..siteIds];
    }

    /// <summary>
    /// The batched, attribution-preserving forward lookup. ONE statement for the whole
    /// tag set: the <c>(TagId, SiteId)</c> pairs are projected off the same
    /// <see cref="LiveMemberships"/> query — the predicate is not restated, and the
    /// grouping happens in memory over the rows that query already returns, not over the
    /// table.
    /// </summary>
    /// <remarks>
    /// Every requested id is seeded into the dictionary first, so a tag with no live
    /// members comes back with an empty set rather than missing. That is the contract on
    /// <see cref="IWorkerTagMembershipService.GetLiveMemberSiteIdsByTagAsync"/>; it also
    /// means the pair loop can index rather than test-and-add.
    /// </remarks>
    public async Task<Dictionary<int, HashSet<int>>> GetLiveMemberSiteIdsByTagAsync(
        IReadOnlyCollection<int> tagIds, CancellationToken ct = default)
    {
        if (tagIds == null || tagIds.Count == 0)
        {
            return new Dictionary<int, HashSet<int>>();
        }

        var tagIdList = tagIds.Distinct().ToList();
        var byTagId = tagIdList.ToDictionary(id => id, _ => new HashSet<int>());

        var core = await coreHelper.GetCore().ConfigureAwait(false);
        await using var sdkDbContext = core.DbContextHelper.GetDbContext();

        var pairs = await LiveMemberships(sdkDbContext)
            .Where(st => tagIdList.Contains(st.TagId.Value))
            .Select(st => new { TagId = st.TagId.Value, SiteId = st.SiteId.Value })
            .Distinct()
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var pair in pairs)
        {
            byTagId[pair.TagId].Add(pair.SiteId);
        }

        return byTagId;
    }

    public async Task<HashSet<int>> GetTagIdsForSitesAsync(
        IReadOnlyCollection<int> siteIds, CancellationToken ct = default)
    {
        if (siteIds == null || siteIds.Count == 0)
        {
            return [];
        }

        var siteIdList = siteIds.Distinct().ToList();

        var core = await coreHelper.GetCore().ConfigureAwait(false);
        await using var sdkDbContext = core.DbContextHelper.GetDbContext();

        var tagIds = await LiveMemberships(sdkDbContext)
            .Where(st => siteIdList.Contains(st.SiteId.Value))
            .Select(st => st.TagId.Value)
            .Distinct()
            .ToListAsync(ct).ConfigureAwait(false);

        return [..tagIds];
    }

    public async Task<HashSet<int>> GetTagIdsWithLiveMembersAsync(CancellationToken ct = default)
    {
        var core = await coreHelper.GetCore().ConfigureAwait(false);
        await using var sdkDbContext = core.DbContextHelper.GetDbContext();

        var tagIds = await LiveMemberships(sdkDbContext)
            .Select(st => st.TagId.Value)
            .Distinct()
            .ToListAsync(ct).ConfigureAwait(false);

        return [..tagIds];
    }
}
