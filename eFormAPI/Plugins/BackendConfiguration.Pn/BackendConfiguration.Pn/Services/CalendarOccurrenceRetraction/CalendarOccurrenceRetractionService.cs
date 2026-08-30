using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using BcCompliance = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.Compliance;
using SdkCore = eFormCore.Core;

namespace BackendConfiguration.Pn.Services.CalendarOccurrenceRetraction;

/// <summary>
/// Retracts a calendar event's deployed-but-NOT-completed occurrences.
///
/// Shared by two callers that both need "wipe the open occurrences, keep the
/// answered history":
///   * #1122 — re-anchoring a series' start date to a different recurrence
///     period (or into the past). Relocating within the period
///     (RelocateNonCompletedComplianceRowsToNewPattern, the #960 fix) is
///     meaningless once the period grid itself shifts, so those occurrences are
///     retracted and the new pattern is deployed fresh.
///   * #1123 — deactivating a task.
///
/// It lives in its own service, NOT on BackendConfigurationCalendarService,
/// because that class is request-shaped: it takes IUserService and
/// IBackendConfigurationLocalizationService, neither of which resolves outside
/// an HTTP request. Retraction needs none of that — only the three DbContexts
/// and the SDK core — so keeping it separate lets a background/batch caller use
/// it without dragging a controller's dependency graph along. Same shape and
/// same deps as CalendarAssignmentReconciliationService, deliberately.
///
/// INVARIANT R2 — completed occurrences are immutable. An occurrence is
/// completed iff its Compliance row has MicrotingSdkCaseId &gt; 0 AND the backing
/// SDK Case.Status == 100. That pair is the ONLY gate: MicrotingSdkCaseDoneAt is
/// read elsewhere for other purposes and is not it. Soft-removing a completed
/// occurrence's Compliance row would destroy the only DB link between the
/// rotation date and its answered SDK case, and DoneByName/DoneAt would stop
/// rendering for that date.
///
/// NOT handled here, by design: active CalendarOccurrenceException rows. This
/// service answers "which deployed occurrences are open", nothing about
/// per-occurrence overrides — the caller that moves an anchor owns purging its
/// own stale exceptions.
/// </summary>
public class CalendarOccurrenceRetractionService(
    BackendConfigurationPnDbContext backendConfigurationPnDbContext,
    ItemsPlanningPnDbContext itemsPlanningPnDbContext,
    IEFormCoreService coreHelper,
    ILogger<CalendarOccurrenceRetractionService> logger)
    : ICalendarOccurrenceRetractionService
{
    private const int CompletedStatus = 100;

    public async Task<OccurrenceRetractionResult> PlanRetractionAsync(
        AreaRulePlanning arp,
        DateTime? fromDate = null,
        CancellationToken ct = default)
    {
        if (arp == null)
        {
            return new OccurrenceRetractionResult(0, 0, 0);
        }

        // tracked: false — a preview must not leave Compliance entities in the
        // request's change tracker for some later SaveChanges to flush. That is
        // the ONLY difference from the write path: same query, same fromDate
        // comparison, same SDK batch lookup, same IsCompleted predicate below.
        var candidates = await LoadCandidatesAsync(arp, fromDate, tracked: false, ct)
            .ConfigureAwait(false);

        // Counted over ROWS, never over distinct Deadline dates. Compliance has
        // no site column, so an occurrence deployed to two workers is two rows
        // with the same day — and they can disagree: worker A answered, worker B
        // did not. The write loop below then retracts B's row and preserves A's,
        // so a date-based count would report one occurrence where the apply
        // touches one and skips one. Both numbers come off the same list here.
        var completedPreserved = candidates.Rows.Count(
            r => IsCompleted(r, candidates.CompletedCaseIds));

        return new OccurrenceRetractionResult(
            Retracted: candidates.Rows.Count - completedPreserved,
            CompletedPreserved: completedPreserved,
            // A projection cannot know which cloud CaseDelete will throw, so it
            // reports the optimistic split. Failures show up in the apply's own
            // result, which is what the user sees afterwards.
            Failed: 0);
    }

    public async Task<OccurrenceRetractionResult> RetractNonCompletedOccurrencesAsync(
        AreaRulePlanning arp,
        DateTime? fromDate = null,
        CancellationToken ct = default)
    {
        if (arp == null)
        {
            return new OccurrenceRetractionResult(0, 0, 0);
        }

        var planningId = arp.ItemPlanningId;

        var candidates = await LoadCandidatesAsync(arp, fromDate, tracked: true, ct)
            .ConfigureAwait(false);
        var rows = candidates.Rows;
        if (rows.Count == 0)
        {
            return new OccurrenceRetractionResult(0, 0, 0);
        }

        var sdkCore = candidates.SdkCore;
        var completedCaseIds = candidates.CompletedCaseIds;
        var microtingUidByCaseId = candidates.MicrotingUidByCaseId;

        var retracted = 0;
        var completedPreserved = 0;
        var failed = 0;

        foreach (var row in rows)
        {
            // Snapshot before anything can mutate the entity.
            var sdkCaseId = row.MicrotingSdkCaseId;

            // R2 — completed occurrence: leave the case, the PlanningCase(Site)
            // and the Compliance row exactly as they are.
            if (IsCompleted(row, completedCaseIds))
            {
                completedPreserved++;
                continue;
            }

            try
            {
                if (sdkCaseId > 0)
                {
                    // CaseDelete is an uncaught cloud call. Do it FIRST: if it
                    // throws, the catch below leaves the Compliance row alive so
                    // a re-run finds the occurrence again, rather than orphaning
                    // a case that is still live on a worker's device.
                    if (microtingUidByCaseId.TryGetValue(sdkCaseId, out var microtingUid)
                        && microtingUid != null)
                    {
                        await sdkCore.CaseDelete((int)microtingUid).ConfigureAwait(false);
                    }

                    await RetractPlanningCaseSitesAsync(sdkCaseId, ct).ConfigureAwait(false);
                }

                await row.Delete(backendConfigurationPnDbContext).ConfigureAwait(false);
                retracted++;
            }
            catch (Exception e)
            {
                failed++;
                logger.LogError(e,
                    "Failed to retract occurrence for AreaRulePlanning {ArpId}, planning {PlanningId}, deadline {Deadline}, compliance {ComplianceId}",
                    arp.Id, planningId, row.Deadline, row.Id);
            }
        }

        logger.LogInformation(
            "Retracted {Retracted} occurrence(s) for AreaRulePlanning {ArpId} from {FromDate}; {Completed} completed preserved, {Failed} failed",
            retracted, arp.Id, fromDate?.ToString("yyyy-MM-dd") ?? "series start", completedPreserved, failed);

        return new OccurrenceRetractionResult(retracted, completedPreserved, failed);
    }

    /// <summary>
    /// Everything both the write path and the read-only projection need about
    /// one series' in-scope occurrences, resolved in exactly one place so the
    /// two can never enumerate a different set or judge completion differently.
    /// </summary>
    private sealed record RetractionCandidates(
        List<BcCompliance> Rows,
        HashSet<int> CompletedCaseIds,
        Dictionary<int, int?> MicrotingUidByCaseId,
        /// <summary>Null when there are no rows — see LoadCandidatesAsync.</summary>
        SdkCore SdkCore);

    /// <summary>
    /// INVARIANT R2, expressed once. An occurrence is completed iff its
    /// Compliance row references an SDK case AND that case is at status 100.
    /// A row released back to MicrotingSdkCaseId == 0 by the reconciliation
    /// engine has no backing case and is therefore NOT completed;
    /// MicrotingSdkCaseDoneAt is deliberately not consulted.
    ///
    /// Both PlanRetractionAsync and RetractNonCompletedOccurrencesAsync call
    /// THIS method — there is no second copy of the predicate to drift from.
    /// </summary>
    private static bool IsCompleted(BcCompliance row, HashSet<int> completedCaseIds) =>
        row.MicrotingSdkCaseId > 0 && completedCaseIds.Contains(row.MicrotingSdkCaseId);

    private async Task<RetractionCandidates> LoadCandidatesAsync(
        AreaRulePlanning arp, DateTime? fromDate, bool tracked, CancellationToken ct)
    {
        var planningId = arp.ItemPlanningId;

        // Deployed rows carry MicrotingSdkCaseId > 0; rows released back to 0 by
        // the reconciliation engine are NOT deployed but still represent a live
        // occurrence the calendar renders, so they are in scope too — they just
        // have no SDK case to pull.
        var query = backendConfigurationPnDbContext.Compliances
            .Where(x => x.PlanningId == planningId
                        && x.WorkflowState != Constants.WorkflowStates.Removed);

        if (!tracked)
        {
            query = query.AsNoTracking();
        }

        if (fromDate.HasValue)
        {
            // Midnight of fromDate, so the whole of that day is included without
            // needing Deadline.Date on the server side.
            var from = fromDate.Value.Date;
            query = query.Where(x => x.Deadline >= from);
        }

        var rows = await query.ToListAsync(ct).ConfigureAwait(false);

        var completedCaseIds = new HashSet<int>();
        var microtingUidByCaseId = new Dictionary<int, int?>();

        if (rows.Count == 0)
        {
            // No occurrences, so no SDK work and — deliberately — no GetCore()
            // call: the write path early-returns on an empty row set and never
            // dereferences SdkCore, and the projection never uses it at all.
            return new RetractionCandidates(rows, completedCaseIds, microtingUidByCaseId, null);
        }

        var sdkCore = await coreHelper.GetCore().ConfigureAwait(false);

        // ONE SDK query for the whole batch, not one per row (the shape
        // RelocateNonCompletedComplianceRowsToNewPattern uses). Everything the
        // write loop needs from the SDK — completion status AND the MicrotingUid
        // that CaseDelete takes — is projected here, so that loop never touches
        // sdkDbContext again.
        //
        // Resolving it all UP FRONT is also what keeps the write loop clear of
        // the bug CalendarAssignmentReconciliationService hit: there, the same
        // tracked Compliance rows were walked once per removed SITE, and
        // mutating a row mid-loop made the next iteration's
        // `sdkCase.SiteId != siteId` guard compare the wrong site. Here every row
        // is visited exactly once, no SDK fact is re-read after a mutation, and
        // there is no site guard at all — this service removes whole occurrences
        // rather than one worker's slot.
        var caseIds = rows
            .Where(r => r.MicrotingSdkCaseId > 0)
            .Select(r => r.MicrotingSdkCaseId)
            .Distinct()
            .ToList();

        if (caseIds.Count > 0)
        {
            await using var sdkDbContext = sdkCore.DbContextHelper.GetDbContext();
            var sdkCases = await sdkDbContext.Cases
                .Where(c => caseIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Status, c.MicrotingUid })
                .ToListAsync(ct).ConfigureAwait(false);

            foreach (var sdkCase in sdkCases)
            {
                microtingUidByCaseId[sdkCase.Id] = sdkCase.MicrotingUid;
                if (sdkCase.Status == CompletedStatus)
                {
                    completedCaseIds.Add(sdkCase.Id);
                }
            }
        }

        return new RetractionCandidates(rows, completedCaseIds, microtingUidByCaseId, sdkCore);
    }

    /// <summary>
    /// Soft-deletes the PlanningCaseSite(s) backing one SDK case and retracts
    /// their owning PlanningCase when no live sibling site remains. Mirrors
    /// CalendarAssignmentReconciliationService.RetractSiteForOccurrenceAsync —
    /// in the calendar deploy path PlanningCase is 1:1 per site, but a shared
    /// PlanningCase with other live sites must survive.
    /// </summary>
    private async Task RetractPlanningCaseSitesAsync(int sdkCaseId, CancellationToken ct)
    {
        var planningCaseSites = await itemsPlanningPnDbContext.PlanningCaseSites
            .Where(x => x.MicrotingSdkCaseId == sdkCaseId
                        && x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var planningCaseSite in planningCaseSites)
        {
            var planningCase = await itemsPlanningPnDbContext.PlanningCases
                .Where(x => x.Id == planningCaseSite.PlanningCaseId
                            && x.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);

            await planningCaseSite.Delete(itemsPlanningPnDbContext).ConfigureAwait(false);

            if (planningCase == null)
            {
                continue;
            }

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
}
