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
/// SDK Case is ANSWERED, i.e. <c>Status == 100 || DoneAt.HasValue</c> — the same
/// predicate the closest analogous destructive path uses before it will touch a
/// case (EventDeployService:1607 and :1776, the eForm swap). The fact that that
/// path spells it with an <c>||</c> is the evidence that "DoneAt set while
/// Status != 100" is a reachable state; in it, a Status-only gate would delete an
/// ANSWERED case and soft-remove its Compliance row, destroying the only DB link
/// between the rotation date and its answer, and DoneByName/DoneAt would stop
/// rendering for that date. Note the direction: DoneAt WIDENS what counts as
/// completed (strictly more preservation on a destructive operation); it never
/// substitutes for the status check, which #1127 forbids.
///
/// TWO ENTRY POINTS, deliberately not merged.
/// <see cref="RetractNonCompletedOccurrencesAsync"/> is occurrence-driven (it
/// walks Compliance rows) and can be bounded by a fromDate, which is what #1122
/// relies on. <see cref="RetractDeployedCasesWithoutComplianceAsync"/> sweeps the
/// deployed PlanningCaseSites that NO Compliance row covers — rows the first
/// method structurally cannot see, and which have no deadline and so cannot be
/// bounded by a fromDate at all. Only the unbounded deactivate paths call the
/// second one. Both judge completion through the one predicate below.
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
    /// Compliance row references an SDK case AND that case is ANSWERED
    /// (<c>Status == 100 || DoneAt.HasValue</c>; the set is built in
    /// LoadCandidatesAsync). A row released back to MicrotingSdkCaseId == 0 by
    /// the reconciliation engine has no backing case and is therefore NOT
    /// completed. <c>Compliance.MicrotingSdkCaseDoneAt</c> is a different,
    /// BC-side column and is still deliberately not consulted — the DoneAt read
    /// here is the SDK <c>Case.DoneAt</c>, exactly as at EventDeployService:1607.
    ///
    /// Both PlanRetractionAsync and RetractNonCompletedOccurrencesAsync call
    /// THIS method — there is no second copy of the predicate to drift from.
    /// </summary>
    private static bool IsCompleted(BcCompliance row, HashSet<int> completedCaseIds) =>
        IsCompleted(row.MicrotingSdkCaseId, completedCaseIds);

    /// <summary>
    /// The same R2 test, expressed over a bare SDK case id, for the orphan sweep
    /// (<see cref="RetractDeployedCasesWithoutComplianceAsync"/>) whose rows are
    /// PlanningCaseSites and therefore have no Compliance row to read the id off.
    /// The Compliance overload above delegates here, so there is exactly ONE
    /// completion test in this file and a change to it reaches every caller.
    /// </summary>
    private static bool IsCompleted(int sdkCaseId, HashSet<int> completedCaseIds) =>
        sdkCaseId > 0 && completedCaseIds.Contains(sdkCaseId);

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
            (completedCaseIds, microtingUidByCaseId) =
                await LoadCaseFactsAsync(sdkCore, caseIds, ct).ConfigureAwait(false);
        }

        return new RetractionCandidates(rows, completedCaseIds, microtingUidByCaseId, sdkCore);
    }

    /// <summary>
    /// The single batched SDK read both passes use: for a set of case ids, which
    /// of them are ANSWERED and what MicrotingUid does CaseDelete need. One query
    /// for the whole batch, never one per row.
    ///
    /// This method is the ONLY place the answered test
    /// (<c>Status == 100 || DoneAt.HasValue</c>) is written. The occurrence pass
    /// and the orphan sweep both build their completed-set from here, so they
    /// cannot drift apart — a second copy of this predicate is exactly the class
    /// of bug #1123 is about.
    /// </summary>
    private static async Task<(HashSet<int> CompletedCaseIds, Dictionary<int, int?> MicrotingUidByCaseId)>
        LoadCaseFactsAsync(SdkCore sdkCore, List<int> caseIds, CancellationToken ct)
    {
        var completedCaseIds = new HashSet<int>();
        var microtingUidByCaseId = new Dictionary<int, int?>();

        await using var sdkDbContext = sdkCore.DbContextHelper.GetDbContext();
        var sdkCases = await sdkDbContext.Cases
            .Where(c => caseIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Status, c.MicrotingUid, c.DoneAt })
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var sdkCase in sdkCases)
        {
            microtingUidByCaseId[sdkCase.Id] = sdkCase.MicrotingUid;
            // Status == 100 OR DoneAt set — verbatim the guard
            // EventDeployService:1607 applies before it will replace a case's
            // eForm. See the R2 note on the class for why the OR is not
            // belt-and-braces: an answered case can carry DoneAt while its
            // Status has not (yet) reached 100, and retracting it would
            // destroy the answer.
            if (sdkCase.Status == CompletedStatus || sdkCase.DoneAt.HasValue)
            {
                completedCaseIds.Add(sdkCase.Id);
            }
        }

        return (completedCaseIds, microtingUidByCaseId);
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
            await RetractPlanningCaseSiteAsync(planningCaseSite, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The local half of retracting ONE PlanningCaseSite, shared by the
    /// occurrence pass (which finds its rows by SDK case id) and the orphan sweep
    /// (which already holds the entity). Extracted rather than copied so the
    /// "only retract the parent PlanningCase when no live sibling site remains"
    /// rule exists once.
    /// </summary>
    private async Task RetractPlanningCaseSiteAsync(
        Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningCaseSite planningCaseSite,
        CancellationToken ct)
    {
        var planningCase = await itemsPlanningPnDbContext.PlanningCases
            .Where(x => x.Id == planningCaseSite.PlanningCaseId
                        && x.WorkflowState != Constants.WorkflowStates.Removed)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        await planningCaseSite.Delete(itemsPlanningPnDbContext).ConfigureAwait(false);

        if (planningCase == null)
        {
            return;
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

    /// <inheritdoc />
    public async Task<OccurrenceRetractionResult> RetractDeployedCasesWithoutComplianceAsync(
        AreaRulePlanning arp,
        CancellationToken ct = default)
    {
        if (arp == null)
        {
            return new OccurrenceRetractionResult(0, 0, 0);
        }

        var planningId = arp.ItemPlanningId;

        // The row selection is lifted VERBATIM from the pre-#1123 DeactivateList
        // (see `git show stable:...BackendConfigurationTaskWizardService.cs`):
        //   PlanningCases where PlanningId == planning.Id and WorkflowState != Removed
        //   -> PlanningCaseSites where PlanningCaseId == that
        //      and (MicrotingSdkCaseId != 0 || MicrotingCheckListSitId != 0)
        //      and WorkflowState != Removed
        // That part of the old code was right — it is exactly "deployed to a
        // device and not already pulled". Only its missing completion guard was
        // wrong, and that is added below.
        var livePlanningCaseIds = itemsPlanningPnDbContext.PlanningCases
            .Where(x => x.PlanningId == planningId
                        && x.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(x => x.Id);

        var candidates = await itemsPlanningPnDbContext.PlanningCaseSites
            .Where(x => livePlanningCaseIds.Contains(x.PlanningCaseId))
            .Where(x => x.MicrotingSdkCaseId != 0 || x.MicrotingCheckListSitId != 0)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync(ct).ConfigureAwait(false);

        if (candidates.Count == 0)
        {
            return new OccurrenceRetractionResult(0, 0, 0);
        }

        // NO-DOUBLE-HANDLING GATE. Anything a Compliance row of this planning
        // points at belongs to RetractNonCompletedOccurrencesAsync, not here.
        // WorkflowState is deliberately NOT filtered: a REMOVED Compliance row
        // means that pass already retracted the occurrence (and soft-deleted this
        // very PlanningCaseSite, so it would not be in `candidates` anyway), while
        // a LIVE one means it either preserved it as completed or is about to
        // retract it. Either way this sweep must keep its hands off. What is left
        // is precisely the orphans: deployed rows no Compliance row ever covered.
        var complianceCaseIds = (await backendConfigurationPnDbContext.Compliances
                .AsNoTracking()
                .Where(x => x.PlanningId == planningId && x.MicrotingSdkCaseId > 0)
                .Select(x => x.MicrotingSdkCaseId)
                .Distinct()
                .ToListAsync(ct).ConfigureAwait(false))
            .ToHashSet();

        var orphans = candidates
            .Where(x => x.MicrotingSdkCaseId == 0 || !complianceCaseIds.Contains(x.MicrotingSdkCaseId))
            .ToList();

        if (orphans.Count == 0)
        {
            return new OccurrenceRetractionResult(0, 0, 0);
        }

        var sdkCore = await coreHelper.GetCore().ConfigureAwait(false);

        // ONE batched SDK read for the whole sweep — same helper, same answered
        // predicate, as the occurrence pass. There is no Compliance row here and
        // therefore no deadline, so completion can only be judged from the case
        // itself; that is exactly what LoadCaseFactsAsync reports.
        var caseIds = orphans
            .Where(x => x.MicrotingSdkCaseId > 0)
            .Select(x => x.MicrotingSdkCaseId)
            .Distinct()
            .ToList();

        var completedCaseIds = new HashSet<int>();
        var microtingUidByCaseId = new Dictionary<int, int?>();
        if (caseIds.Count > 0)
        {
            (completedCaseIds, microtingUidByCaseId) =
                await LoadCaseFactsAsync(sdkCore, caseIds, ct).ConfigureAwait(false);
        }

        // The old code's fallback for a row that was handed out as a CheckListSite
        // but has no Case row yet. Batched here for the same reason as above; the
        // old loop issued one SingleAsync per row and threw if it found nothing.
        var checkListSiteUidById = new Dictionary<int, int>();
        var checkListSiteIds = orphans
            .Where(x => x.MicrotingSdkCaseId == 0 && x.MicrotingCheckListSitId != 0)
            .Select(x => x.MicrotingCheckListSitId)
            .Distinct()
            .ToList();

        if (checkListSiteIds.Count > 0)
        {
            await using var sdkDbContext = sdkCore.DbContextHelper.GetDbContext();
            checkListSiteUidById = await sdkDbContext.CheckListSites
                .Where(x => checkListSiteIds.Contains(x.Id))
                .Select(x => new { x.Id, x.MicrotingUid })
                .ToDictionaryAsync(x => x.Id, x => x.MicrotingUid, ct)
                .ConfigureAwait(false);
        }

        var retracted = 0;
        var completedPreserved = 0;
        var failed = 0;

        foreach (var row in orphans)
        {
            // Snapshot before anything can mutate the entity.
            var sdkCaseId = row.MicrotingSdkCaseId;

            // R2, via the SAME predicate the occurrence pass uses. A row with no
            // SDK case at all (CheckListSite only) cannot have been answered —
            // there is nothing to carry a Status or a DoneAt — so it falls through
            // and is retracted, which is also what the old code did with it.
            if (IsCompleted(sdkCaseId, completedCaseIds))
            {
                completedPreserved++;
                continue;
            }

            try
            {
                // CaseDelete FIRST, exactly as in the occurrence pass: if the cloud
                // call throws, the catch leaves the PlanningCaseSite alive so a
                // re-run finds it again, rather than soft-deleting the only local
                // record of a case that is still live on a worker's device.
                if (sdkCaseId > 0)
                {
                    if (microtingUidByCaseId.TryGetValue(sdkCaseId, out var microtingUid)
                        && microtingUid != null)
                    {
                        await sdkCore.CaseDelete((int)microtingUid).ConfigureAwait(false);
                    }
                }
                else if (checkListSiteUidById.TryGetValue(row.MicrotingCheckListSitId, out var clUid))
                {
                    await sdkCore.CaseDelete(clUid).ConfigureAwait(false);
                }

                await RetractPlanningCaseSiteAsync(row, ct).ConfigureAwait(false);
                retracted++;
            }
            catch (Exception e)
            {
                failed++;
                logger.LogError(e,
                    "Failed to retract orphan PlanningCaseSite {PlanningCaseSiteId} (case {SdkCaseId}) for AreaRulePlanning {ArpId}, planning {PlanningId}",
                    row.Id, sdkCaseId, arp.Id, planningId);
            }
        }

        logger.LogInformation(
            "Swept {Retracted} deployed case(s) without a Compliance row for AreaRulePlanning {ArpId}; {Completed} completed preserved, {Failed} failed",
            retracted, arp.Id, completedPreserved, failed);

        return new OccurrenceRetractionResult(retracted, completedPreserved, failed);
    }
}
