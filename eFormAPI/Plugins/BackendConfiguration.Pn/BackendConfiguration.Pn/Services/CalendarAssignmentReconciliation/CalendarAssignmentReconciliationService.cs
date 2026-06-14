using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.EventDeployService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using SdkCore = eFormCore.Core;
using SdkDbContext = Microting.eForm.Infrastructure.MicrotingDbContext;

namespace BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;

/// <summary>
/// Retroactive reconciliation engine for calendar worker-tag assignments.
/// For a calendar event (AreaRulePlanning), brings every FUTURE already-deployed
/// occurrence into line with the event's effective recipient set (explicit
/// PlanningSites ∪ live worker-tag members): deploys missing sites and retracts
/// sites no longer assigned. Completed cases (SDK Case.Status == 100) are
/// immutable and never touched. Occurrences carrying an active per-occurrence
/// CalendarOccurrenceException are skipped — those are managed explicitly and
/// worker tags must not fight them.
/// </summary>
public class CalendarAssignmentReconciliationService(
    BackendConfigurationPnDbContext backendConfigurationPnDbContext,
    ItemsPlanningPnDbContext itemsPlanningPnDbContext,
    IEFormCoreService coreHelper,
    IEventDeployService eventDeployService,
    ICalendarAssignmentResolver resolver,
    ILogger<CalendarAssignmentReconciliationService> logger)
    : ICalendarAssignmentReconciliationService
{
    private const int CompletedStatus = 100;

    public async Task ReconcileEventAsync(int areaRulePlanningId, CancellationToken ct = default)
    {
        // 1. Load the AreaRulePlanning (not removed). Skip if missing or inactive.
        var arp = await backendConfigurationPnDbContext.AreaRulePlannings
            .Where(x => x.Id == areaRulePlanningId
                        && x.WorkflowState != Constants.WorkflowStates.Removed)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        if (arp == null || !arp.Status)
        {
            return;
        }

        var planningId = arp.ItemPlanningId;

        // 2. Effective recipient set.
        var desired = await resolver.ResolveEffectiveSiteIdsAsync(areaRulePlanningId, ct)
            .ConfigureAwait(false);

        // 3. SDK core + db context (needed for case status reads and CaseDelete).
        var sdkCore = await coreHelper.GetCore().ConfigureAwait(false);
        await using var sdkDbContext = sdkCore.DbContextHelper.GetDbContext();

        // 4. Future, already-deployed occurrence dates for this planning.
        var now = DateTime.UtcNow;
        var futureDeadlines = await backendConfigurationPnDbContext.Compliances
            .Where(x => x.PlanningId == planningId
                        && x.WorkflowState != Constants.WorkflowStates.Removed
                        && x.Deadline > now)
            .Select(x => x.Deadline)
            .ToListAsync(ct).ConfigureAwait(false);

        var occurrenceDates = futureDeadlines
            .Select(d => d.Date)
            .Distinct()
            .ToList();

        if (occurrenceDates.Count == 0)
        {
            return;
        }

        // 5. Active per-occurrence exception dates to skip.
        var exceptions = await backendConfigurationPnDbContext.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == areaRulePlanningId
                        && x.WorkflowState != Constants.WorkflowStates.Removed
                        && !x.IsDeleted)
            .Select(x => new { x.OriginalDate, x.NewDate })
            .ToListAsync(ct).ConfigureAwait(false);

        var exceptionDates = new HashSet<DateTime>();
        foreach (var ex in exceptions)
        {
            exceptionDates.Add(ex.OriginalDate.Date);
            if (ex.NewDate.HasValue)
            {
                exceptionDates.Add(ex.NewDate.Value.Date);
            }
        }

        // 6. Reconcile each remaining occurrence.
        foreach (var occurrenceDate in occurrenceDates)
        {
            if (exceptionDates.Contains(occurrenceDate))
            {
                continue;
            }

            // a. SDK case ids deployed for this (planning, date).
            var caseIds = await backendConfigurationPnDbContext.Compliances
                .Where(x => x.PlanningId == planningId
                            && x.WorkflowState != Constants.WorkflowStates.Removed
                            && x.MicrotingSdkCaseId > 0)
                .Where(x => x.Deadline.Date == occurrenceDate)
                .Select(x => x.MicrotingSdkCaseId)
                .ToListAsync(ct).ConfigureAwait(false);

            // b. Backing SDK cases (site + status).
            var sdkCases = caseIds.Count == 0
                ? new List<CaseStatus>()
                : await sdkDbContext.Cases
                    .Where(x => caseIds.Contains(x.Id) && x.SiteId != null)
                    .Select(x => new CaseStatus(x.SiteId.Value, x.Status))
                    .ToListAsync(ct).ConfigureAwait(false);

            // c. Split into non-completed and completed site sets.
            var actualNonCompleted = sdkCases
                .Where(x => x.Status != CompletedStatus)
                .Select(x => x.SiteId)
                .ToHashSet();
            var completed = sdkCases
                .Where(x => x.Status == CompletedStatus)
                .Select(x => x.SiteId)
                .ToHashSet();

            // d. Plan add/remove.
            var plan = AssignmentReconciliationPlanner.Plan(desired, actualNonCompleted, completed);

            // e. Deploy missing sites.
            foreach (var siteId in plan.ToAdd)
            {
                try
                {
                    await eventDeployService
                        .EnsureComplianceForOccurrenceAsync(arp, occurrenceDate, siteId, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    logger.LogError(e,
                        "Failed to deploy occurrence for AreaRulePlanning {ArpId}, planning {PlanningId}, date {Date}, site {SiteId}",
                        areaRulePlanningId, planningId, occurrenceDate, siteId);
                }
            }

            // f. Retract sites no longer assigned. Load the (planning, date)
            //    compliance rows ONCE here (tracked, for soft-delete) and reuse
            //    across every site in ToRemove instead of re-querying per site.
            List<Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.Compliance> complianceRowsForDate = null;
            if (plan.ToRemove.Count > 0)
            {
                complianceRowsForDate = await backendConfigurationPnDbContext.Compliances
                    .Where(x => x.PlanningId == planningId
                                && x.WorkflowState != Constants.WorkflowStates.Removed
                                && x.MicrotingSdkCaseId > 0)
                    .Where(x => x.Deadline.Date == occurrenceDate)
                    .ToListAsync(ct).ConfigureAwait(false);
            }

            foreach (var siteId in plan.ToRemove)
            {
                try
                {
                    await RetractSiteForOccurrenceAsync(
                            sdkCore, sdkDbContext, complianceRowsForDate, siteId, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    logger.LogError(e,
                        "Failed to retract occurrence for AreaRulePlanning {ArpId}, planning {PlanningId}, date {Date}, site {SiteId}",
                        areaRulePlanningId, planningId, occurrenceDate, siteId);
                }
            }
        }
    }

    public async Task ReconcileEventsForWorkerTagsAsync(
        IReadOnlyCollection<int> tagIds, CancellationToken ct = default)
    {
        if (tagIds == null || tagIds.Count == 0)
        {
            return;
        }

        var areaRulePlanningIds = await backendConfigurationPnDbContext.AreaRulePlanningWorkerTags
            .Where(x => tagIds.Contains(x.TagId)
                        && x.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(x => x.AreaRulePlanningId)
            .Distinct()
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var areaRulePlanningId in areaRulePlanningIds)
        {
            await ReconcileEventAsync(areaRulePlanningId, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Retracts the given site's non-completed case for one (planning, date)
    /// occurrence, mirroring the canonical retraction path: delete the SDK case
    /// via core.CaseDelete, set the owning PlanningCase to Retracted, soft-delete
    /// the matching PlanningCaseSite(s) and the Compliance row. Completed cases
    /// (Status == 100) are never touched.
    /// </summary>
    private async Task RetractSiteForOccurrenceAsync(
        SdkCore sdkCore,
        SdkDbContext sdkDbContext,
        IReadOnlyList<Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.Compliance> complianceList,
        int siteId,
        CancellationToken ct)
    {
        // Compliance rows for this (planning, date) with a backing SDK case
        // are loaded once by the caller and reused across every removed site.
        foreach (var compliance in complianceList)
        {
            var sdkCase = await sdkDbContext.Cases
                .SingleOrDefaultAsync(x => x.Id == compliance.MicrotingSdkCaseId, ct)
                .ConfigureAwait(false);

            // Only retract this site's non-completed case.
            if (sdkCase == null
                || sdkCase.SiteId != siteId
                || sdkCase.Status == CompletedStatus)
            {
                continue;
            }

            if (sdkCase.MicrotingUid != null)
            {
                await sdkCore.CaseDelete((int)sdkCase.MicrotingUid).ConfigureAwait(false);
            }

            // Soft-delete the matching PlanningCaseSite(s) and retract their owners.
            var planningCaseSites = await itemsPlanningPnDbContext.PlanningCaseSites
                .Where(x => x.MicrotingSdkCaseId == compliance.MicrotingSdkCaseId
                            && x.WorkflowState != Constants.WorkflowStates.Removed)
                .ToListAsync(ct).ConfigureAwait(false);

            foreach (var planningCaseSite in planningCaseSites)
            {
                var planningCase = await itemsPlanningPnDbContext.PlanningCases
                    .Where(x => x.Id == planningCaseSite.PlanningCaseId
                                && x.WorkflowState != Constants.WorkflowStates.Removed)
                    .FirstOrDefaultAsync(ct).ConfigureAwait(false);

                await planningCaseSite.Delete(itemsPlanningPnDbContext).ConfigureAwait(false);

                if (planningCase != null)
                {
                    // Only retract the owning PlanningCase when no non-removed
                    // PlanningCaseSite children remain. In the calendar deploy
                    // path PlanningCase is 1:1 per site, but a shared
                    // PlanningCase with other live sites must survive.
                    var remainingLiveSites = await itemsPlanningPnDbContext.PlanningCaseSites
                        .CountAsync(x => x.PlanningCaseId == planningCase.Id
                                         && x.WorkflowState != Constants.WorkflowStates.Removed,
                            ct)
                        .ConfigureAwait(false);
                    if (remainingLiveSites == 0)
                    {
                        planningCase.WorkflowState = Constants.WorkflowStates.Retracted;
                        await planningCase.Update(itemsPlanningPnDbContext).ConfigureAwait(false);
                    }
                }
            }

            await compliance.Delete(backendConfigurationPnDbContext).ConfigureAwait(false);
        }
    }

    private readonly record struct CaseStatus(int SiteId, int? Status);
}
