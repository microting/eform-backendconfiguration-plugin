using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;

namespace BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;

/// <summary>
/// Resolves the effective set of recipient SDK site ids for a calendar event
/// (AreaRulePlanning): the union of the event's explicit PlanningSites and the
/// live members of every worker tag assigned to the event. Worker-tag
/// membership is read from the SDK <c>SiteTags</c> table, so the set is always
/// current (members added/removed from a tag immediately change the result).
/// </summary>
public class CalendarAssignmentResolver(
    BackendConfigurationPnDbContext backendConfigurationPnDbContext,
    IEFormCoreService coreHelper) : ICalendarAssignmentResolver
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

        // 3. Live members of those tags, read from the SDK SiteTags table.
        if (tagIds.Count > 0)
        {
            var sdkCore = await coreHelper.GetCore().ConfigureAwait(false);
            await using var sdkDbContext = sdkCore.DbContextHelper.GetDbContext();

            var memberSiteIds = await sdkDbContext.SiteTags
                .Where(x => x.TagId != null && tagIds.Contains(x.TagId.Value)
                            && x.SiteId != null
                            && x.WorkflowState != Constants.WorkflowStates.Removed)
                .Select(x => x.SiteId.Value)
                .ToListAsync(ct).ConfigureAwait(false);

            foreach (var siteId in memberSiteIds)
            {
                result.Add(siteId);
            }
        }

        return result;
    }
}
