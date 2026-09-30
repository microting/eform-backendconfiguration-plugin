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
/// <param name="Failures">Items whose delete threw, as "areaProperty:&lt;id&gt;", "case:&lt;uid&gt;" or "entityGroup:&lt;uid&gt;".</param>
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
/// migration dropped. Idempotent; marker-gated like AreaRulePlanningTagPurgeService.
/// Items are deleted one by one in isolation; the marker is written after every
/// completed pass, even one with failed items (those are logged in one warning).
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

    public async Task RunIfNeededAsync()
    {
        if (await dbContext.PluginConfigurationValues.AnyAsync(x => x.Name == MarkerName).ConfigureAwait(false))
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

        // The marker is written even when items failed: a deterministic failure
        // would otherwise re-run the whole cleanup (and report to Sentry) on
        // every boot. The failed ids are left for manual follow-up.
        if (result.Failures.Count > 0)
        {
            logger.LogWarning(
                "LegacyChemicalCleanup: {FailureCount} items could not be removed and will not be retried: {Failures}",
                result.Failures.Count, string.Join(", ", result.Failures));
        }
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

        var assignments = await dbContext.AreaProperties
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed && x.Area.Type == AreaTypesEnum.Type9)
            .OrderBy(x => x.Id)
            .ToListAsync().ConfigureAwait(false);

        // Cases deployed straight into the legacy folders (the 25.02–25.07 expiry
        // folders) are not reachable through plannings, so they go first.
        var deployedUids = await CollectDeployedCaseUidsAsync(sdkDbContext, assignments.Select(x => x.Id).ToList())
            .ConfigureAwait(false);
        var cases = await DeleteEachAsync(deployedUids, "case", uid => uid, sdkOperations.DeleteCaseAsync, failures)
            .ConfigureAwait(false);

        var areaProperties = await DeleteEachAsync(assignments, "areaProperty", assignment => assignment.Id,
            assignment => BackendConfigurationPropertyAreasServiceHelper.DeleteAreaPropertyAsync(
                assignment, core, dbContext, itemsPlanningPnDbContext, SystemUserId),
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
