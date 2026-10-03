using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using PlanningSite = Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite;

namespace BackendConfiguration.Pn.Services.DuplicatePlanningSiteRepair;

/// <summary>
/// #1385 — one-time cleanup of the duplicate items-planning <c>PlanningSites</c> rows the
/// task wizard's create path wrote (every site twice: A, B, A, B) until #1385. Duplicates
/// deploy and push twice per site in the items-planning service, and made removing a
/// worker from a property throw.
///
/// <para>Admin only, opt-in, nothing runs at startup. Same contract as the #1294 repair:
/// <c>GET dry-run</c> returns the plan and its hash; <c>POST run?planHash=</c> recomputes
/// the plan and refuses when the hash differs or there is nothing to remove. Per (planning,
/// site) the lowest id is kept and the others are soft-deleted. Every row is re-read right
/// before its write, and a row that changed is skipped, never overwritten. Every change is
/// logged.</para>
///
/// <para>No run marker: the repair is idempotent. A finished or interrupted run leaves a
/// smaller plan, so it is resumed by another dry run and run, and a second run over clean
/// data is refused as empty.</para>
///
/// <para>Scope: plannings of live AreaRulePlannings, i.e. the plannings this plugin owns.</para>
/// </summary>
public class DuplicatePlanningSiteRepairService(
    BackendConfigurationPnDbContext dbContext,
    ItemsPlanningPnDbContext itemsPlanningPnDbContext,
    ILogger<DuplicatePlanningSiteRepairService> logger) : IDuplicatePlanningSiteRepairService
{
    /// <summary>
    /// Test seam: runs right before each row is soft-deleted, with that row. Throwing from
    /// it is indistinguishable from a failing write.
    /// </summary>
    internal Func<PlanningSite, Task> OnBeforeRemove { get; set; } = _ => Task.CompletedTask;

    public async Task<OperationDataResult<DuplicatePlanningSiteRepairPlanModel>> DryRunAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var plan = await ComputeAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation(
                "DuplicatePlanningSiteRepair: dry run — {Groups} duplicated (planning, site) pairs, {Rows} rows to remove, plan {PlanHash}",
                plan.Groups.Count, plan.RowsToRemove, plan.PlanHash);
            return new OperationDataResult<DuplicatePlanningSiteRepairPlanModel>(true, plan);
        }
        catch (Exception e)
        {
            logger.LogError(e, "DuplicatePlanningSiteRepair: dry run failed");
            return new OperationDataResult<DuplicatePlanningSiteRepairPlanModel>(false,
                $"Duplicate PlanningSites dry run failed: {e.Message}");
        }
    }

    public async Task<OperationDataResult<DuplicatePlanningSiteRepairRunResultModel>> RunAsync(
        string planHash, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHash))
        {
            return Refused("planHash is required: run the dry run, review it, and pass its PlanHash.");
        }

        try
        {
            var plan = await ComputeAsync(cancellationToken).ConfigureAwait(false);
            if (!string.Equals(planHash, plan.PlanHash, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "DuplicatePlanningSiteRepair: refused — planHash {Given} does not match the current plan {Current}",
                    planHash, plan.PlanHash);
                return Refused("The data changed since the dry run: run the dry run again and review the new plan.");
            }

            if (plan.Groups.Count == 0)
            {
                return Refused("There are no duplicate PlanningSites rows to remove.");
            }

            var result = new DuplicatePlanningSiteRepairRunResultModel();
            foreach (var group in plan.Groups)
            {
                await ApplyGroupAsync(group, result).ConfigureAwait(false);
            }

            logger.LogInformation(
                "DuplicatePlanningSiteRepair: run done — {Removed} removed, {Skipped} skipped, {Failed} failed",
                result.Removed, result.Skipped.Count, result.Failures.Count);
            return new OperationDataResult<DuplicatePlanningSiteRepairRunResultModel>(true, result);
        }
        catch (Exception e)
        {
            logger.LogError(e, "DuplicatePlanningSiteRepair: run failed");
            return Refused($"Duplicate PlanningSites run failed: {e.Message}");
        }
    }

    private static OperationDataResult<DuplicatePlanningSiteRepairRunResultModel> Refused(string message)
        => new(false, message);

    private async Task<DuplicatePlanningSiteRepairPlanModel> ComputeAsync(CancellationToken ct)
    {
        var ownedPlanningIds = (await dbContext.AreaRulePlannings
                .AsNoTracking()
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Select(x => x.ItemPlanningId)
                .Distinct()
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .ToHashSet();

        // Grouped in the database, so only the duplicated keys come back. The owned
        // plannings live in another database, so that filter is applied here.
        var duplicatedPlanningIds = (await itemsPlanningPnDbContext.PlanningSites
                .AsNoTracking()
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .GroupBy(x => new { x.PlanningId, x.SiteId })
                .Where(g => g.Count() > 1)
                .Select(g => g.Key.PlanningId)
                .Distinct()
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .Where(ownedPlanningIds.Contains)
            .ToList();

        var liveRows = duplicatedPlanningIds.Count == 0
            ? []
            : await itemsPlanningPnDbContext.PlanningSites
                .AsNoTracking()
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed
                            && duplicatedPlanningIds.Contains(x.PlanningId))
                .Select(x => new { x.Id, x.PlanningId, x.SiteId })
                .ToListAsync(ct)
                .ConfigureAwait(false);

        var groups = liveRows
            .GroupBy(x => (x.PlanningId, x.SiteId))
            .Where(g => g.Count() > 1)
            .Select(g =>
            {
                var ids = g.Select(x => x.Id).OrderBy(id => id).ToList();
                return new DuplicatePlanningSiteGroupModel
                {
                    PlanningId = g.Key.PlanningId,
                    SiteId = g.Key.SiteId,
                    KeptPlanningSiteId = ids[0],
                    RemovedPlanningSiteIds = ids.Skip(1).ToList()
                };
            })
            .OrderBy(g => g.PlanningId)
            .ThenBy(g => g.SiteId)
            .ToList();

        return new DuplicatePlanningSiteRepairPlanModel
        {
            Groups = groups,
            RowsToRemove = groups.Sum(g => g.RemovedPlanningSiteIds.Count),
            PlanHash = HashOf(groups)
        };
    }

    private static string HashOf(IEnumerable<DuplicatePlanningSiteGroupModel> groups)
    {
        var sb = new StringBuilder();
        foreach (var g in groups)
        {
            sb.Append(g.PlanningId).Append(':').Append(g.SiteId).Append(':').Append(g.KeptPlanningSiteId)
                .Append(':').AppendJoin(',', g.RemovedPlanningSiteIds).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    /// <summary>
    /// Soft-deletes the group's extra rows, each re-read right before its write. The kept
    /// row is checked first, so a pair is never left without a live row.
    /// </summary>
    private async Task ApplyGroupAsync(
        DuplicatePlanningSiteGroupModel group, DuplicatePlanningSiteRepairRunResultModel result)
    {
        foreach (var rowId in group.RemovedPlanningSiteIds)
        {
            try
            {
                var keptIsLive = await itemsPlanningPnDbContext.PlanningSites
                    .AsNoTracking()
                    .AnyAsync(x => x.Id == group.KeptPlanningSiteId
                                   && x.PlanningId == group.PlanningId
                                   && x.SiteId == group.SiteId
                                   && x.WorkflowState != Constants.WorkflowStates.Removed)
                    .ConfigureAwait(false);
                var row = await itemsPlanningPnDbContext.PlanningSites
                    .FirstOrDefaultAsync(x => x.Id == rowId)
                    .ConfigureAwait(false);

                if (!keptIsLive
                    || row == null
                    || row.WorkflowState == Constants.WorkflowStates.Removed
                    || row.PlanningId != group.PlanningId
                    || row.SiteId != group.SiteId)
                {
                    var reason = !keptIsLive
                        ? $"kept row {group.KeptPlanningSiteId} is no longer live"
                        : row == null
                            ? "the row no longer exists"
                            : row.WorkflowState == Constants.WorkflowStates.Removed
                                ? "the row is already removed"
                                : "the row now belongs to another planning or site";
                    result.Skipped.Add($"PlanningSite {rowId} (planning {group.PlanningId}, site {group.SiteId}): {reason}");
                    logger.LogWarning(
                        "DuplicatePlanningSiteRepair: skipped PlanningSite {Id} (planning {PlanningId}, site {SiteId}): {Reason}",
                        rowId, group.PlanningId, group.SiteId, reason);
                    continue;
                }

                await OnBeforeRemove(row).ConfigureAwait(false);
                await row.Delete(itemsPlanningPnDbContext).ConfigureAwait(false);
                result.Removed++;
                logger.LogInformation(
                    "DuplicatePlanningSiteRepair: removed PlanningSite {Id} (planning {PlanningId}, site {SiteId}), kept {KeptId}",
                    rowId, group.PlanningId, group.SiteId, group.KeptPlanningSiteId);
            }
            catch (Exception e)
            {
                // A failed write leaves the rest of the run going; the row stays in the next plan.
                itemsPlanningPnDbContext.ChangeTracker.Clear();
                result.Failures.Add($"PlanningSite {rowId} (planning {group.PlanningId}, site {group.SiteId}): {e.Message}");
                logger.LogError(e,
                    "DuplicatePlanningSiteRepair: could not remove PlanningSite {Id} (planning {PlanningId}, site {SiteId})",
                    rowId, group.PlanningId, group.SiteId);
            }
        }
    }
}
