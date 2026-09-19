using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;

namespace BackendConfiguration.Pn.Services.WorkerTagMembership;

/// <summary>
/// Plugin-db helpers that pair the property-scoped membership results of
/// <see cref="IWorkerTagMembershipService"/> with the events' team assignments
/// (<c>AreaRulePlanningWorkerTags</c>). They never decide membership themselves — the
/// tag sets they take are already resolved by the service — they only answer "which
/// events carry one of THESE teams on THAT property?" (#1256).
/// </summary>
public static class WorkerTagAssignmentQueries
{
    /// <summary>
    /// The ids of the non-removed events on property P that are assigned (non-removed
    /// <c>AreaRulePlanningWorkerTag</c>) to a team in <c>tagIdsByPropertyId[P]</c>. One
    /// statement; returns an empty list without querying when the map is empty.
    /// </summary>
    /// <remarks>
    /// Used by the multi-property worker filters (calendar task list, compliance report):
    /// feeding <see cref="IWorkerTagMembershipService.GetTagIdsForSitesByPropertyAsync"/>
    /// in here yields exactly the events a filtered worker would be deployed through a
    /// team, which a single flat tag list cannot express once a worker's teams differ per
    /// property. The (property, tag) pairing is done in memory because EF cannot push a
    /// list of pairs down as a parameter; the SQL side is still bounded by the two
    /// id lists.
    /// </remarks>
    public static async Task<List<int>> ArpIdsAssignedToTeamsOnPropertiesAsync(
        BackendConfigurationPnDbContext backendConfigurationPnDbContext,
        IReadOnlyDictionary<int, HashSet<int>> tagIdsByPropertyId,
        CancellationToken ct = default)
    {
        if (tagIdsByPropertyId == null || tagIdsByPropertyId.Count == 0)
        {
            return [];
        }

        var propertyIds = tagIdsByPropertyId.Keys.ToList();
        var tagIds = tagIdsByPropertyId.Values.SelectMany(x => x).Distinct().ToList();
        if (tagIds.Count == 0)
        {
            return [];
        }

        var rows = await backendConfigurationPnDbContext.AreaRulePlannings
            .AsNoTracking()
            .Where(arp => arp.WorkflowState != Constants.WorkflowStates.Removed
                          && propertyIds.Contains(arp.PropertyId))
            .SelectMany(arp => arp.AreaRulePlanningWorkerTags
                .Where(wt => wt.WorkflowState != Constants.WorkflowStates.Removed
                             && tagIds.Contains(wt.TagId))
                .Select(wt => new { ArpId = arp.Id, arp.PropertyId, wt.TagId }))
            .ToListAsync(ct).ConfigureAwait(false);

        return rows
            .Where(r => tagIdsByPropertyId.TryGetValue(r.PropertyId, out var allowed) && allowed.Contains(r.TagId))
            .Select(r => r.ArpId)
            .Distinct()
            .ToList();
    }
}
