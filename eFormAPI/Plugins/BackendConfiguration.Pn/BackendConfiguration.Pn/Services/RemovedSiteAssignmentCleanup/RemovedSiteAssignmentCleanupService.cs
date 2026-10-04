using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.AssignmentWorker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;

namespace BackendConfiguration.Pn.Services.RemovedSiteAssignmentCleanup;

/// <summary>
/// #1376 — one-off cleanup of the explicit assignments (BC <c>PlanningSites</c>) that removed
/// workers left behind on inactive legacy rules.
///
/// <para>Before #1376 the worker delete / property unassign cleanup stopped at the first
/// legacy rule with <c>ItemPlanningId = 0</c>, and the core device-user delete never touches
/// plugin tables at all, so assignments of SDK sites that are removed (or no longer exist)
/// stayed live. On an inactive legacy rule they are invisible until someone reactivates the
/// rule, which would then hand the task to a worker who is gone.</para>
///
/// <para>Scope, deliberately narrow: a live <c>PlanningSite</c> on a live
/// <c>AreaRulePlanning</c> that is inactive (<c>Status = false</c>) and legacy
/// (<c>ItemPlanningId = 0</c>, so there is no items-planning side to touch), whose SDK site
/// is removed or missing. Active rules are never touched: they need a person's decision.
/// Only the PlanningSite is soft-deleted; nothing else is written.</para>
///
/// <para>The run is opt-in and reviewed: admin endpoint only, only with the hash of a dry
/// run, and every row is re-read right before its write. The write is idempotent, so a
/// second run finds nothing to do and is refused; no marker is needed.</para>
/// </summary>
public class RemovedSiteAssignmentCleanupService(
    BackendConfigurationPnDbContext dbContext,
    IEFormCoreService coreHelper,
    IUserService userService,
    ILogger<RemovedSiteAssignmentCleanupService> logger) : IRemovedSiteAssignmentCleanupService
{
    internal const string SiteRemoved = "removed";
    internal const string SiteMissing = "missing";

    /// <summary>
    /// Test seam: runs right before every write of the real run, with the PlanningSite id
    /// about to be removed. Lets a test change the data between plan and write.
    /// </summary>
    internal Func<int, Task> OnBeforeWrite { get; set; } = _ => Task.CompletedTask;

    public async Task<OperationDataResult<RemovedSiteAssignmentCleanupPlanModel>> DryRunAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var plan = await ComputeAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation(
                "RemovedSiteAssignmentCleanup: dry run — {Count} assignments of removed or missing sites on inactive legacy rules, plan hash {Hash}",
                plan.Assignments.Count, plan.PlanHash);
            return new OperationDataResult<RemovedSiteAssignmentCleanupPlanModel>(true, plan);
        }
        catch (Exception e)
        {
            logger.LogError(e, "RemovedSiteAssignmentCleanup: dry run failed");
            return new OperationDataResult<RemovedSiteAssignmentCleanupPlanModel>(false,
                $"Removed-site assignment cleanup dry run failed: {e.Message}");
        }
    }

    public async Task<OperationDataResult<RemovedSiteAssignmentCleanupRunResultModel>> RunAsync(
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
                    "RemovedSiteAssignmentCleanup: refused — plan hash {Given} does not match the current plan {Current}; nothing was written",
                    planHash, plan.PlanHash);
                return Refused("The data changed since the reviewed dry run (plan hash mismatch). Run the dry run again and review it.");
            }
            if (plan.Assignments.Count == 0)
            {
                return Refused("The plan has nothing to remove; the cleanup was not started.");
            }

            // From here on the run writes; a closed browser tab must not stop it halfway.
            var result = new RemovedSiteAssignmentCleanupRunResultModel { Plan = plan };
            var sdkDbContext = (await coreHelper.GetCore().ConfigureAwait(false)).DbContextHelper.GetDbContext();
            foreach (var row in plan.Assignments)
            {
                try
                {
                    await OnBeforeWrite(row.PlanningSiteId).ConfigureAwait(false);

                    // Re-read everything the plan decided on, right before the write.
                    var planningSite = await dbContext.PlanningSites
                        .FirstOrDefaultAsync(x => x.Id == row.PlanningSiteId, CancellationToken.None)
                        .ConfigureAwait(false);
                    var stillQualifies = planningSite != null
                                         && planningSite.WorkflowState != Constants.WorkflowStates.Removed
                                         && planningSite.SiteId == row.SiteId
                                         && planningSite.AreaRulePlanningsId == row.AreaRulePlanningId
                                         && await dbContext.AreaRulePlannings
                                             .Where(x => x.Id == row.AreaRulePlanningId)
                                             .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                             .AnyAsync(x => !x.Status && x.ItemPlanningId == 0, CancellationToken.None)
                                             .ConfigureAwait(false)
                                         && !await sdkDbContext.Sites
                                             .AnyAsync(x => x.Id == row.SiteId
                                                            && x.WorkflowState != Constants.WorkflowStates.Removed,
                                                 CancellationToken.None)
                                             .ConfigureAwait(false);
                    if (!stillQualifies)
                    {
                        var skipped = $"PlanningSite {row.PlanningSiteId}: no longer qualifies (removed, reassigned, rule reactivated or site restored); left as it is";
                        logger.LogWarning("RemovedSiteAssignmentCleanup: {Skipped}", skipped);
                        result.Skipped.Add(skipped);
                        continue;
                    }

                    planningSite!.UpdatedByUserId = userService.UserId;
                    await planningSite.Delete(dbContext).ConfigureAwait(false);
                    result.RemovedAssignments++;
                }
                catch (Exception e)
                {
                    logger.LogError(e, "RemovedSiteAssignmentCleanup: PlanningSite {Id} could not be removed",
                        row.PlanningSiteId);
                    result.Failures.Add($"PlanningSite {row.PlanningSiteId}: {e.Message}");
                    // Drop whatever the failed write left tracked, so it cannot ride along
                    // with the next row's save.
                    dbContext.ChangeTracker.Clear();
                }
            }

            logger.LogInformation(
                "RemovedSiteAssignmentCleanup: finished — {Removed} assignments removed, {Skipped} skipped, {Failures} failures",
                result.RemovedAssignments, result.Skipped.Count, result.Failures.Count);
            return new OperationDataResult<RemovedSiteAssignmentCleanupRunResultModel>(result.Failures.Count == 0, result);
        }
        catch (Exception e)
        {
            logger.LogError(e, "RemovedSiteAssignmentCleanup: run failed");
            return Refused($"Removed-site assignment cleanup failed: {e.Message}");
        }
    }

    private static OperationDataResult<RemovedSiteAssignmentCleanupRunResultModel> Refused(string message)
        => new(false, message);

    /// <summary>Read-only: every row the run would remove, plus the plan hash.</summary>
    private async Task<RemovedSiteAssignmentCleanupPlanModel> ComputeAsync(CancellationToken ct)
    {
        // EF's C# null semantics keep legacy rows whose WorkflowState is NULL.
        var candidates = await dbContext.PlanningSites
            .AsNoTracking()
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(x => x.AreaRulePlanning.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(x => !x.AreaRulePlanning.Status && x.AreaRulePlanning.ItemPlanningId == 0)
            .OrderBy(x => x.Id)
            .Select(x => new RemovedSiteAssignmentModel
            {
                PlanningSiteId = x.Id,
                AreaRulePlanningId = x.AreaRulePlanningsId,
                PropertyId = x.AreaRulePlanning.PropertyId,
                AreaId = x.AreaId,
                SiteId = x.SiteId
            })
            .ToListAsync(ct).ConfigureAwait(false);

        var plan = new RemovedSiteAssignmentCleanupPlanModel();
        if (candidates.Count > 0)
        {
            var sdkDbContext = (await coreHelper.GetCore().ConfigureAwait(false)).DbContextHelper.GetDbContext();
            var siteIds = candidates.Select(x => x.SiteId).Distinct().ToList();
            var sites = await sdkDbContext.Sites
                .AsNoTracking()
                .Where(x => siteIds.Contains(x.Id))
                .Select(x => new { x.Id, x.WorkflowState, x.UpdatedAt })
                .ToDictionaryAsync(x => x.Id, ct).ConfigureAwait(false);

            foreach (var row in candidates)
            {
                if (!sites.TryGetValue(row.SiteId, out var site))
                {
                    row.SiteState = SiteMissing;
                }
                else if (site.WorkflowState == Constants.WorkflowStates.Removed)
                {
                    row.SiteState = SiteRemoved;
                    row.SiteRemovedAt = site.UpdatedAt;
                }
                else
                {
                    continue;
                }
                plan.Assignments.Add(row);
            }
        }

        plan.PlanHash = HashOf(plan.Assignments);
        return plan;
    }

    private static string HashOf(IEnumerable<RemovedSiteAssignmentModel> rows)
    {
        var sb = new StringBuilder();
        foreach (var r in rows)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"{r.PlanningSiteId}|{r.AreaRulePlanningId}|{r.SiteId}|{r.SiteState}\n");
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}
