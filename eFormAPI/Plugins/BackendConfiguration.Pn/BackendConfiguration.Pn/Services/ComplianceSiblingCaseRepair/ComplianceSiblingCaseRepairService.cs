using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models.Compliances;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data;

namespace BackendConfiguration.Pn.Services.ComplianceSiblingCaseRepair;

/// <summary>
/// #1371 — one-time repair of the compliances the legacy device completion path left
/// pointing at the wrong case. A task assigned to several workers deploys one case per
/// worker; when a worker other than the one stored on the compliance completed it,
/// <c>eFormCompletedHandler</c> (service plugin, fixed in its #588) soft-removed the
/// compliance without repointing it, and the stored case was retracted.
///
/// <para>The plan: every removed compliance whose own SDK case is not completed but
/// which has a completed sibling (<see cref="CompletedSiblingCases"/> — same
/// <c>PlanningCaseId</c>, earliest completion wins) is repointed at that sibling's case.
/// Only <c>MicrotingSdkCaseId</c> changes; nothing is deleted. The readers already
/// resolve the sibling at read time, so the repair makes the stored data say what the
/// screens show.</para>
///
/// <para>Same reviewed contract as the #1294 monthly re-anchor repair: the dry run (GET)
/// writes nothing and returns a <c>PlanHash</c>; the real run (POST) only starts with
/// that hash, and each row is re-checked right before its write. There is no run marker:
/// a repointed compliance's case is completed, so it drops out of every later plan — a
/// second run finds nothing to do and is refused, and two concurrent runs write the
/// same values.</para>
/// </summary>
public class ComplianceSiblingCaseRepairService(
    BackendConfigurationPnDbContext dbContext,
    ItemsPlanningPnDbContext itemsPlanningPnDbContext,
    IEFormCoreService coreHelper,
    ILogger<ComplianceSiblingCaseRepairService> logger) : IComplianceSiblingCaseRepairService
{
    /// <summary>Case ids per batched SDK / items-planning lookup, well under the placeholder ceiling.</summary>
    internal const int BatchSize = 1000;

    public async Task<OperationDataResult<ComplianceSiblingCaseRepairPlanModel>> DryRunAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var plan = await ComputeAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation(
                "ComplianceSiblingCaseRepair (dry run): {Count} compliances to repoint, plan {PlanHash}",
                plan.Repoints.Count, plan.PlanHash);
            return new OperationDataResult<ComplianceSiblingCaseRepairPlanModel>(true, plan);
        }
        catch (Exception e)
        {
            logger.LogError(e, "ComplianceSiblingCaseRepair: dry run failed");
            return new OperationDataResult<ComplianceSiblingCaseRepairPlanModel>(false,
                $"Sibling completed case dry run failed: {e.Message}");
        }
    }

    public async Task<OperationDataResult<ComplianceSiblingCaseRepairRunResultModel>> RunAsync(
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
                    "ComplianceSiblingCaseRepair: refused — plan hash {Given} does not match the current plan {Current}; nothing was written",
                    planHash, plan.PlanHash);
                return Refused("The data changed since the reviewed dry run (plan hash mismatch). Run the dry run again and review it.");
            }
            if (plan.Repoints.Count == 0)
            {
                return Refused("The plan has nothing to write; the repair was not started.");
            }

            // From here on the run writes; a closed browser tab must not stop it halfway.
            var result = new ComplianceSiblingCaseRepairRunResultModel { Plan = plan };
            var core = await coreHelper.GetCore().ConfigureAwait(false);
            await using var sdkDbContext = core.DbContextHelper.GetDbContext();
            foreach (var repoint in plan.Repoints)
            {
                await RepointAsync(repoint, sdkDbContext, result).ConfigureAwait(false);
            }

            logger.LogInformation(
                "ComplianceSiblingCaseRepair: finished — {Repointed} compliances repointed, {Skipped} skipped, {Failures} failures",
                result.Repointed, result.Skipped.Count, result.Failures.Count);
            return new OperationDataResult<ComplianceSiblingCaseRepairRunResultModel>(result.Failures.Count == 0, result);
        }
        catch (Exception e)
        {
            logger.LogError(e, "ComplianceSiblingCaseRepair: run failed");
            return Refused($"Sibling completed case repair failed: {e.Message}");
        }
    }

    private static OperationDataResult<ComplianceSiblingCaseRepairRunResultModel> Refused(string message)
        => new(false, message);

    private async Task<ComplianceSiblingCaseRepairPlanModel> ComputeAsync(CancellationToken ct)
    {
        var removed = await dbContext.Compliances
            .AsNoTracking()
            .Where(c => c.WorkflowState == Constants.WorkflowStates.Removed && c.MicrotingSdkCaseId > 0)
            .OrderBy(c => c.Id)
            .Select(c => new { c.Id, c.PlanningId, c.PropertyId, c.Deadline, c.MicrotingSdkCaseId })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var plan = new ComplianceSiblingCaseRepairPlanModel();
        if (removed.Count > 0)
        {
            var core = await coreHelper.GetCore().ConfigureAwait(false);
            await using var sdkDbContext = core.DbContextHelper.GetDbContext();

            // Most removed compliances are ordinary completed logs. Narrow to the ones
            // whose own case is NOT completed first, so the sibling lookup (unindexed on
            // the items-planning side) only sees the rare #1371 shape.
            var notDoneCaseIds = new List<int>();
            foreach (var chunk in removed.Select(c => c.MicrotingSdkCaseId).Distinct().Chunk(BatchSize))
            {
                var completed = (await sdkDbContext.Cases
                        .Where(c => chunk.Contains(c.Id) && c.Status == 100)
                        .Select(c => c.Id)
                        .ToListAsync(ct)
                        .ConfigureAwait(false))
                    .ToHashSet();
                notDoneCaseIds.AddRange(chunk.Where(id => !completed.Contains(id)));
            }

            var siblings = new Dictionary<int, Microting.eForm.Infrastructure.Data.Entities.Case>();
            foreach (var chunk in notDoneCaseIds.Chunk(BatchSize))
            {
                var found = await CompletedSiblingCases.FindAsync(itemsPlanningPnDbContext, sdkDbContext, chunk)
                    .ConfigureAwait(false);
                foreach (var (ownCaseId, sibling) in found)
                {
                    siblings[ownCaseId] = sibling;
                }
            }

            foreach (var c in removed)
            {
                if (!siblings.TryGetValue(c.MicrotingSdkCaseId, out var sibling)) continue;
                plan.Repoints.Add(new ComplianceSiblingCaseRepointModel
                {
                    ComplianceId = c.Id,
                    PlanningId = c.PlanningId,
                    PropertyId = c.PropertyId,
                    Deadline = c.Deadline,
                    OldSdkCaseId = c.MicrotingSdkCaseId,
                    NewSdkCaseId = sibling.Id
                });
            }
        }

        plan.PlanHash = HashOf(plan);
        return plan;
    }

    private async Task RepointAsync(ComplianceSiblingCaseRepointModel repoint,
        Microting.eForm.Infrastructure.MicrotingDbContext sdkDbContext,
        ComplianceSiblingCaseRepairRunResultModel result)
    {
        var what = $"compliance {repoint.ComplianceId}";
        var compliance = await dbContext.Compliances
            .FirstOrDefaultAsync(x => x.Id == repoint.ComplianceId)
            .ConfigureAwait(false);
        if (compliance == null
            || compliance.WorkflowState != Constants.WorkflowStates.Removed
            || compliance.MicrotingSdkCaseId != repoint.OldSdkCaseId)
        {
            logger.LogInformation(
                "ComplianceSiblingCaseRepair: {What} skipped: it changed since the dry run", what);
            result.Skipped.Add($"{what}: changed since the dry run");
            return;
        }

        // The planned target must still be the completed sibling the readers resolve:
        // its completion may have been undone, or an earlier completion may have arrived.
        var current = await CompletedSiblingCases.FindAsync(
                itemsPlanningPnDbContext, sdkDbContext, [repoint.OldSdkCaseId])
            .ConfigureAwait(false);
        if (current.GetValueOrDefault(repoint.OldSdkCaseId)?.Id != repoint.NewSdkCaseId)
        {
            logger.LogInformation(
                "ComplianceSiblingCaseRepair: {What} skipped: its completed sibling changed during the run", what);
            result.Skipped.Add($"{what}: completed sibling changed during the run");
            return;
        }

        try
        {
            compliance.MicrotingSdkCaseId = repoint.NewSdkCaseId;
            await compliance.Update(dbContext).ConfigureAwait(false);
            result.Repointed++;
        }
        catch (Exception e)
        {
            // Discard the pending change, or every later SaveChanges of this context
            // would re-send (and fail on) it.
            var entry = dbContext.Entry(compliance);
            entry.CurrentValues.SetValues(entry.OriginalValues);
            entry.State = EntityState.Unchanged;
            logger.LogError(e, "ComplianceSiblingCaseRepair: writing {What} failed; left unchanged", what);
            result.Failures.Add($"{what}: {e.Message}");
        }
    }

    internal static string HashOf(ComplianceSiblingCaseRepairPlanModel plan)
    {
        var sb = new StringBuilder();
        foreach (var r in plan.Repoints)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"S|{r.ComplianceId}|{r.PlanningId}|{r.PropertyId}|{r.Deadline:O}|{r.OldSdkCaseId}|{r.NewSdkCaseId}\n");
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}
