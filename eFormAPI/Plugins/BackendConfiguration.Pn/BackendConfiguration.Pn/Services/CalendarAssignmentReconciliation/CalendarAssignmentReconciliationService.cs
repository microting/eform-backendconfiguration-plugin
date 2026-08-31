using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.CalendarChangeNotification;
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
    ICalendarChangeNotifier changeNotifier,
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

            // e. Deploy missing sites. Keep the SDK case id each deploy produced:
            //    step (g) needs a case that belongs to THIS occurrence, and the
            //    schema cannot give it one - nothing on PlanningCase or
            //    PlanningCaseSite records a rotation, so a query by PlanningId
            //    would happily return another week's case (see
            //    EventDeployService.cs:1409-1419). These ids are same-occurrence
            //    by construction.
            var deployedThisPass = new Dictionary<int, int>();

            foreach (var siteId in plan.ToAdd)
            {
                try
                {
                    var ensured = await eventDeployService
                        .EnsureComplianceForOccurrenceAsync(arp, occurrenceDate, siteId, ct)
                        .ConfigureAwait(false);

                    if (ensured is { SdkCaseId: > 0 })
                    {
                        deployedThisPass[siteId] = ensured.SdkCaseId;
                    }
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

            // Track what was ACTUALLY retracted. CaseDelete is an uncaught cloud
            // call, so a timeout unwinds this site before its PlanningCaseSite and
            // PlanningCase are soft-deleted. Step (g) must not hand the shared row
            // to someone else while a case it believes retracted is still live on a
            // worker's device - the next pass derives "actual" from that row, so the
            // stranded case would never be seen again.
            var retractedThisPass = new HashSet<int>();

            foreach (var siteId in plan.ToRemove)
            {
                try
                {
                    if (await RetractSiteForOccurrenceAsync(
                                sdkCore, sdkDbContext, complianceRowsForDate, siteId, ct)
                            .ConfigureAwait(false))
                    {
                        retractedThisPass.Add(siteId);
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e,
                        "Failed to retract occurrence for AreaRulePlanning {ArpId}, planning {PlanningId}, date {Date}, site {SiteId}",
                        areaRulePlanningId, planningId, occurrenceDate, siteId);
                }
            }

            // g. Decide the shared Compliance row's fate, once, after every
            //    removal for this occurrence has been applied. The row belongs to
            //    the OCCURRENCE, not to the worker it happens to name, so the only
            //    question is whether the worker it names still owns it:
            //      completed case    -> untouched (completed cases are immutable)
            //      owner still here  -> untouched
            //      owner gone, alone -> Delete (an unassigned event is inactive)
            //      owner gone, other -> repoint at a case deployed in this pass,
            //                           else release with MicrotingSdkCaseId = 0
            if (complianceRowsForDate is { Count: > 0 })
            {
                foreach (var compliance in complianceRowsForDate)
                {
                    var backing = await sdkDbContext.Cases
                        .Where(x => x.Id == compliance.MicrotingSdkCaseId)
                        .Select(x => new { x.SiteId, x.Status })
                        .FirstOrDefaultAsync(ct)
                        .ConfigureAwait(false);

                    // A completed case is immutable - never retract, repoint or
                    // delete the row that records it.
                    if (backing is { Status: CompletedStatus })
                    {
                        continue;
                    }

                    // The row is fine while it names a worker who is still assigned.
                    if (backing?.SiteId is { } ownerSiteId)
                    {
                        // Still assigned - nothing to do.
                        if (desired.Contains(ownerSiteId))
                        {
                            continue;
                        }

                        // Unassigned, but its retraction did not complete. Leave the
                        // row pointing at it so the next pass retries; repointing now
                        // would orphan a case that is still live on a device.
                        if (!retractedThisPass.Contains(ownerSiteId))
                        {
                            continue;
                        }
                    }

                    if (desired.Count == 0)
                    {
                        // Nobody is left to execute the event. An event with no
                        // assigned worker is inactive, so the occurrence's row goes.
                        await compliance.Delete(backendConfigurationPnDbContext)
                            .ConfigureAwait(false);
                        continue;
                    }

                    // The row's owner is gone - retracted by this pass, or its case
                    // no longer resolves at all - but other workers remain, so the
                    // occurrence is still live and the row must survive. Hand it a
                    // case deployed for THIS occurrence in step (e); 0 if there is
                    // none, which releases the row for redeploy rather than deleting
                    // it - the calendar UI holds complianceId and the stuck-row
                    // branch keys on SdkCaseId == 0 (EventDeployService.cs:1341-1345).
                    var replacementCaseId = deployedThisPass
                        .Where(kv => desired.Contains(kv.Key) && !plan.ToRemove.Contains(kv.Key))
                        .Select(kv => kv.Value)
                        .FirstOrDefault();

                    compliance.MicrotingSdkCaseId = replacementCaseId;
                    await compliance.Update(backendConfigurationPnDbContext).ConfigureAwait(false);
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
    /// via core.CaseDelete, soft-delete the matching PlanningCaseSite(s) and set
    /// the owning PlanningCase to Retracted. Completed cases (Status == 100) are
    /// never touched. The occurrence's shared Compliance row is deliberately NOT
    /// touched here — see the note at the end of the loop, and step (g) of
    /// <see cref="ReconcileEventAsync"/>.
    /// </summary>
    private async Task<bool> RetractSiteForOccurrenceAsync(
        SdkCore sdkCore,
        SdkDbContext sdkDbContext,
        IReadOnlyList<Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.Compliance> complianceList,
        int siteId,
        CancellationToken ct)
    {
        // Compliance rows for this (planning, date) with a backing SDK case
        // are loaded once by the caller and reused across every removed site.
        var retractedAnything = false;

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

            retractedAnything = true;

            // The shared Compliance row's fate is NOT decided here. It belongs to
            // the occurrence, not to this worker, and this method runs once per
            // removed site over a cached, tracked row - mutating it mid-loop made
            // the next iteration's `sdkCase.SiteId != siteId` guard compare the
            // wrong site and silently skip that worker's retraction. Resolved once
            // in ReconcileEventAsync step (g) instead.
        }

        return retractedAnything;
    }

    private readonly record struct CaseStatus(int SiteId, int? Status);
}
