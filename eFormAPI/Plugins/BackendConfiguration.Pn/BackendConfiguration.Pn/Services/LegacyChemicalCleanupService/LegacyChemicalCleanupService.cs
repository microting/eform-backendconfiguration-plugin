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
using System.Linq;
using System.Threading.Tasks;
using Infrastructure.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data;

public sealed record LegacyChemicalCleanupResult(int AreaProperties, int Cases, int EntityGroups);

/// <summary>
/// One-off removal of what the legacy eForm chemical flow left on a customer
/// site (flutter-chemistry spec §12): every assignment of the Type9 area
/// "25. KemiKontrol" with its rules, plannings and SDK folders (25.01 and the
/// 25.02–25.07 expiry folders), the cases deployed into those folders, and the
/// entity lists "Chemicals - Barcode", "Chemicals - RegNo" and
/// "Chemicals - Areas - &lt;property&gt;". Works by area type and entity-group
/// name, so it does not need the Property columns the ChemicalInventory
/// migration dropped. Idempotent; marker-gated like AreaRulePlanningTagPurgeService.
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
    }

    public async Task<LegacyChemicalCleanupResult> CleanupAsync()
    {
        var core = await coreHelper.GetCore().ConfigureAwait(false);
        var sdkDbContext = core.DbContextHelper.GetDbContext();

        var assignments = await dbContext.AreaProperties
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed && x.Area.Type == AreaTypesEnum.Type9)
            .ToListAsync().ConfigureAwait(false);
        var assignmentIds = assignments.Select(x => x.Id).ToList();

        var folderIds = await dbContext.ProperyAreaFolders
            .Where(x => assignmentIds.Contains(x.ProperyAreaAsignmentId))
            .Select(x => x.FolderId)
            .Distinct()
            .ToListAsync().ConfigureAwait(false);

        // Cases deployed straight into the legacy folders (the 25.02–25.07 expiry
        // folders) are not reachable through plannings, so they go first.
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
        var deployedUids = caseUids.Concat(checkListSiteUids).Distinct().ToList();
        foreach (var uid in deployedUids)
        {
            await sdkOperations.DeleteCaseAsync(uid).ConfigureAwait(false);
        }

        foreach (var assignment in assignments)
        {
            await BackendConfigurationPropertyAreasServiceHelper.DeleteAreaPropertyAsync(
                assignment, core, dbContext, itemsPlanningPnDbContext, SystemUserId).ConfigureAwait(false);
        }

        var entityGroupUids = await sdkDbContext.EntityGroups
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed
                        && (GlobalEntityGroupNames.Contains(x.Name) || x.Name.StartsWith(PropertyEntityGroupPrefix)))
            .Select(x => x.MicrotingUid)
            .ToListAsync().ConfigureAwait(false);
        foreach (var uid in entityGroupUids)
        {
            await sdkOperations.DeleteEntityGroupAsync(uid).ConfigureAwait(false);
        }

        return new LegacyChemicalCleanupResult(assignments.Count, deployedUids.Count, entityGroupUids.Count);
    }
}
