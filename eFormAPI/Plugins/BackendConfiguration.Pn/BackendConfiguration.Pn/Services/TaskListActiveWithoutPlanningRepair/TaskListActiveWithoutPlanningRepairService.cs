using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models.TaskList;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using LegacyCleanup = BackendConfiguration.Pn.Services.LegacyChemicalCleanupService.LegacyChemicalCleanupService;

namespace BackendConfiguration.Pn.Services.TaskListActiveWithoutPlanningRepair;

/// <summary>
/// #1376 — one-time cleanup of ACTIVE AreaRulePlannings without a live items-planning
/// Planning (ItemPlanningId 0, or a Planning that is removed or missing). The task list
/// no longer shows them; this removes them.
///
/// <para>Only a row whose AreaRule has no other live AreaRulePlanning is deleted.
/// Legacy area types (type 6, slurry tanks, pools) hang several plannings off one rule
/// and keep a planning-less row among them on purpose — the legacy area-rule editor
/// addresses them by position — so such a row is listed as skipped
/// (<see cref="ReasonLegacySibling"/>) and left alone.</para>
///
/// <para>Per row the run first retracts the open device deployments (cases, or
/// CheckListSites for deployments without one) of the row's removed Planning —
/// completed cases are kept — and only then soft-deletes the row through
/// <see cref="IBackendConfigurationTaskWizardService.DeleteTaskDeferredRetraction"/>,
/// the task-list delete. A row whose retraction fails is not deleted.</para>
///
/// <para>Opt-in and reviewed, like the #1294 repair: nothing runs at startup; the GET
/// dry run writes nothing and returns a plan hash; the POST run recomputes the plan,
/// refuses unless the hash matches and the plan has something to delete, and re-reads
/// every row and its open deployments right before writing it, and the row again after
/// its retractions, skipping one that changed. Re-running is safe:
/// deleted rows drop out of the plan, so a finished cleanup has an empty plan and a
/// second run is refused.</para>
/// </summary>
public class TaskListActiveWithoutPlanningRepairService(
    BackendConfigurationPnDbContext dbContext,
    ItemsPlanningPnDbContext itemsPlanningPnDbContext,
    IEFormCoreService coreHelper,
    IBackendConfigurationTaskWizardService taskWizardService,
    ILogger<TaskListActiveWithoutPlanningRepairService> logger) : ITaskListActiveWithoutPlanningRepairService
{
    internal const string StateNoPlanning = "NoPlanning";
    internal const string StatePlanningRemoved = "PlanningRemoved";
    internal const string StatePlanningMissing = "PlanningMissing";
    internal const string ReasonLegacySibling = "LegacySibling";

    private const int CompletedStatus = 100;

    /// <summary>
    /// Test seam: retracts one device deployment by MicrotingUid. Null (production) uses
    /// <c>core.CaseDelete</c>, which needs the Microting cloud that tests do not have.
    /// </summary>
    internal Func<int, Task<bool>> RetractDeployment { get; set; }

    /// <summary>
    /// Test seam: runs for every planned deletion right before the row is re-read, so a
    /// test can change the row between the plan and the write.
    /// </summary>
    internal Func<ActiveWithoutPlanningRowModel, Task> OnBeforeWrite { get; set; } = _ => Task.CompletedTask;

    public async Task<OperationDataResult<ActiveWithoutPlanningRepairPlanModel>> DryRunAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var plan = await ComputeAsync(cancellationToken).ConfigureAwait(false);
            LogPlan(plan, "dry run");
            return new OperationDataResult<ActiveWithoutPlanningRepairPlanModel>(true, plan);
        }
        catch (Exception e)
        {
            logger.LogError(e, "TaskListActiveWithoutPlanningRepair: dry run failed");
            return new OperationDataResult<ActiveWithoutPlanningRepairPlanModel>(false,
                $"Active-without-planning dry run failed: {e.Message}");
        }
    }

    public async Task<OperationDataResult<ActiveWithoutPlanningRepairRunResultModel>> RunAsync(
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
                    "TaskListActiveWithoutPlanningRepair: refused — plan hash {Given} does not match the current plan {Current}; nothing was written",
                    planHash, plan.PlanHash);
                return Refused("The data changed since the reviewed dry run (plan hash mismatch). Run the dry run again and review it.");
            }
            if (plan.Deletions.Count == 0)
            {
                return Refused("The plan has nothing to delete; the cleanup was not started.");
            }

            LogPlan(plan, "run");
            // From here on the run writes; a closed browser tab must not stop it halfway.
            var result = new ActiveWithoutPlanningRepairRunResultModel { Plan = plan };
            foreach (var row in plan.Deletions)
            {
                await ApplyAsync(row, result).ConfigureAwait(false);
            }

            logger.LogInformation(
                "TaskListActiveWithoutPlanningRepair: finished — {Deleted} rows deleted, {Retracted} cases retracted, {Changed} skipped as changed, {Failures} failures",
                result.DeletedAreaRulePlanningIds.Count, result.RetractedCases, result.SkippedAsChanged.Count,
                result.Failures.Count);
            return new OperationDataResult<ActiveWithoutPlanningRepairRunResultModel>(result.Failures.Count == 0, result);
        }
        catch (Exception e)
        {
            logger.LogError(e, "TaskListActiveWithoutPlanningRepair: run failed");
            return new OperationDataResult<ActiveWithoutPlanningRepairRunResultModel>(false,
                $"Active-without-planning cleanup failed: {e.Message}");
        }
    }

    private static OperationDataResult<ActiveWithoutPlanningRepairRunResultModel> Refused(string message)
        => new(false, message);

    // ── Plan ────────────────────────────────────────────────────────────────

    private async Task<ActiveWithoutPlanningRepairPlanModel> ComputeAsync(CancellationToken ct)
    {
        var activeRows = await dbContext.AreaRulePlannings
            .AsNoTracking()
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed && x.Status)
            .Select(x => new { x.Id, x.AreaRuleId, x.PropertyId, x.AreaId, x.ItemPlanningId })
            .ToListAsync(ct).ConfigureAwait(false);

        var planningIds = activeRows.Where(x => x.ItemPlanningId > 0).Select(x => x.ItemPlanningId).Distinct().ToList();
        var planningStates = await itemsPlanningPnDbContext.Plannings
            .AsNoTracking()
            .Where(x => planningIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.WorkflowState, ct).ConfigureAwait(false);

        var candidates = activeRows
            .Select(x => new { Row = x, State = PlanningStateOf(x.ItemPlanningId, planningStates) })
            .Where(x => x.State != null)
            .OrderBy(x => x.Row.Id)
            .ToList();

        var plan = new ActiveWithoutPlanningRepairPlanModel();
        if (candidates.Count == 0)
        {
            plan.PlanHash = HashOf(plan);
            return plan;
        }

        var areaRuleIds = candidates.Select(x => x.Row.AreaRuleId).Distinct().ToList();
        var liveRowsPerAreaRule = await dbContext.AreaRulePlannings
            .AsNoTracking()
            .Where(x => areaRuleIds.Contains(x.AreaRuleId) && x.WorkflowState != Constants.WorkflowStates.Removed)
            .GroupBy(x => x.AreaRuleId)
            .Select(g => new { AreaRuleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.AreaRuleId, x => x.Count, ct).ConfigureAwait(false);

        var areaIds = candidates.Select(x => x.Row.AreaId).Distinct().ToList();
        var areaTypes = await dbContext.Areas
            .AsNoTracking()
            .Where(x => areaIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => (int)x.Type, ct).ConfigureAwait(false);

        foreach (var candidate in candidates)
        {
            var row = new ActiveWithoutPlanningRowModel
            {
                AreaRulePlanningId = candidate.Row.Id,
                AreaRuleId = candidate.Row.AreaRuleId,
                PropertyId = candidate.Row.PropertyId,
                AreaId = candidate.Row.AreaId,
                AreaType = areaTypes.TryGetValue(candidate.Row.AreaId, out var type) ? type : null,
                ItemPlanningId = candidate.Row.ItemPlanningId,
                PlanningState = candidate.State
            };
            if (liveRowsPerAreaRule.GetValueOrDefault(candidate.Row.AreaRuleId) > 1)
            {
                row.SkipReason = ReasonLegacySibling;
                plan.Skipped.Add(row);
                continue;
            }

            row.CasesToRetract = await OpenCasesOfAsync(candidate.Row.ItemPlanningId, ct).ConfigureAwait(false);
            plan.Deletions.Add(row);
        }

        plan.PlanHash = HashOf(plan);
        return plan;
    }

    /// <summary>Null when the planning is live (not a candidate).</summary>
    private static string PlanningStateOf(int itemPlanningId, Dictionary<int, string> planningStates)
    {
        if (itemPlanningId == 0)
        {
            return StateNoPlanning;
        }
        if (!planningStates.TryGetValue(itemPlanningId, out var state))
        {
            return StatePlanningMissing;
        }
        return state == Constants.WorkflowStates.Removed ? StatePlanningRemoved : null;
    }

    /// <summary>
    /// The open device deployments of a planning, through the same PlanningCase →
    /// PlanningCaseSite link the task delete uses, each resolved as
    /// <c>BackendConfigurationPropertyAreasServiceHelper.ResolvePlannedCaseUidAsync</c>
    /// does: the legacy Type9 shape (MicrotingSdkCaseId holds the CheckListSite's
    /// MicrotingUid) is recognised first, so its uid is never read as a Cases.Id; a link
    /// whose CheckListSite row is gone is ambiguous and skipped. An existing Cases row
    /// decides its link — the CheckListSite fallback applies only when it does not exist.
    /// A case counts when it is live (not removed or retracted), not completed (Status 100
    /// or DoneAt set — those are records and are kept) and has a MicrotingUid. A
    /// CheckListSite counts when it is live and its uid is not a completed record:
    /// <c>core.CaseDelete</c> removes the Cases row when it is the only one with that uid
    /// (see <c>LegacyChemicalCleanupService.CompletedRecordUids</c>).
    /// </summary>
    private async Task<List<ActiveWithoutPlanningCaseModel>> OpenCasesOfAsync(int planningId, CancellationToken ct)
    {
        if (planningId == 0)
        {
            return [];
        }

        var deployments = await (
                from planningCase in itemsPlanningPnDbContext.PlanningCases
                join site in itemsPlanningPnDbContext.PlanningCaseSites on planningCase.Id equals site.PlanningCaseId
                where planningCase.PlanningId == planningId
                      && planningCase.WorkflowState != Constants.WorkflowStates.Removed
                      && site.WorkflowState != Constants.WorkflowStates.Removed
                      && (site.MicrotingSdkCaseId != 0 || site.MicrotingCheckListSitId != 0)
                select new { site.MicrotingSdkCaseId, site.MicrotingCheckListSitId })
            .Distinct()
            .ToListAsync(ct).ConfigureAwait(false);
        if (deployments.Count == 0)
        {
            return [];
        }

        var core = await coreHelper.GetCore().ConfigureAwait(false);
        await using var sdkDbContext = core.DbContextHelper.GetDbContext();

        var linkedCheckListSiteIds = deployments.Select(x => x.MicrotingCheckListSitId).Where(x => x != 0).Distinct().ToList();
        var checkListSitesById = await sdkDbContext.CheckListSites
            .AsNoTracking()
            .Where(x => linkedCheckListSiteIds.Contains(x.Id))
            .Select(x => new { x.Id, x.MicrotingUid, x.WorkflowState })
            .ToDictionaryAsync(x => x.Id, ct).ConfigureAwait(false);

        // Drop links whose CheckListSite row is gone, then split off the legacy shape.
        var resolvable = deployments
            .Where(x => x.MicrotingCheckListSitId == 0 || checkListSitesById.ContainsKey(x.MicrotingCheckListSitId))
            .ToList();
        bool IsLegacy(int checkListSiteId, int sdkCaseId) =>
            checkListSiteId != 0 && checkListSitesById[checkListSiteId].MicrotingUid == sdkCaseId;

        var caseIds = resolvable
            .Where(x => x.MicrotingSdkCaseId != 0 && !IsLegacy(x.MicrotingCheckListSitId, x.MicrotingSdkCaseId))
            .Select(x => x.MicrotingSdkCaseId).Distinct().ToList();
        // Every existing Cases row, whatever its uid: an existing row decides the link, so
        // the CheckListSite fallback is only for a link whose Cases row does not exist.
        var casesById = await sdkDbContext.Cases
            .AsNoTracking()
            .Where(x => caseIds.Contains(x.Id))
            .Select(x => new { x.Id, x.MicrotingUid, x.WorkflowState, x.Status, x.DoneAt })
            .ToDictionaryAsync(x => x.Id, ct).ConfigureAwait(false);

        var openCheckListSites = resolvable
            .Where(x => x.MicrotingCheckListSitId != 0
                        && (IsLegacy(x.MicrotingCheckListSitId, x.MicrotingSdkCaseId)
                            || !casesById.ContainsKey(x.MicrotingSdkCaseId)))
            .Select(x => checkListSitesById[x.MicrotingCheckListSitId])
            .Where(x => BackendConfigurationPropertyAreasServiceHelper.IsLive(x.WorkflowState))
            .DistinctBy(x => x.Id)
            .ToList();
        var checkListSiteUids = openCheckListSites.Select(x => x.MicrotingUid).Distinct().ToList();
        var completedRecordUids = (await LegacyCleanup.CompletedRecordUids(sdkDbContext, checkListSiteUids)
            .ToListAsync(ct).ConfigureAwait(false)).ToHashSet();

        return casesById.Values
            .Where(x => x.MicrotingUid != null
                        && BackendConfigurationPropertyAreasServiceHelper.IsLive(x.WorkflowState)
                        && x.Status != CompletedStatus && x.DoneAt == null)
            .Select(x => new ActiveWithoutPlanningCaseModel { SdkCaseId = x.Id, MicrotingUid = x.MicrotingUid!.Value })
            .Concat(openCheckListSites
                .Where(x => !completedRecordUids.Contains(x.MicrotingUid))
                .Select(x => new ActiveWithoutPlanningCaseModel { CheckListSiteId = x.Id, MicrotingUid = x.MicrotingUid }))
            .GroupBy(x => x.MicrotingUid)
            .Select(g => g.First())
            .OrderBy(x => x.SdkCaseId).ThenBy(x => x.CheckListSiteId).ThenBy(x => x.MicrotingUid)
            .ToList();
    }

    /// <summary>The identity of a deployment, as the plan hash and the pre-write recheck compare it.</summary>
    private static string KeyOf(ActiveWithoutPlanningCaseModel deployment)
        => $"{deployment.SdkCaseId}:{deployment.CheckListSiteId}:{deployment.MicrotingUid}";

    private static string HashOf(ActiveWithoutPlanningRepairPlanModel plan)
    {
        var sb = new StringBuilder();
        foreach (var row in plan.Deletions.Concat(plan.Skipped))
        {
            sb.Append(row.AreaRulePlanningId).Append('|').Append(row.AreaRuleId).Append('|')
                .Append(row.PropertyId).Append('|').Append(row.AreaId).Append('|').Append(row.AreaType).Append('|')
                .Append(row.ItemPlanningId).Append('|').Append(row.PlanningState).Append('|')
                .Append(row.SkipReason ?? "delete").Append('|')
                .Append(string.Join(',', row.CasesToRetract.Select(KeyOf)))
                .Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    // ── Run ─────────────────────────────────────────────────────────────────

    private async Task ApplyAsync(ActiveWithoutPlanningRowModel row, ActiveWithoutPlanningRepairRunResultModel result)
    {
        var what = $"AreaRulePlanning {row.AreaRulePlanningId}";
        try
        {
            await OnBeforeWrite(row).ConfigureAwait(false);
            var reason = await ChangedSincePlanAsync(row).ConfigureAwait(false);
            if (reason != null)
            {
                SkipAsChanged(row, result, reason);
                return;
            }

            // The plan was computed for the whole run; a case completed since then must not
            // be retracted, so the deployments are re-read and must still be the planned ones.
            var currentDeployments = await OpenCasesOfAsync(row.ItemPlanningId, CancellationToken.None)
                .ConfigureAwait(false);
            if (!currentDeployments.Select(KeyOf).Order().SequenceEqual(row.CasesToRetract.Select(KeyOf).Order()))
            {
                SkipAsChanged(row, result, "its open device deployments changed");
                return;
            }

            if (row.CasesToRetract.Count > 0)
            {
                var retract = RetractDeployment;
                if (retract == null)
                {
                    var core = await coreHelper.GetCore().ConfigureAwait(false);
                    retract = core.CaseDelete;
                }
                foreach (var sdkCase in row.CasesToRetract)
                {
                    if (!await retract(sdkCase.MicrotingUid).ConfigureAwait(false))
                    {
                        throw new InvalidOperationException(
                            $"retracting SDK case {sdkCase.SdkCaseId} / CheckListSite {sdkCase.CheckListSiteId} (MicrotingUid {sdkCase.MicrotingUid}) failed");
                    }
                    result.RetractedCases++;
                    logger.LogInformation(
                        "TaskListActiveWithoutPlanningRepair: {What} retracted SDK case {SdkCaseId} / CheckListSite {CheckListSiteId} (MicrotingUid {MicrotingUid})",
                        what, sdkCase.SdkCaseId, sdkCase.CheckListSiteId, sdkCase.MicrotingUid);
                }

                // The retractions are remote calls; the row may have changed meanwhile.
                reason = await ChangedSincePlanAsync(row).ConfigureAwait(false);
                if (reason != null)
                {
                    SkipAsChanged(row, result, $"{reason} (after its open deployments were retracted)");
                    return;
                }
            }

            // Cases are retracted above (completed ones kept), so the delete must not
            // retract again — it would also pull the completed ones.
            var deleted = await taskWizardService
                .DeleteTaskDeferredRetraction(row.AreaRulePlanningId, retractDeviceCases: false)
                .ConfigureAwait(false);
            if (!deleted.Success)
            {
                throw new InvalidOperationException($"the task delete failed: {deleted.Message}");
            }

            result.DeletedAreaRulePlanningIds.Add(row.AreaRulePlanningId);
            logger.LogInformation(
                "TaskListActiveWithoutPlanningRepair: {What} (AreaRule {AreaRuleId}, property {PropertyId}, planning {ItemPlanningId} {PlanningState}) soft-deleted",
                what, row.AreaRuleId, row.PropertyId, row.ItemPlanningId, row.PlanningState);
        }
        catch (Exception e)
        {
            logger.LogError(e, "TaskListActiveWithoutPlanningRepair: {What} not deleted", what);
            result.Failures.Add($"{what}: {e.Message}");
        }
    }

    private void SkipAsChanged(ActiveWithoutPlanningRowModel row, ActiveWithoutPlanningRepairRunResultModel result,
        string reason)
    {
        var what = $"AreaRulePlanning {row.AreaRulePlanningId}";
        logger.LogWarning("TaskListActiveWithoutPlanningRepair: {What} skipped: {Reason} (the next dry run re-evaluates it)",
            what, reason);
        result.SkippedAsChanged.Add($"{what}: {reason}");
    }

    /// <summary>Re-reads the row right before its write; null when it is still what the plan saw.</summary>
    private async Task<string> ChangedSincePlanAsync(ActiveWithoutPlanningRowModel row)
    {
        var current = await dbContext.AreaRulePlannings
            .AsNoTracking()
            .Where(x => x.Id == row.AreaRulePlanningId)
            .Select(x => new { x.WorkflowState, x.Status, x.ItemPlanningId, x.AreaRuleId })
            .SingleOrDefaultAsync().ConfigureAwait(false);
        if (current == null || current.WorkflowState == Constants.WorkflowStates.Removed)
        {
            return "the row is already removed";
        }
        if (!current.Status)
        {
            return "the row was deactivated";
        }
        if (current.ItemPlanningId != row.ItemPlanningId || current.AreaRuleId != row.AreaRuleId)
        {
            return "the row's planning or rule changed";
        }

        if (current.ItemPlanningId != 0)
        {
            // An object projection, so a row with a null WorkflowState (live, as
            // PlanningStateOf reads it) is not mistaken for a missing planning.
            var planning = await itemsPlanningPnDbContext.Plannings.AsNoTracking()
                .Where(x => x.Id == current.ItemPlanningId)
                .Select(x => new { x.WorkflowState })
                .SingleOrDefaultAsync().ConfigureAwait(false);
            if (planning != null && planning.WorkflowState != Constants.WorkflowStates.Removed)
            {
                return "the row's planning is live again";
            }
        }

        var hasLiveSibling = await dbContext.AreaRulePlannings.AsNoTracking()
            .Where(x => x.AreaRuleId == current.AreaRuleId && x.Id != row.AreaRulePlanningId)
            .AnyAsync(x => x.WorkflowState != Constants.WorkflowStates.Removed).ConfigureAwait(false);
        return hasLiveSibling ? "its rule gained a live sibling row" : null;
    }

    private void LogPlan(ActiveWithoutPlanningRepairPlanModel plan, string mode)
    {
        logger.LogInformation(
            "TaskListActiveWithoutPlanningRepair ({Mode}): {Deletions} rows to delete ({Cases} open cases to retract), {Skipped} skipped, plan {PlanHash}",
            mode, plan.Deletions.Count, plan.Deletions.Sum(x => x.CasesToRetract.Count), plan.Skipped.Count, plan.PlanHash);
        foreach (var row in plan.Skipped)
        {
            logger.LogInformation(
                "TaskListActiveWithoutPlanningRepair ({Mode}): AreaRulePlanning {ArpId} (AreaRule {AreaRuleId}, area type {AreaType}) skipped: {Reason}",
                mode, row.AreaRulePlanningId, row.AreaRuleId, row.AreaType, row.SkipReason);
        }
    }
}
