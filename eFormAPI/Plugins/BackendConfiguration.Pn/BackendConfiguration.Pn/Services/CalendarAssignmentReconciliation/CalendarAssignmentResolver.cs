using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.WorkerTagMembership;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;

namespace BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;

/// <summary>
/// Resolves the effective set of recipient SDK site ids for a calendar event
/// (AreaRulePlanning): the union of the event's explicit PlanningSites and the
/// live members of every worker tag assigned to the event. Worker-tag
/// membership is read from the SDK <c>SiteTags</c> table, so the set is always
/// current (members added/removed from a tag immediately change the result).
///
/// <para>
/// Membership itself is NOT defined here. It comes from
/// <see cref="IWorkerTagMembershipService"/>, which owns the rule for this resolver,
/// for the teams dropdown (<c>BackendConfigurationWorkerTagsService</c>) and for the
/// calendar's assignee filter (<c>BackendConfigurationCalendarService.GetTasksForWeek</c>)
/// alike. The three used to spell the rule out separately: the dropdown and the filter
/// agreed clause for clause, while this resolver was one clause short (below). With one
/// copy, a future edit cannot make them disagree.
/// </para>
///
/// <para>
/// The gap this closes: this resolver had NO <c>Site.WorkflowState != Removed</c>
/// clause, while the other two did — a gap that was known and deferred, not undiscovered
/// (the old <c>BackendConfigurationWorkerTagsService</c> remarks named it and said "That
/// is tracked separately and is not fixed here"). <c>Core.SiteDelete</c> soft-removes the
/// Site but leaves its <c>SiteTags</c> rows behind, so a deleted device user was still
/// handed to the deploy path as a recipient — while being correctly absent from the teams list and
/// the calendar filter. Adopting the shared rule fixes that, and it is the only change
/// to PRODUCTION behaviour in the extraction: the resigned clause is clause for clause
/// the one that already shipped. It is not the only change overall — the calendar
/// filter's membership dependency also went from optional to required, which a running
/// system cannot observe but test fixtures can; the reasoning for that is written down at
/// the <c>effectiveWorkerTagIds</c> block in <c>BackendConfigurationCalendarService</c>.
/// </para>
///
/// <para>
/// <b>Property scope (#1295).</b> The tag-derived half is further restricted to members
/// linked to the event's property (<c>AreaRulePlanning.PropertyId</c> → active
/// <c>PropertyWorker</c>), via
/// <see cref="IWorkerTagMembershipService.GetLiveMemberSiteIdsOnPropertyAsync"/>.
/// </para>
///
/// <para>
/// Explicit <c>PlanningSites</c> assignment is deliberately NOT filtered by that rule:
/// naming a person on an event is a fact about the event and survives their
/// resignation. Only the tag-derived half is membership-gated.
/// </para>
/// </summary>
public class CalendarAssignmentResolver(
    BackendConfigurationPnDbContext backendConfigurationPnDbContext,
    IWorkerTagMembershipService workerTagMembershipService) : ICalendarAssignmentResolver
{
    public async Task<HashSet<int>> ResolveEffectiveSiteIdsAsync(
        int areaRulePlanningId, CancellationToken ct = default)
    {
        // 1. Explicit assignees: PlanningSite rows for this event (not removed).
        var explicitSiteIds = await backendConfigurationPnDbContext.PlanningSites
            .Where(x => x.AreaRulePlanningsId == areaRulePlanningId
                        && x.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(x => x.SiteId)
            .ToListAsync(ct).ConfigureAwait(false);

        var result = new HashSet<int>(explicitSiteIds);

        // 2. Assigned worker tag ids (not removed).
        var tagIds = await backendConfigurationPnDbContext.AreaRulePlanningWorkerTags
            .Where(x => x.AreaRulePlanningId == areaRulePlanningId
                        && x.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(x => x.TagId)
            .ToListAsync(ct).ConfigureAwait(false);

        if (tagIds.Count == 0)
        {
            return result;
        }

        // 3. Live members of those tags that are linked to the EVENT'S PROPERTY — the
        //    shared membership rule plus its property clause (#1295, resolves #1256).
        //    A team is installation-wide in the SDK, so without the property clause a
        //    team with members on property A, picked on an event on property B,
        //    deployed cases to A's workers. The team stays a live link: membership
        //    changes still flow into future cases, only restricted to this property.
        //    The removed-state of the event is deliberately not filtered here — a
        //    reconcile of a removed event must still see the same property.
        var propertyId = await backendConfigurationPnDbContext.AreaRulePlannings
            .Where(x => x.Id == areaRulePlanningId)
            .Select(x => (int?)x.PropertyId)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        if (propertyId == null)
        {
            return result;
        }

        var memberSiteIds = await workerTagMembershipService
            .GetLiveMemberSiteIdsOnPropertyAsync(tagIds, propertyId.Value, ct).ConfigureAwait(false);

        result.UnionWith(memberSiteIds);

        return result;
    }
}
