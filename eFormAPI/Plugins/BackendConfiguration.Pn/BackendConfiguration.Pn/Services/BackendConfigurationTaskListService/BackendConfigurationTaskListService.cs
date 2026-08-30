using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Enums;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Infrastructure.Models.TaskList;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.CalendarOccurrenceRetraction;
using BackendConfiguration.Pn.Services.CalendarPastSeriesBackfill;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Models;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using CalendarService =
    BackendConfiguration.Pn.Services.BackendConfigurationCalendarService.BackendConfigurationCalendarService;
using BackfillService =
    BackendConfiguration.Pn.Services.CalendarPastSeriesBackfill.CalendarPastSeriesBackfillService;

namespace BackendConfiguration.Pn.Services.BackendConfigurationTaskListService;

public class BackendConfigurationTaskListService(
    IBackendConfigurationLocalizationService localizationService,
    IUserService userService,
    BackendConfigurationPnDbContext backendConfigurationPnDbContext,
    ItemsPlanningPnDbContext itemsPlanningPnDbContext,
    IBackendConfigurationCalendarService calendarService,
    IBackendConfigurationTaskWizardService taskWizardService,
    ICalendarOccurrenceRetractionService occurrenceRetractionService,
    ICalendarPastSeriesBackfillService pastSeriesBackfillService,
    ILogger<BackendConfigurationTaskListService> logger)
    : IBackendConfigurationTaskListService
{
    public async Task<OperationResult> Assign(TaskListBatchAssignModel model) =>
        await RunPerTask(model.TaskIds, async id =>
        {
            var update = await BuildUpdateModel(id);
            if (update == null) return (false, "Task not found");
            update.Sites = [model.SiteId];
            var result = await calendarService.UpdateTask(update);
            return (result.Success, result.Message);
        }, "Tasks updated");

    public async Task<OperationResult> Reassign(TaskListBatchReassignModel model)
    {
        // Spec: "the result reports which tasks were moved" — track moved
        // (actually reassigned) vs skipped (not assigned to "from", so left
        // untouched) counts alongside RunPerTask/Aggregate's own ok/error
        // bookkeeping. RunPerTask runs tasks sequentially, so plain closures
        // are safe here (no concurrent increments).
        var moved = 0;
        var skipped = 0;
        var result = await RunPerTask(model.TaskIds, async id =>
        {
            var update = await BuildUpdateModel(id);
            if (update == null) return (false, "Task not found");
            if (!update.Sites.Contains(model.FromSiteId))
            {
                // Not assigned to "from" — spec: skip silently, not an error.
                skipped++;
                return (true, null);
            }
            update.Sites = update.Sites
                .Where(s => s != model.FromSiteId)
                .Append(model.ToSiteId)
                .Distinct()
                .ToList();
            var updateResult = await calendarService.UpdateTask(update);
            if (updateResult.Success)
            {
                moved++;
            }
            return (updateResult.Success, updateResult.Message);
        }, "Tasks updated");

        // Every task ended up either moved or skipped only when there were
        // zero failures (an explicit "Task not found"/UpdateTask failure, or
        // an exception caught by RunPerTask, increments neither counter).
        // Leave Aggregate's partial-/full-failure message untouched in that
        // case — only the clean-success path gets the moved/skipped summary
        // appended, per the least-invasive Reassign-local wrapper approach.
        if (moved + skipped != model.TaskIds.Count)
        {
            return result;
        }
        return new OperationResult(result.Success, $"{result.Message} (moved {moved}, skipped {skipped})");
    }

    public async Task<OperationResult> AddWorker(TaskListBatchAssignModel model) =>
        await RunPerTask(model.TaskIds, async id =>
        {
            var update = await BuildUpdateModel(id);
            if (update == null) return (false, "Task not found");
            if (update.Sites.Contains(model.SiteId))
            {
                // Already assigned — dedup no-op, still counts as success.
                return (true, null);
            }
            update.Sites = update.Sites.Append(model.SiteId).ToList();
            var result = await calendarService.UpdateTask(update);
            return (result.Success, result.Message);
        }, "Tasks updated");

    public async Task<OperationResult> ChangeEform(TaskListBatchChangeEformModel model) =>
        await RunPerTask(model.TaskIds, async id =>
        {
            var update = await BuildUpdateModel(id);
            if (update == null) return (false, "Task not found");
            update.EformId = model.EformId;
            var result = await calendarService.UpdateTask(update);
            return (result.Success, result.Message);
        }, "Tasks updated");

    public async Task<OperationResult> AddTags(TaskListBatchTagsModel model) =>
        await RunPerTask(model.TaskIds, async id =>
        {
            var update = await BuildUpdateModel(id);
            if (update == null) return (false, "Task not found");
            update.TagIds = update.TagIds.Union(model.TagIds).ToList();
            var result = await calendarService.UpdateTask(update);
            return (result.Success, result.Message);
        }, "Tasks updated");

    public async Task<OperationResult> RemoveTags(TaskListBatchTagsModel model) =>
        await RunPerTask(model.TaskIds, async id =>
        {
            var update = await BuildUpdateModel(id);
            if (update == null) return (false, "Task not found");
            update.TagIds = update.TagIds.Except(model.TagIds).ToList();
            var result = await calendarService.UpdateTask(update);
            return (result.Success, result.Message);
        }, "Tasks updated");

    // Sets AreaRulePlanning.ComplianceEnabled (and, downstream, the
    // template-level AreaRule.ComplianceEnabled — the wizard writes both,
    // BackendConfigurationTaskWizardService.UpdateTask:812,865) on every
    // selected task. Same RunPerTask/BuildUpdateModel shape as ChangeEform.
    //
    // Deliberately does NOT touch Status, unlike the single-task calendar
    // modal, whose onPickOverdueShown/onPickOverdueHidden handlers both force
    // statusControl to true. An admin flipping compliance on 40 rows does not
    // intend to silently reactivate dormant tasks and redeploy their cases;
    // batch activation is its own action. BuildUpdateModel round-trips the
    // planning's current Status, so an inactive task stays inactive.
    //
    // Deliberately does NOT eagerly clean up already-overdue Compliance rows
    // either: the calendar path this page follows persists the flag and
    // nothing else, and the effect appears on the next scheduled pass. (The
    // older Property-Areas edit path in
    // BackendConfigurationAreaRulePlanningsServiceHelper additionally deletes
    // all Compliance rows inline and recomputes Property.ComplianceStatus —
    // that is not the path this page uses.)
    public async Task<OperationResult> SetCompliance(TaskListBatchComplianceModel model) =>
        await RunPerTask(model.TaskIds, async id =>
        {
            var update = await BuildUpdateModel(id);
            if (update == null) return (false, "Task not found");
            update.ComplianceEnabled = model.ComplianceEnabled;
            var result = await calendarService.UpdateTask(update);
            return (result.Success, result.Message);
        }, "Tasks updated");

    // ------------------------------------------------------------------
    // #1122 — change start date
    // ------------------------------------------------------------------

    // Re-anchors every selected series to model.StartDate, forwards or
    // BACKWARDS. Same RunPerTask/BuildUpdateModel/UpdateTask shape as
    // ChangeEform, with three deliberate overrides on the built model:
    //
    //  * StartDate — BuildUpdateModel synthesizes a fake "nearest future
    //    same-weekday" anchor (see its comment). That synthetic value exists so
    //    worker/eForm/tag batches never move a series; this action's ENTIRE
    //    purpose is to move it, so the user's date replaces it. Without this
    //    override the picked date is silently discarded.
    //
    //  * OriginalDate = null — forces dateChanged = true in UpdateTask. Leaving
    //    it equal to StartDate makes UpdateTask treat the edit as "occurrence
    //    not moved", re-fetch the CURRENT anchor from the DB and hand THAT to
    //    the wizard, again discarding the user's date. Null cannot NRE: the only
    //    DateTime.Parse of OriginalDate is guarded by IsNullOrEmpty. It is NOT
    //    scope-neutral though — UpdateTask's this/thisAndFollowing dispatch is
    //    `scope == "..." && !IsNullOrEmpty(OriginalDate)`, so a null OriginalDate
    //    makes both branches fall through to "all". Benign here because "all" is
    //    exactly what we set, but it means the two overrides below are not
    //    independent.
    //
    //  * Scope = "all" — re-anchor the whole series, not one occurrence.
    //
    // Downstream, UpdateTask's #1122 §3 gate decides relocate vs retract. Its
    // comparison basis is `originalOccurrenceDate ?? previousStartDate`; with
    // OriginalDate null the first is null, so it falls back to the series'
    // PREVIOUS anchor — which is precisely the right question for a batch
    // re-anchor ("did the series move to another period?").
    //
    // Weekday re-anchoring is INTENDED here. For RepeatType == Week or
    // RepeatOrdinalWeek.HasValue, UpdateTask writes
    // `arp.DayOfWeek = updateModel.StartDate.DayOfWeek` verbatim. That is why
    // BuildUpdateModel goes to the trouble of picking a same-weekday synthetic
    // anchor for the other batch actions. Moving a weekly task's start date to a
    // Thursday SHOULD make it recur on Thursdays.
    public async Task<OperationResult> ChangeStartDate(TaskListBatchStartDateModel model)
    {
        var invalidStartDate = ValidateStartDate(model);
        if (invalidStartDate != null)
        {
            return invalidStartDate;
        }

        return await RunPerTask(model.TaskIds, async id =>
        {
            var update = await BuildUpdateModel(id);
            if (update == null) return (false, "Task not found");
            update.StartDate = model.StartDate;
            update.OriginalDate = null;
            update.Scope = "all";
            var result = await calendarService.UpdateTask(update);
            return (result.Success, result.Message);
        }, "Tasks updated");
    }

    // The ONLY batch-wide, pre-loop guard this action admits, mirroring Copy's
    // "validate once so a failure never produces a partial batch" shape.
    //
    // The action's whole input is a single DateTime, and the issue is explicit
    // that there is NO cap on how far back it may go ("Large past range […] No
    // cap — the preview surfaces the magnitude before the admin commits"), so
    // there is deliberately no range check here. What IS worth rejecting is the
    // unset sentinel: an absent, null or unparsable `startDate` field
    // deserialises to default(DateTime) == 0001-01-01, which is not a date any
    // picker can produce, yet would re-anchor the series to year 1 and make the
    // backfill enumerate two millennia of occurrences x sites synchronously.
    // Everything else the user can pick is, by design, legal.
    private OperationResult ValidateStartDate(TaskListBatchStartDateModel model) =>
        model.StartDate == default
            ? new OperationResult(false, localizationService.GetString("StartDateIsRequired"))
            : null;

    // Read-only projection of what ChangeStartDate would do to the selected
    // tasks (#1122 §5). Writes NOTHING: every query below is AsNoTracking, the
    // retraction projection is AsNoTracking, and the backfill plan is documented
    // as a pure read.
    //
    // It must agree with the apply on THREE separate things, and each is handled
    // by calling the apply's own code rather than by re-deriving it:
    //
    //  1. WHICH ANCHOR gets persisted — CalendarService.NormalizeStartDateToLocalDay,
    //     the same rounding UpdateTask applies to the incoming StartDate. (The
    //     wizard's own `Hour != 0` round-up can then never fire, because the
    //     normalised value is always midnight.)
    //  2. WHICH BRANCH the apply takes — CalendarService.IsSameRecurrencePeriod,
    //     fed the POST-save pattern via BackfillService.ApplyProspectiveAnchor,
    //     because the apply evaluates that gate on the already-written entities.
    //     A task that stays in the same period AND in the future relocates
    //     instead of retracting, and relocation retracts nothing and backfills
    //     nothing — so it contributes only to TaskCount.
    //  3. HOW MANY ROWS each branch touches — PlanRetractionAsync and
    //     PlanPastSeriesBackfillAsync, which are the read halves of the very
    //     methods the apply calls.
    //
    // Ineligible ids (missing, not CreatedInGuide, anchorless, no planning) are
    // skipped rather than counted: the apply reports them as per-task failures
    // and changes nothing for them, so counting them would over-promise.
    public async Task<OperationDataResult<TaskListBatchStartDatePreviewModel>> ChangeStartDatePreview(
        TaskListBatchStartDateModel model)
    {
        var invalidStartDate = ValidateStartDate(model);
        if (invalidStartDate != null)
        {
            return new OperationDataResult<TaskListBatchStartDatePreviewModel>(
                false, invalidStartDate.Message);
        }

        var newAnchor = CalendarService.NormalizeStartDateToLocalDay(model.StartDate).Date;
        var today = DateTime.UtcNow.Date;
        var preview = new TaskListBatchStartDatePreviewModel();

        // Distinct: a selection is a set. The apply would run a repeated id
        // twice, but the second pass finds the occurrences already retracted and
        // the overdue rows already present (both operations are idempotent), so
        // counting a duplicate twice would report work that never happens.
        foreach (var id in model.TaskIds.Distinct())
        {
            var arp = await backendConfigurationPnDbContext.AreaRulePlannings
                .AsNoTracking()
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync(x => x.Id == id);
            // arp.StartDate == null makes BuildUpdateModel throw, so the apply
            // fails this task without touching anything.
            if (arp?.StartDate == null) continue;

            // Same eligibility rule as BuildUpdateModel / IsEligibleTaskAsync.
            var rule = await backendConfigurationPnDbContext.AreaRules
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == arp.AreaRuleId);
            if (rule is not { CreatedInGuide: true }) continue;

            // AsNoTracking is load-bearing: ApplyProspectiveAnchor MUTATES this
            // planning to model the post-save pattern, and a tracked entity would
            // carry that speculative anchor into the next SaveChanges on the
            // shared request DbContext.
            var planning = await itemsPlanningPnDbContext.Plannings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == arp.ItemPlanningId
                                          && x.WorkflowState != Constants.WorkflowStates.Removed);
            if (planning == null) continue;

            preview.TaskCount++;

            var previousAnchor = arp.StartDate.Value.Date;
            var effectiveArp = BackfillService.ApplyProspectiveAnchor(planning, arp, newAnchor);
            var anchorIsInThePast = newAnchor < today;
            var stillInSamePeriod = CalendarService.IsSameRecurrencePeriod(
                planning, effectiveArp, previousAnchor, newAnchor);

            if (stillInSamePeriod && !anchorIsInThePast)
            {
                // Relocate branch: Compliance deadlines are moved within their
                // own periods, nothing is retracted and nothing is backfilled.
                continue;
            }

            // Whole series, no fromDate — exactly the call the retract branch makes.
            var retraction = await occurrenceRetractionService.PlanRetractionAsync(arp);
            preview.OccurrencesToRetract += retraction.Retracted;
            preview.CompletedPreserved += retraction.CompletedPreserved;

            // OverdueToCreate is already 0 for a future anchor (no past
            // occurrences) and 0 when compliance is off, so this one call covers
            // the whole of the issue's compliance ON/OFF rule.
            var plan = await pastSeriesBackfillService
                .PlanPastSeriesBackfillAsync(arp, newAnchor);
            preview.OverdueToCreate += plan.OverdueToCreate;
        }

        return new OperationDataResult<TaskListBatchStartDatePreviewModel>(true, preview);
    }

    // Copy creates a brand-new AreaRulePlanning on the target property/board
    // via calendarService.CreateTask, seeded from the source task's full
    // current state (BuildUpdateModel). Two fields are deliberately NOT a
    // verbatim round-trip of the source, both verified against
    // BackendConfigurationCalendarService.CreateTask's real body:
    //
    //  - FolderId is left null. CreateTask auto-resolves (and creates if
    //    missing) the target property's "00. Logbøger" folder whenever
    //    FolderId is null/0 (ResolveOrCreateLogbøgerFolderAsync), so the
    //    source's FolderId — which belongs to a different property — must
    //    never be carried across. Matches how the Angular calendar
    //    create-event modal itself resolves FolderId (best-effort by name,
    //    falls back to null and lets the backend own the default).
    //
    //  - Sites is set to the caller's explicit target-property assignee
    //    (model.SiteId), and WorkerTagIds is always cleared ([]). The copy
    //    dialog requires choosing a worker (site) from the TARGET property
    //    before submitting, so that explicit assignee — not a carried-over
    //    source worker-tag — is what satisfies CreateTask's hard
    //    "AtLeastOneWorkerMustBeAssigned" guard for every task. Source
    //    Sites (per-property worker identities) never carried across
    //    properties even before this change; source WorkerTagIds no longer
    //    carry forward either, since the explicit assignee makes that
    //    fallback unnecessary.
    public async Task<OperationResult> Copy(TaskListBatchCopyModel model)
    {
        // Defense-in-depth (mirrors EventDeployService's PropertyWorkers
        // guard): the copy dialog's site picker is scoped to the target
        // property's workers client-side, but the API must not trust that.
        // model.SiteId ends up as CalendarTaskCreateRequestModel.Sites,
        // which CreateTask persists verbatim with no cross-check against
        // model.TargetPropertyId — so an arbitrary/stale SiteId would
        // silently assign a worker from an unrelated property. Validate
        // once, before touching any task, so a failure here never creates
        // a partial batch.
        var siteIsTargetPropertyWorker = await backendConfigurationPnDbContext.PropertyWorkers
            .AsNoTracking()
            .AnyAsync(pw =>
                pw.PropertyId == model.TargetPropertyId
                && pw.WorkerId == model.SiteId
                && pw.WorkflowState != Constants.WorkflowStates.Removed);
        if (!siteIsTargetPropertyWorker)
        {
            return new OperationResult(false,
                localizationService.GetString("SelectedWorkerDoesNotBelongToTargetProperty"));
        }

        // Defense-in-depth (mirrors the SiteId guard above): model.TargetBoardId
        // ends up as CalendarTaskCreateRequestModel.BoardId, which CreateTask
        // persists verbatim with no cross-check against model.TargetPropertyId —
        // so a stale/crafted TargetBoardId from another property would silently
        // land in the copy's CalendarConfiguration. Validate once, before
        // touching any task, so a failure here never creates a partial batch.
        var boardIsOnTargetProperty = await backendConfigurationPnDbContext.CalendarBoards
            .AsNoTracking()
            .AnyAsync(b =>
                b.Id == model.TargetBoardId
                && b.PropertyId == model.TargetPropertyId
                && b.WorkflowState != Constants.WorkflowStates.Removed);
        if (!boardIsOnTargetProperty)
        {
            return new OperationResult(false,
                localizationService.GetString("SelectedBoardDoesNotBelongToTargetProperty"));
        }

        return await RunPerTask(model.TaskIds, async id =>
        {
            var source = await BuildUpdateModel(id);
            if (source == null) return (false, "Task not found");
            var create = new CalendarTaskCreateRequestModel
            {
                PropertyId = model.TargetPropertyId,
                FolderId = null,
                ItemPlanningTagId = source.ItemPlanningTagId,
                TagIds = source.TagIds,
                Translates = source.Translates,
                EformId = source.EformId,
                // The user explicitly chose this date/time in the copy
                // dialog — unlike BuildUpdateModel's synthetic anchor (which
                // exists only because batch worker actions have no user
                // input to preserve), there is no reason to override it
                // here. Since #1122 CreateTask has no past-date guard at all,
                // so a back-dated copy is accepted; any other per-task failure
                // still surfaces through RunPerTask/Aggregate, exactly as it
                // would from the interactive calendar create modal given the
                // same input.
                StartDate = model.StartDate,
                RepeatType = source.RepeatType,
                RepeatEvery = source.RepeatEvery,
                // TaskWizardStatuses.NotActive == 2 (verified against the
                // enum and BuildUpdateModel's own `arp.Status ? 1 : 2`
                // convention) — a copy always starts inactive so an admin
                // must review/assign real workers before it goes live.
                Status = (int)TaskWizardStatuses.NotActive,
                Sites = [model.SiteId],
                WorkerTagIds = [],
                ComplianceEnabled = source.ComplianceEnabled,
                StartHour = source.StartHour,
                Duration = source.Duration,
                BoardId = model.TargetBoardId,
                Color = source.Color,
                RepeatEndMode = source.RepeatEndMode,
                RepeatOccurrences = source.RepeatOccurrences,
                RepeatUntilDate = source.RepeatUntilDate,
                RepeatWeekdaysCsv = source.RepeatWeekdaysCsv,
                DayOfMonth = source.DayOfMonth,
                RepeatOrdinalWeek = source.RepeatOrdinalWeek,
                DescriptionHtml = source.DescriptionHtml
            };
            var result = await calendarService.CreateTask(create);
            return (result.Success, result.Message);
        }, "Tasks copied");
    }

    // Delete permanently removes a task-list-eligible planning. It delegates to
    // taskWizardService.DeleteTaskDeferredRetraction(int), which soft-deletes
    // Planning/AreaRuleTranslations/PlanningSites/AreaRule/AreaRulePlanning/
    // Compliances SYNCHRONOUSLY (so the row is gone from the very next
    // tasks/index read) and retracts every deployed SDK case fire-and-forget.
    // The plain DeleteTask retracts inline via core.CaseDelete, which blocks
    // for minutes in dev (no eform-core consumer) and would hang the whole
    // batch request; the deferred variant mirrors the fire-and-forget shape
    // BackendConfigurationCompliancesService.UpdateFromCalendar already ships
    // (PR #1049). Retraction was already best-effort (its result is never
    // consumed), so guarantees are unchanged — only latency and ordering.
    //
    // Server-side safety: only task-list-eligible plannings (existing,
    // non-removed, backed by a CreatedInGuide AreaRule) may be deleted this
    // way — IsEligibleTaskAsync mirrors BuildUpdateModel's own rule lookup
    // so a caller cannot use this batch endpoint to delete an arbitrary
    // AreaRulePlanning id that isn't actually surfaced on the task-list page.
    // Eligibility is checked SYNCHRONOUSLY per task, so per-task ineligibility
    // ("Task not found") is still reported accurately by Aggregate.
    public async Task<OperationResult> Delete(TaskListBatchRequestModel model) =>
        await RunPerTask(model.TaskIds, async id =>
        {
            if (!await IsEligibleTaskAsync(id)) return (false, "Task not found");
            var result = await taskWizardService.DeleteTaskDeferredRetraction(id);
            return (result.Success, result.Message);
        }, "Tasks deleted");

    // Lightweight existence/eligibility check mirroring BuildUpdateModel's
    // own rule lookup (Removed-filtered ARP + CreatedInGuide rule), without
    // loading the full update-model shape Delete doesn't need.
    private async Task<bool> IsEligibleTaskAsync(int areaRulePlanningId)
    {
        var arp = await backendConfigurationPnDbContext.AreaRulePlannings
            .AsNoTracking()
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .FirstOrDefaultAsync(x => x.Id == areaRulePlanningId);
        if (arp == null) return false;

        var rule = await backendConfigurationPnDbContext.AreaRules
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == arp.AreaRuleId);
        return rule is { CreatedInGuide: true };
    }

    // Loads an AreaRulePlanning's full current state and produces a
    // CalendarTaskUpdateRequestModel that, if submitted to
    // calendarService.UpdateTask unchanged, is a no-op edit (Scope="all").
    // Batch worker actions (Assign/Reassign/AddWorker) mutate only the
    // .Sites list on the returned model before submitting it, so every
    // other field here must faithfully round-trip the planning's current
    // state — UpdateTask writes back everything it's handed, including
    // fields we don't intend to touch (translations, description,
    // recurrence rule, calendar-board config, worker-tag links, ...).
    //
    // StartDate/OriginalDate deliberately do NOT carry the planning's real
    // series anchor (arp.StartDate). Two reasons:
    //  1. HISTORICAL, no longer true since #1122: UpdateTask used to reject
    //     any edit whose StartDate+StartHour had already passed
    //     ("CannotCreateTaskInThePast"), and arp.StartDate on an established
    //     series is very likely in the past, so every batch action on such a
    //     series would have failed outright. The guard is gone; reason 2 and
    //     the weekday note below are what keep the synthetic anchor necessary.
    //  2. For Scope="all" with OriginalDate == StartDate.Date, UpdateTask
    //     treats the edit as NOT moving the occurrence ("dateChanged =
    //     false") and re-fetches the TRUE current arp.StartDate from the DB
    //     for the actual wizard write — so the literal StartDate we send is
    //     discarded for that purpose regardless of its value.
    // BUT the value we send for StartDate is still read directly (not
    // re-fetched) for one thing: when the rule is weekly/"Nth weekday of
    // month" (RepeatType==Week or RepeatOrdinalWeek.HasValue), UpdateTask
    // writes `arp.DayOfWeek = updateModel.StartDate.DayOfWeek` verbatim. So
    // an arbitrary future date would corrupt which weekday the series
    // recurs on. To satisfy both constraints we pick the nearest date that
    // is (a) at least tomorrow, so StartDate+StartHour is always in the
    // future regardless of time-of-day, and (b) on the SAME weekday as the
    // real anchor, so DayOfWeek round-trips unchanged. OriginalDate mirrors
    // the same date so dateChanged stays false.
    private async Task<CalendarTaskUpdateRequestModel> BuildUpdateModel(int areaRulePlanningId)
    {
        // AsNoTracking is load-bearing, not just an optimization. BuildUpdateModel
        // only reads (it projects into a new CalendarTaskUpdateRequestModel and
        // never mutates the loaded entity), but this and the calendar/wizard
        // services share ONE scoped DbContext per request. An unfiltered
        // `.Include(x => x.PlanningSites)` on a TRACKING query would attach every
        // PlanningSite row — including soft-REMOVED ones — to the context. When
        // the downstream wizard (BackendConfigurationTaskWizardService.UpdateTask)
        // then loads the same ARP with a FILTERED include
        // (`.Include(x => x.PlanningSites.Where(non-removed))`), EF relationship
        // fixup re-attaches those already-tracked removed rows to the navigation
        // regardless of the filter. That made the wizard's currentSiteIds contain
        // a removed site, so Reassign TO a site that had a stale removed row
        // computed sitesToAdd = [] and never re-created the PlanningSite — the
        // task silently lost its assignment (reassign 220->138 where 138 had a
        // prior removed row). AsNoTracking keeps this read-only load from
        // polluting the shared context so the wizard's filtered include is honored.
        var arp = await backendConfigurationPnDbContext.AreaRulePlannings
            .AsNoTracking()
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Include(x => x.PlanningSites)
            .Include(x => x.AreaRulePlanningTags)
            .FirstOrDefaultAsync(x => x.Id == areaRulePlanningId);
        if (arp == null) return null;

        if (arp.StartDate == null)
        {
            // arp.StartDate is the series anchor. UpdateTask's dateChanged=false
            // re-anchor step (see BackendConfigurationCalendarService.UpdateTask)
            // re-fetches the TRUE current arp.StartDate from the DB and
            // overwrites the synthetic StartDate we send below — but ONLY when
            // that DB value is non-null (`if (currentStartDate.HasValue)`).
            // When arp.StartDate is already null, that guard is skipped, so the
            // synthetic "nearest future same-weekday" date we would synthesize
            // below gets written straight into arp.StartDate, permanently
            // re-anchoring an anchorless series to an arbitrary date. Fail
            // loudly instead of proceeding: throwing here is caught by
            // RunPerTask's existing exception handler, which surfaces a
            // distinct "Task has no start date" error for this task without
            // changing the (ok, error) tuple contract or touching the DB.
            throw new InvalidOperationException("Task has no start date");
        }

        var rule = await backendConfigurationPnDbContext.AreaRules
            .Include(r => r.AreaRuleTranslations)
            .FirstOrDefaultAsync(r => r.Id == arp.AreaRuleId);
        if (rule == null || !rule.CreatedInGuide) return null;

        var configuration = await backendConfigurationPnDbContext.CalendarConfigurations
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .FirstOrDefaultAsync(x => x.AreaRulePlanningId == arp.Id);

        var planning = await itemsPlanningPnDbContext.Plannings
            .FirstOrDefaultAsync(x => x.Id == arp.ItemPlanningId
                && x.WorkflowState != Constants.WorkflowStates.Removed);

        var anchorWeekday = arp.StartDate.Value.DayOfWeek;
        var safeDate = DateTime.UtcNow.Date.AddDays(1);
        while (safeDate.DayOfWeek != anchorWeekday)
        {
            safeDate = safeDate.AddDays(1);
        }

        var workerTagIds = await backendConfigurationPnDbContext.AreaRulePlanningWorkerTags
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(x => x.AreaRulePlanningId == arp.Id)
            .Select(x => x.TagId)
            .ToListAsync();

        return new CalendarTaskUpdateRequestModel
        {
            Id = arp.Id,
            Scope = "all",
            OriginalDate = safeDate.ToString("yyyy-MM-dd"),
            PropertyId = arp.PropertyId,
            FolderId = arp.FolderId,
            ItemPlanningTagId = arp.ItemPlanningTagId,
            TagIds = arp.AreaRulePlanningTags
                .Where(t => t.WorkflowState != Constants.WorkflowStates.Removed)
                .Select(t => t.ItemPlanningTagId)
                .ToList(),
            Translates = rule.AreaRuleTranslations
                .Where(t => t.WorkflowState != Constants.WorkflowStates.Removed)
                .Select(t => new CommonTranslationsModel
                {
                    LanguageId = t.LanguageId,
                    Name = t.Name,
                    Description = t.Description
                })
                .ToList(),
            EformId = rule.EformId ?? 0,
            StartDate = safeDate,
            RepeatType = arp.RepeatType ?? 0,
            RepeatEvery = arp.RepeatEvery ?? 0,
            // TaskWizardStatuses convention (mirrors the Angular edit modal's
            // `status: this.statusControl.value ? 1 : 2`): 1 = active, 2 = inactive.
            Status = arp.Status ? 1 : 2,
            Sites = arp.PlanningSites
                .Where(s => s.WorkflowState != Constants.WorkflowStates.Removed)
                .Select(s => s.SiteId)
                .ToList(),
            WorkerTagIds = workerTagIds,
            ComplianceEnabled = arp.ComplianceEnabled,
            // Must match the read fallback in BackendConfigurationCalendarService:
            // UpdateTask writes StartHour unconditionally, so a 0 here would move an
            // un-configured task the grid renders at 09:00 down to midnight -- and
            // stamp the new row with a real CreatedByUserId, putting it permanently
            // beyond the legacy-midnight repair in CalendarConfigurationBackfillService.
            StartHour = configuration?.StartHour ?? 9.0,
            Duration = configuration?.Duration ?? 1.0,
            BoardId = configuration?.BoardId,
            Color = configuration?.Color,
            RepeatEndMode = arp.RepeatEndMode,
            RepeatOccurrences = arp.RepeatOccurrences,
            RepeatUntilDate = arp.RepeatUntilDate,
            RepeatWeekdaysCsv = arp.RepeatWeekdaysCsv,
            DayOfMonth = arp.DayOfMonth,
            RepeatOrdinalWeek = arp.RepeatOrdinalWeek,
            DescriptionHtml = planning?.Description ?? string.Empty
        };
    }

    // Aggregates per-task results into a single OperationResult. All-success
    // (including the empty-list case) yields the success message; a mix
    // yields a partial-failure message with an "ok/total" count plus a
    // semicolon-joined "#id: error" list; all-failure keeps the same shape
    // with Success=false.
    private OperationResult Aggregate(List<(int Id, bool Ok, string Error)> results, string successKey)
    {
        var ok = results.Count(r => r.Ok);
        var failed = results.Where(r => !r.Ok).ToList();
        if (failed.Count == 0)
        {
            return new OperationResult(true, localizationService.GetString(successKey));
        }
        var errors = string.Join("; ", failed.Select(f => $"#{f.Id}: {f.Error}"));
        return new OperationResult(ok > 0,
            $"{localizationService.GetString("PartiallyCompleted")} {ok}/{results.Count}. {errors}");
    }

    private async Task<OperationResult> RunPerTask(
        List<int> taskIds,
        Func<int, Task<(bool Ok, string Error)>> action,
        string successKey)
    {
        var results = new List<(int, bool, string)>();
        foreach (var id in taskIds)
        {
            try
            {
                var (ok, error) = await action(id);
                results.Add((id, ok, error));
            }
            catch (Exception e)
            {
                logger.LogError(e, "Task-list batch action failed for planning {Id}", id);
                results.Add((id, false, e.Message));
            }
        }
        return Aggregate(results, successKey);
    }
}
