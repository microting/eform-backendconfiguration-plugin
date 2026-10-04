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
using Microting.eForm.Infrastructure.Data.Entities;
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
/// records the first completed pass. After it, a start re-runs the pass only while
/// something it removes is still live (see HasLegacyLeftoversAsync: Type9
/// assignments, rules or rule plannings, open cases or check-list sites in Type9
/// folders, legacy entity lists), and at most <see cref="MaxPasses"/> passes in
/// all; then it logs one warning and leaves the rest for manual follow-up.
/// Items are deleted one by one in isolation; failures are logged in one warning per pass.
/// Completed case records (Status 100 or DoneAt set) are never deleted or changed (owner decision):
/// they stay in history and reports.
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

    /// <summary>
    /// How many passes have run (a tenant whose marker predates it counts as one);
    /// <see cref="MaxPasses"/> + 1 records that the cleanup gave up.
    /// </summary>
    public const string AttemptsName = MarkerName + ":Attempts";

    /// <summary>Passes in all, the first included, before leftovers are left for manual follow-up.</summary>
    internal const int MaxPasses = 5;

    internal const string PropertyEntityGroupPrefix = "Chemicals - Areas - ";

    internal static readonly string[] GlobalEntityGroupNames = ["Chemicals - Barcode", "Chemicals - RegNo"];

    private const int SystemUserId = 0;

    private const int CompletedStatus = 100;

    private const AreaTypesEnum LegacyArea = BackendConfigurationPropertyAreasServiceHelper.LegacyChemicalAreaType;

    public async Task RunIfNeededAsync()
    {
        var markerWritten = await dbContext.PluginConfigurationValues.AnyAsync(x => x.Name == MarkerName)
            .ConfigureAwait(false);
        if (markerWritten && !await HasLegacyLeftoversAsync().ConfigureAwait(false))
        {
            return;
        }

        var (passes, counted) = await ReadPassesAsync(markerWritten).ConfigureAwait(false);
        if (passes > MaxPasses)
        {
            return;
        }

        // Every pass is claimed atomically (compare-and-set on the Attempts row)
        // before it starts, so pods starting together run it once and the cap holds.
        if (passes == MaxPasses)
        {
            if (await ClaimPassAsync(passes, counted).ConfigureAwait(false))
            {
                logger.LogWarning(
                    "LegacyChemicalCleanup: giving up after {Passes} passes; the remaining legacy KemiKontrol items are left for manual follow-up",
                    passes);
            }

            return;
        }

        if (!await ClaimPassAsync(passes, counted).ConfigureAwait(false))
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

        // The marker is written even when items failed. Leftovers re-trigger the
        // pass on the next start (see HasLegacyLeftoversAsync), up to MaxPasses.
        if (result.Failures.Count > 0)
        {
            logger.LogWarning(
                "LegacyChemicalCleanup: {FailureCount} items could not be removed (pass {Pass} of at most {MaxPasses}): {Failures}",
                result.Failures.Count, passes + 1, MaxPasses, string.Join(", ", result.Failures));
        }
    }

    /// <summary>
    /// Passes run so far, and whether the Attempts row exists. Without the row,
    /// a written marker means one pass already ran (a tenant whose pass predates it).
    /// </summary>
    private async Task<(int Passes, bool Counted)> ReadPassesAsync(bool markerWritten)
    {
        var value = await dbContext.PluginConfigurationValues.AsNoTracking()
            .Where(x => x.Name == AttemptsName)
            .Select(x => x.Value)
            .FirstOrDefaultAsync().ConfigureAwait(false);
        if (value == null)
        {
            return (markerWritten ? 1 : 0, false);
        }

        return (int.TryParse(value, out var passes) ? passes : MaxPasses + 1, true);
    }

    /// <summary>
    /// Moves the Attempts row from <paramref name="passes"/> to passes + 1, atomically:
    /// a compare-and-set update, or a conditional insert when the row does not exist
    /// yet. False when another pod claimed it first.
    /// </summary>
    private async Task<bool> ClaimPassAsync(int passes, bool counted)
    {
        var expected = passes.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var next = (passes + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (counted)
        {
            return await dbContext.PluginConfigurationValues
                .Where(x => x.Name == AttemptsName && x.Value == expected)
                .ExecuteUpdateAsync(x => x
                    .SetProperty(v => v.Value, next)
                    .SetProperty(v => v.UpdatedAt, DateTime.UtcNow)).ConfigureAwait(false) == 1;
        }

        return await dbContext.Database.ExecuteSqlRawAsync(
            @"INSERT INTO `PluginConfigurationValues`
                  (`Name`, `Value`, `CreatedAt`, `UpdatedAt`, `Version`,
                   `WorkflowState`, `CreatedByUserId`, `UpdatedByUserId`)
              SELECT {0}, {1}, {2}, {2}, 1, {3}, 1, 0 FROM DUAL
              WHERE NOT EXISTS (
                  SELECT 1 FROM `PluginConfigurationValues` `existing`
                  WHERE `existing`.`Name` = {0})",
            AttemptsName,
            next,
            DateTime.UtcNow,
            Constants.WorkflowStates.Created).ConfigureAwait(false) == 1;
    }

    /// <summary>
    /// Whether anything the pass removes is still live: a Type9 assignment, area
    /// rule or area rule planning, a case or check-list site in a Type9
    /// assignment's folder, or a legacy entity list. A delete that failed leaves
    /// its row live (Core marks only what it deleted), so this is what retries it.
    /// </summary>
    private async Task<bool> HasLegacyLeftoversAsync()
    {
        if (await dbContext.AreaProperties.AnyAsync(x =>
                x.WorkflowState != Constants.WorkflowStates.Removed && x.Area.Type == LegacyArea).ConfigureAwait(false)
            || await dbContext.AreaRules.AnyAsync(x =>
                x.WorkflowState != Constants.WorkflowStates.Removed && x.Area.Type == LegacyArea).ConfigureAwait(false)
            || await dbContext.AreaRulePlannings.AnyAsync(x =>
                x.WorkflowState != Constants.WorkflowStates.Removed && x.AreaRule.Area.Type == LegacyArea).ConfigureAwait(false))
        {
            return true;
        }

        var sdkDbContext = (await coreHelper.GetCore().ConfigureAwait(false)).DbContextHelper.GetDbContext();
        return (await CollectDeployedCaseUidsAsync(sdkDbContext).ConfigureAwait(false)).Count > 0
               || await LegacyEntityGroups(sdkDbContext).AnyAsync().ConfigureAwait(false);
    }

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

        // Every case delete, folder pass and planned path alike, rechecks just before
        // the call that the uid's only Cases row is not a completed record (see
        // IsCompletedRecordAsync), so Core cannot soft-delete that record.
        Func<int, Task> deleteCase = async uid =>
        {
            if (await IsCompletedRecordAsync(sdkDbContext, uid).ConfigureAwait(false))
            {
                logger.LogInformation(
                    "LegacyChemicalCleanup: left uid {MicrotingUid} alone, its only case is a completed record", uid);
                return;
            }

            await sdkOperations.DeleteCaseAsync(uid).ConfigureAwait(false);
        };

        var assignments = await dbContext.AreaProperties
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed && x.Area.Type == LegacyArea)
            .OrderBy(x => x.Id)
            .ToListAsync().ConfigureAwait(false);

        // Cases deployed straight into the legacy folders (the 25.02–25.07 expiry
        // folders) are not reachable through plannings, so they go first. Folders
        // of already-removed assignments count too, so a failed delete is retried. Planned
        // cases are left to the rule-planning delete, so none is deleted twice.
        var plannedUids = await PlannedCaseUidsAsync(sdkDbContext).ConfigureAwait(false);
        var deployedUids = (await CollectDeployedCaseUidsAsync(sdkDbContext).ConfigureAwait(false))
            .Where(uid => !plannedUids.Contains(uid))
            .ToList();
        var cases = await DeleteEachAsync(deployedUids, "case", uid => uid, deleteCase, failures)
            .ConfigureAwait(false);

        var areaProperties = await DeleteEachAsync(assignments, "areaProperty", assignment => assignment.Id,
            assignment => BackendConfigurationPropertyAreasServiceHelper.DeleteAreaPropertyAsync(
                assignment, core, dbContext, itemsPlanningPnDbContext, SystemUserId, deleteCase),
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
                core, dbContext, itemsPlanningPnDbContext, SystemUserId, deleteCase),
            failures).ConfigureAwait(false);

        var orphanRulePlannings = await dbContext.AreaRulePlannings
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed
                        && x.AreaRule.WorkflowState == Constants.WorkflowStates.Removed
                        && x.AreaRule.Area.Type == LegacyArea)
            .Include(x => x.PlanningSites)
            .ToListAsync().ConfigureAwait(false);
        await DeleteEachAsync(orphanRulePlannings, "areaRulePlanning", x => x.Id,
            x => BackendConfigurationPropertyAreasServiceHelper.DeleteAreaRulePlanningAsync(x, sdkDbContext, dbContext,
                itemsPlanningPnDbContext, SystemUserId, deleteCase),
            failures).ConfigureAwait(false);

        var entityGroupUids = await LegacyEntityGroups(sdkDbContext)
            .Select(x => x.MicrotingUid)
            .ToListAsync().ConfigureAwait(false);
        var entityGroups = await DeleteEachAsync(entityGroupUids, "entityGroup", uid => uid,
            sdkOperations.DeleteEntityGroupAsync, failures).ConfigureAwait(false);

        return new LegacyChemicalCleanupResult(areaProperties, cases, entityGroups, failures);
    }

    /// <summary>
    /// Distinct MicrotingUids of the open (not removed, retracted or completed)
    /// cases and live check-list sites deployed into the SDK folders of any Type9
    /// assignment, live or removed.
    /// </summary>
    private async Task<List<int>> CollectDeployedCaseUidsAsync(MicrotingDbContext sdkDbContext)
    {
        var folderIds = await dbContext.ProperyAreaFolders
            .Join(dbContext.AreaProperties.Where(x => x.Area.Type == LegacyArea),
                folder => folder.ProperyAreaAsignmentId, assignment => assignment.Id, (folder, _) => folder.FolderId)
            .Distinct()
            .ToListAsync().ConfigureAwait(false);

        // Retracted rows were already deleted from the device, as
        // BackendConfigurationPropertyAreasServiceHelper.ResolvePlannedCaseUidAsync treats them.
        // Completed rows (Status 100, or DoneAt set) are records, not deployments: a reversed
        // CheckListSite gets one per completion, all with its uid, and Core.CaseDelete
        // marks a Cases row only on a single match, so they would never clear. The
        // deployment itself is the CheckListSite, collected below. Uids are distinct.
        var caseUids = await sdkDbContext.Cases
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed
                        && x.WorkflowState != Constants.WorkflowStates.Retracted
                        && x.Status != CompletedStatus && x.DoneAt == null
                        && x.FolderId != null && folderIds.Contains(x.FolderId.Value)
                        && x.MicrotingUid != null)
            .Select(x => x.MicrotingUid!.Value)
            .ToListAsync().ConfigureAwait(false);
        var checkListSiteUids = await sdkDbContext.CheckListSites
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed
                        && x.WorkflowState != Constants.WorkflowStates.Retracted
                        && x.FolderId != null && folderIds.Contains(x.FolderId.Value))
            .Select(x => x.MicrotingUid)
            .ToListAsync().ConfigureAwait(false);

        // Never a uid whose only Cases row is a completed record (see IsCompletedRecordAsync).
        var uids = caseUids.Concat(checkListSiteUids).Distinct().ToList();
        var completedRecordUids = await CompletedRecordUids(sdkDbContext, uids).ToListAsync().ConfigureAwait(false);
        return uids.Except(completedRecordUids).ToList();
    }

    /// <summary>
    /// Owner decision: the cleanup never soft-deletes or changes a completed legacy
    /// case record (Status 100). Core.CaseDelete(uid) marks the Cases row Removed
    /// when it is the only row with that uid, so such a uid is never deleted. With
    /// two or more rows Core marks none of them, so deleting stays safe.
    /// </summary>
    private static async Task<bool> IsCompletedRecordAsync(MicrotingDbContext sdkDbContext, int uid) =>
        await CompletedRecordUids(sdkDbContext, [uid]).AnyAsync().ConfigureAwait(false);

    /// <summary>
    /// Those of <paramref name="uids"/> with exactly one Cases row, and that row answered:
    /// <c>Status == 100 || DoneAt.HasValue</c>, the codebase's predicate for a completed
    /// case (CalendarOccurrenceRetractionService, invariant R2).
    /// </summary>
    internal static IQueryable<int> CompletedRecordUids(MicrotingDbContext sdkDbContext, IReadOnlyCollection<int> uids) =>
        sdkDbContext.Cases
            .Where(x => x.MicrotingUid != null && uids.Contains(x.MicrotingUid.Value))
            .GroupBy(x => x.MicrotingUid!.Value)
            .Where(g => g.Count() == 1 && g.Count(x => x.Status == CompletedStatus || x.DoneAt != null) == 1)
            .Select(g => g.Key);

    private IQueryable<EntityGroup> LegacyEntityGroups(MicrotingDbContext sdkDbContext) =>
        sdkDbContext.EntityGroups
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed
                        && x.MicrotingUid != null
                        && (GlobalEntityGroupNames.Contains(x.Name) || x.Name.StartsWith(PropertyEntityGroupPrefix)));

    /// <summary>
    /// MicrotingUids that the rule-planning delete will delete itself: those of
    /// the live PlanningCaseSites behind live Type9 rule plannings, resolved as
    /// BackendConfigurationPropertyAreasServiceHelper does.
    /// </summary>
    private async Task<HashSet<int>> PlannedCaseUidsAsync(MicrotingDbContext sdkDbContext)
    {
        var planningIds = await dbContext.AreaRulePlannings
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed
                        && x.ItemPlanningId != 0
                        && x.AreaRule.Area.Type == LegacyArea)
            .Select(x => x.ItemPlanningId)
            .ToListAsync().ConfigureAwait(false);

        var caseSites = await itemsPlanningPnDbContext.PlanningCaseSites.AsNoTracking()
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed && planningIds.Contains(x.PlanningId))
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
