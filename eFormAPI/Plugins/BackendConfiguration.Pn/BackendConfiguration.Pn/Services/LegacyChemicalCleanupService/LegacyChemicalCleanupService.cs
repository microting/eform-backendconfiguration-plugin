/*
The MIT License (MIT)
Copyright (c) 2007 - 2026 Microting A/S
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:
The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.
THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

namespace BackendConfiguration.Pn.Services.LegacyChemicalCleanupService;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Infrastructure.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data;

/// <param name="AreaProperties">Area assignments removed.</param>
/// <param name="Cases">Deployed cases deleted.</param>
/// <param name="EntityGroups">Entity lists deleted.</param>
/// <param name="Failures">Items whose delete threw, as "areaProperty:&lt;id&gt;", "areaRules:&lt;propertyId&gt;/&lt;areaId&gt;",
/// "areaRulePlanning:&lt;id&gt;", "case:&lt;uid&gt;" or "entityGroup:&lt;uid&gt;".</param>
public sealed record LegacyChemicalCleanupResult(
    int AreaProperties,
    int Cases,
    int EntityGroups,
    IReadOnlyList<string> Failures);

/// <summary>
/// One-off removal of what the legacy eForm chemical flow left on a customer
/// site (flutter-chemistry spec §12): every assignment of the Type9 area
/// "25. KemiKontrol" with its rules, plannings and SDK folders (25.01 and the
/// 25.02–25.07 expiry folders), the cases deployed into those folders, and the
/// entity lists "Chemicals - Barcode", "Chemicals - RegNo" and
/// "Chemicals - Areas - &lt;property&gt;". Works by area type and entity-group
/// name, so it does not need the Property columns the ChemicalInventory
/// migration dropped. Idempotent. The marker (as in AreaRulePlanningTagPurgeService)
/// records the first completed pass; after it, a start re-runs the pass only
/// while Type9 assignments, rules or rule plannings are still live, so items a
/// pass could not remove are retried on the next start.
/// Items are deleted one by one in isolation; failures are logged in one warning.
/// </summary>
public class LegacyChemicalCleanupService(
    BackendConfigurationPnDbContext dbContext,
    ItemsPlanningPnDbContext itemsPlanningPnDbContext,
    IEFormCoreService coreHelper,
    ILegacyChemicalSdkOperations sdkOperations,
    ILogger<LegacyChemicalCleanupService> logger)
{
    /// <summary>Same key convention as AreaRulePlanningTagPurgeService.BacklogPurgeMarkerName.</summary>
    public const string MarkerName = "BackendConfigurationBaseSettings:LegacyChemicalFlowRemoved";

    internal const string PropertyEntityGroupPrefix = "Chemicals - Areas - ";

    internal static readonly string[] GlobalEntityGroupNames = ["Chemicals - Barcode", "Chemicals - RegNo"];

    private const int SystemUserId = 0;

    private const AreaTypesEnum LegacyArea = BackendConfigurationPropertyAreasServiceHelper.LegacyChemicalAreaType;

    public async Task RunIfNeededAsync()
    {
        var markerWritten = await dbContext.PluginConfigurationValues.AnyAsync(x => x.Name == MarkerName)
            .ConfigureAwait(false);
        if (markerWritten && !await HasLegacyLeftoversAsync().ConfigureAwait(false))
        {
            return;
        }

        var result = await CleanupAsync().ConfigureAwait(false);

        // Conditional insert for the reason given in AreaRulePlanningTagPurgeService:
        // two pods starting together must not write two markers.
        await dbContext.Database.ExecuteSqlRawAsync(
            @"INSERT INTO `PluginConfigurationValues`
                  (`Name`, `Value`, `CreatedAt`, `UpdatedAt`, `Version`,
                   `WorkflowState`, `CreatedByUserId`, `UpdatedByUserId`)
              SELECT {0}, 'true', {1}, {1}, 1, {2}, 1, 0 FROM DUAL
              WHERE NOT EXISTS (
                  SELECT 1 FROM `PluginConfigurationValues` `existing`
                  WHERE `existing`.`Name` = {0})",
            MarkerName,
            DateTime.UtcNow,
            Constants.WorkflowStates.Created).ConfigureAwait(false);

        logger.LogInformation(
            "LegacyChemicalCleanup: removed {AreaProperties} area assignments, {Cases} cases, {EntityGroups} entity lists",
            result.AreaProperties, result.Cases, result.EntityGroups);

        // The marker is written even when items failed. Live Type9 leftovers
        // re-trigger the pass on the next start (see HasLegacyLeftoversAsync).
        if (result.Failures.Count > 0)
        {
            logger.LogWarning(
                "LegacyChemicalCleanup: {FailureCount} items could not be removed and are retried on the next start: {Failures}",
                result.Failures.Count, string.Join(", ", result.Failures));
        }
    }

    /// <summary>
    /// Whether any Type9 assignment, area rule or area rule planning is still live,
    /// e.g. left half-deleted by an earlier pass.
    /// </summary>
    private async Task<bool> HasLegacyLeftoversAsync() =>
        await dbContext.AreaProperties.AnyAsync(x =>
            x.WorkflowState != Constants.WorkflowStates.Removed && x.Area.Type == LegacyArea).ConfigureAwait(false)
        || await dbContext.AreaRules.AnyAsync(x =>
            x.WorkflowState != Constants.WorkflowStates.Removed && x.Area.Type == LegacyArea).ConfigureAwait(false)
        || await dbContext.AreaRulePlannings.AnyAsync(x =>
            x.WorkflowState != Constants.WorkflowStates.Removed && x.AreaRule.Area.Type == LegacyArea).ConfigureAwait(false);

    /// <summary>
    /// One pass over the legacy data. Every item is deleted in isolation: a
    /// failing delete is logged and listed in <see cref="LegacyChemicalCleanupResult.Failures"/>,
    /// and the pass carries on with the rest.
    /// </summary>
    public async Task<LegacyChemicalCleanupResult> CleanupAsync()
    {
        var core = await coreHelper.GetCore().ConfigureAwait(false);
        var sdkDbContext = core.DbContextHelper.GetDbContext();
        var failures = new List<string>();

        var assignments = await dbContext.AreaProperties
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed && x.Area.Type == LegacyArea)
            .OrderBy(x => x.Id)
            .ToListAsync().ConfigureAwait(false);

        // Cases deployed straight into the legacy folders (the 25.02–25.07 expiry
        // folders) are not reachable through plannings, so they go first. Planned
        // cases are left to the rule-planning delete, so none is deleted twice.
        var plannedUids = await PlannedCaseUidsAsync(sdkDbContext).ConfigureAwait(false);
        var deployedUids = (await CollectDeployedCaseUidsAsync(sdkDbContext, assignments.Select(x => x.Id).ToList())
                .ConfigureAwait(false))
            .Where(uid => !plannedUids.Contains(uid))
            .ToList();
        var cases = await DeleteEachAsync(deployedUids, "case", uid => uid, sdkOperations.DeleteCaseAsync, failures)
            .ConfigureAwait(false);

        var areaProperties = await DeleteEachAsync(assignments, "areaProperty", assignment => assignment.Id,
            assignment => BackendConfigurationPropertyAreasServiceHelper.DeleteAreaPropertyAsync(
                assignment, core, dbContext, itemsPlanningPnDbContext, SystemUserId, sdkOperations.DeleteCaseAsync),
            failures).ConfigureAwait(false);

        // Rules whose assignment is already gone, and rule plannings whose rule is,
        // e.g. left by an earlier interrupted pass. Pairs with an assignment in this
        // pass were handled (or failed) above.
        var assignedPairs = assignments.Select(x => (x.PropertyId, x.AreaId)).ToHashSet();
        var orphanRulePairs = (await dbContext.AreaRules
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed && x.Area.Type == LegacyArea)
                .Select(x => new { x.PropertyId, x.AreaId })
                .Distinct()
                .ToListAsync().ConfigureAwait(false))
            .Select(x => (x.PropertyId, x.AreaId))
            .Where(pair => !assignedPairs.Contains(pair))
            .ToList();
        await DeleteEachAsync(orphanRulePairs, "areaRules", pair => $"{pair.PropertyId}/{pair.AreaId}",
            pair => BackendConfigurationPropertyAreasServiceHelper.DeleteAreaRulesAsync(pair.PropertyId, pair.AreaId,
                core, dbContext, itemsPlanningPnDbContext, SystemUserId, sdkOperations.DeleteCaseAsync),
            failures).ConfigureAwait(false);

        var orphanRulePlannings = await dbContext.AreaRulePlannings
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed
                        && x.AreaRule.WorkflowState == Constants.WorkflowStates.Removed
                        && x.AreaRule.Area.Type == LegacyArea)
            .Include(x => x.PlanningSites)
            .ToListAsync().ConfigureAwait(false);
        await DeleteEachAsync(orphanRulePlannings, "areaRulePlanning", x => x.Id,
            x => BackendConfigurationPropertyAreasServiceHelper.DeleteAreaRulePlanningAsync(x, sdkDbContext, dbContext,
                itemsPlanningPnDbContext, SystemUserId, sdkOperations.DeleteCaseAsync),
            failures).ConfigureAwait(false);

        var entityGroupUids = await sdkDbContext.EntityGroups
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed
                        && x.MicrotingUid != null
                        && (GlobalEntityGroupNames.Contains(x.Name) || x.Name.StartsWith(PropertyEntityGroupPrefix)))
            .Select(x => x.MicrotingUid)
            .ToListAsync().ConfigureAwait(false);
        var entityGroups = await DeleteEachAsync(entityGroupUids, "entityGroup", uid => uid,
            sdkOperations.DeleteEntityGroupAsync, failures).ConfigureAwait(false);

        return new LegacyChemicalCleanupResult(areaProperties, cases, entityGroups, failures);
    }

    /// <summary>
    /// MicrotingUids of the cases and check-list sites deployed into the SDK
    /// folders of the given area assignments.
    /// </summary>
    private async Task<List<int>> CollectDeployedCaseUidsAsync(
        MicrotingDbContext sdkDbContext, List<int> assignmentIds)
    {
        var folderIds = await dbContext.ProperyAreaFolders
            .Where(x => assignmentIds.Contains(x.ProperyAreaAsignmentId))
            .Select(x => x.FolderId)
            .Distinct()
            .ToListAsync().ConfigureAwait(false);

        var caseUids = await sdkDbContext.Cases
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed
                        && x.FolderId != null && folderIds.Contains(x.FolderId.Value)
                        && x.MicrotingUid != null)
            .Select(x => x.MicrotingUid!.Value)
            .ToListAsync().ConfigureAwait(false);
        var checkListSiteUids = await sdkDbContext.CheckListSites
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed
                        && x.FolderId != null && folderIds.Contains(x.FolderId.Value))
            .Select(x => x.MicrotingUid)
            .ToListAsync().ConfigureAwait(false);

        return caseUids.Concat(checkListSiteUids).Distinct().ToList();
    }

    /// <summary>
    /// MicrotingUids that the rule-planning delete will delete itself: those of
    /// the live PlanningCaseSites of live plannings behind live Type9 rule
    /// plannings, resolved as BackendConfigurationPropertyAreasServiceHelper does.
    /// </summary>
    private async Task<HashSet<int>> PlannedCaseUidsAsync(MicrotingDbContext sdkDbContext)
    {
        var planningIds = await dbContext.AreaRulePlannings
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed
                        && x.ItemPlanningId != 0
                        && x.AreaRule.Area.Type == LegacyArea)
            .Select(x => x.ItemPlanningId)
            .ToListAsync().ConfigureAwait(false);

        var caseSites = await itemsPlanningPnDbContext.PlanningCaseSites
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed && planningIds.Contains(x.PlanningId))
            .Join(itemsPlanningPnDbContext.Plannings.Where(p => p.WorkflowState != Constants.WorkflowStates.Removed),
                caseSite => caseSite.PlanningId, planning => planning.Id, (caseSite, _) => caseSite)
            .AsNoTracking()
            .ToListAsync().ConfigureAwait(false);

        var uids = new HashSet<int>();
        foreach (var caseSite in caseSites)
        {
            var uid = await BackendConfigurationPropertyAreasServiceHelper
                .ResolvePlannedCaseUidAsync(caseSite, sdkDbContext).ConfigureAwait(false);
            if (uid != null)
            {
                uids.Add(uid.Value);
            }
        }

        return uids;
    }

    /// <summary>
    /// Runs <paramref name="delete"/> for every item, logging and recording a
    /// failure as "&lt;kind&gt;:&lt;id&gt;" instead of stopping. Returns how many succeeded.
    /// </summary>
    private async Task<int> DeleteEachAsync<T>(IEnumerable<T> items, string kind, Func<T, object> idOf,
        Func<T, Task> delete, List<string> failures)
    {
        var deleted = 0;
        foreach (var item in items)
        {
            try
            {
                await delete(item).ConfigureAwait(false);
                deleted++;
            }
            catch (Exception e)
            {
                logger.LogError(e, "LegacyChemicalCleanup: could not delete {Kind} {Id}", kind, idOf(item));
                failures.Add($"{kind}:{idOf(item)}");
            }
        }

        return deleted;
    }
}
