using Sentry;

namespace BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BackendConfigurationLocalizationService;
using BackendConfigurationTaskWizardService;
using EventDeployService;
using Infrastructure.Models.Calendar;
using Infrastructure.Models.TaskWizard;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Dto;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Helpers;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.eForm.Infrastructure.Models;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using SdkUploadedData = Microting.eForm.Infrastructure.Data.Entities.UploadedData;

public class BackendConfigurationCalendarService(
    IBackendConfigurationLocalizationService localizationService,
    IUserService userService,
    BackendConfigurationPnDbContext backendConfigurationPnDbContext,
    IEFormCoreService coreHelper,
    IEventDeployService eventDeployService,
    ItemsPlanningPnDbContext itemsPlanningPnDbContext,
    IBackendConfigurationTaskWizardService taskWizardService,
    ILogger<BackendConfigurationCalendarService> logger)
    : IBackendConfigurationCalendarService
{
    public async Task<OperationDataResult<List<CalendarTaskResponseModel>>> GetTasksForWeek(
        CalendarTaskRequestModel requestModel)
    {
        try
        {
            var weekStart = DateTime.Parse(requestModel.WeekStart, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            var weekEnd = DateTime.Parse(requestModel.WeekEnd, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

            var userLanguageId = (await userService.GetCurrentUserLanguage()).Id;
            var result = new List<CalendarTaskResponseModel>();

            // Get the default board for this property (first created board)
            var defaultBoard = await backendConfigurationPnDbContext.CalendarBoards
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.PropertyId == requestModel.PropertyId)
                .OrderBy(x => x.Id)
                .FirstOrDefaultAsync();
            var defaultBoardId = defaultBoard?.Id;

            // Pre-load compliance dates to avoid duplicates between occurrence expansion and compliances.
            //
            // Two modes, gated on requestModel.ActionableOnly:
            //
            // * ActionableOnly == false (default; angular admin REST calendar +
            //   CalendarGrpcService): emit ALL non-removed compliances in the week, including
            //   missed deadlines and already-completed ones. Bit-identical to pre-c2637800.
            //
            // * ActionableOnly == true (mobile worker via EventsGrpcService): emit only
            //   compliances whose backing SDK Case still exists, is not soft-deleted, and is
            //   not yet completed (Status != 100). Non-actionable compliance rows must NOT be
            //   emitted to the worker because the corresponding write handlers ("complete",
            //   "comment", etc.) have nothing to bind to and will fail.
            List<Compliance> compliancesInWeek;
            // Bug A fix side-dict — see ActionableOnly branch below for rationale.
            // Empty for non-ActionableOnly callers (angular admin REST + CalendarGrpcService);
            // the recurrence-emit lookup below tolerates that as a no-op.
            Dictionary<(int PlanningId, DateTime Date), (int ComplianceId, int SdkCaseId)> nonActionableByPlanningDate
                = new();
            // The set of compliances whose (PlanningId, Deadline) tuples should
            // suppress recurrence-expansion emit for that date. Default branch
            // populates this with compliancesInWeek (which already includes
            // removed-but-completed rows, see line 100-104). The ActionableOnly
            // branch builds the union of compliancesInWeek + removed-completed
            // separately so the mobile worker doesn't see a phantom uncompleted
            // task for a date they just completed (the canonical compliance
            // Update + gRPC mobile complete both soft-delete Compliance after
            // setting Status=100, so without this suppression the
            // recurrence-expansion loop happily re-emits the date).
            List<Compliance> compliancesForDedup;
            if (!requestModel.ActionableOnly)
            {
                // Load both:
                //   * non-removed compliances (the live week view).
                //   * removed-but-COMPLETED compliances. The canonical compliance
                //     Update path (CompliancesService.Update + mobile gRPC) soft-
                //     deletes the Compliance row after setting the backing SDK
                //     Case to Status=100. Without including these here, the
                //     recurrence-expansion loop below would emit a fresh
                //     uncompleted task for the same date (because its dedup set
                //     is built from `compliancesInWeek`), and the user would
                //     see the just-completed event "snap back" to uncompleted.
                //
                //     The Where below still excludes removed compliances that
                //     never deployed (SdkCaseId == 0 — retracted-without-case
                //     shape) so genuinely missed/retracted rotations stay
                //     hidden. The post-load filter further narrows
                //     removed rows to those whose SDK case is Status=100.
                var loadedCompliances = await backendConfigurationPnDbContext.Compliances
                    .Where(x => x.PropertyId == requestModel.PropertyId)
                    .Where(x => x.Deadline >= weekStart && x.Deadline <= weekEnd)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed
                                || x.MicrotingSdkCaseId > 0)
                    .ToListAsync();

                var loadedCaseIds = loadedCompliances
                    .Select(c => c.MicrotingSdkCaseId)
                    .Where(id => id > 0)
                    .Distinct()
                    .ToList();
                var loadedCases = new Dictionary<int, Microting.eForm.Infrastructure.Data.Entities.Case>();
                if (loadedCaseIds.Count > 0)
                {
                    var sdkCoreForPrefilter = await coreHelper.GetCore().ConfigureAwait(false);
                    await using var sdkDbContextForPrefilter = sdkCoreForPrefilter.DbContextHelper.GetDbContext();
                    loadedCases = await sdkDbContextForPrefilter.Cases
                        .Where(c => loadedCaseIds.Contains(c.Id))
                        .ToDictionaryAsync(c => c.Id);
                }

                compliancesInWeek = loadedCompliances
                    .Where(c => c.WorkflowState != Constants.WorkflowStates.Removed
                            || (loadedCases.TryGetValue(c.MicrotingSdkCaseId, out var sdk) && sdk.Status == 100))
                    .ToList();
                // Default branch already includes removed-completed rows in
                // compliancesInWeek (filter above), so the dedup set is identical.
                compliancesForDedup = compliancesInWeek;
            }
            else
            {
                // Mobile-worker branch — actionable subset only.
                // Treat WorkflowState NULL as "not removed" here (pre-existing project rule
                // applied across this service); the default branch above keeps its original
                // strict `!= Removed` semantics so non-mobile callers remain bit-identical.
                //
                // Include removed-but-deployed rows in the load so the post-load
                // pass can pull out the removed-COMPLETED subset (Status=100 on
                // backing SDK case) for recurrence-dedup. Without this, the
                // recurrence-emit loop would happily re-emit a date the user
                // just completed — the canonical complete path
                // (CompliancesGrpcService.UpdateComplianceCase / web modal
                // Save) soft-deletes the Compliance row after setting
                // Status=100, leaving no live row in the actionable subset
                // to seed the dedup set with.
                var compliancesInWeekAll = await backendConfigurationPnDbContext.Compliances
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed
                                || x.WorkflowState == null
                                || x.MicrotingSdkCaseId > 0)
                    .Where(x => x.PropertyId == requestModel.PropertyId)
                    .Where(x => x.Deadline >= weekStart && x.Deadline <= weekEnd)
                    .ToListAsync();

                // Batch-load the SDK Cases backing those compliances so we can decide
                // actionability without an N+1 round-trip per compliance row.
                var complianceSdkCaseIds = compliancesInWeekAll
                    .Select(c => c.MicrotingSdkCaseId)
                    .Where(id => id > 0)
                    .Distinct()
                    .ToList();
                var sdkCore = await coreHelper.GetCore().ConfigureAwait(false);
                await using var sdkDbContextForCalendar = sdkCore.DbContextHelper.GetDbContext();
                var sdkCasesById = await sdkDbContextForCalendar.Cases
                    .Where(c => complianceSdkCaseIds.Contains(c.Id))
                    .ToDictionaryAsync(c => c.Id);

                bool IsComplianceActionable(Compliance compliance)
                {
                    // 1. Compliance row itself must not be soft-deleted (NULL == not removed).
                    if (compliance.WorkflowState == Constants.WorkflowStates.Removed)
                        return false;

                    // 2. Backing SDK Case must exist and not be soft-deleted (NULL == not removed).
                    if (compliance.MicrotingSdkCaseId <= 0)
                        return false;
                    if (!sdkCasesById.TryGetValue(compliance.MicrotingSdkCaseId, out var sdkCase) || sdkCase == null)
                        return false;
                    if (sdkCase.WorkflowState == Constants.WorkflowStates.Removed)
                        return false;

                    // 3. SDK Case must not be already completed.
                    //    Status == 100 is the canonical "done" code (see e.g.
                    //    BackendConfigurationCompliancesService.cs:258, BackendConfigurationCaseService.cs:73,
                    //    BackendConfigurationReportService.cs:84).
                    if (sdkCase.Status == 100)
                        return false;

                    return true;
                }

                // The actionable subset is what we actually emit AND what governs the
                // recurrence-dedup gate below. If a planning's only in-week compliance is
                // non-actionable (missed deadline or already completed), the recurrence path
                // SHOULD still fire so the worker doesn't lose visibility on a NEXT live
                // rotation in the same week.
                compliancesInWeek = compliancesInWeekAll
                    .Where(IsComplianceActionable)
                    .ToList();

                // Bug A fix (compliance 9810 / case 17701 retracted-rotation parity):
                // when a compliance is filtered out by IsComplianceActionable (retracted SDK
                // case, soft-deleted, or status==100), the recurrence-emit loop below STILL
                // fires for that planning's occurrence date because the compliance dedup set
                // (built from the filtered actionable subset) no longer contains it. Without
                // intervention the model emitted by that loop has ComplianceId=null /
                // SdkCaseId=null, the device caches compliance_id=0 in Drift, and any
                // subsequent CompleteOpgave / SetComment / SetFieldValue / UploadPhoto write
                // arrives with compliance_id=0 — which then either fails to resolve (legacy
                // payloads) or routes through the fallback fuzzy lookup that historically
                // excluded retracted cases (Bug B).
                //
                // Fix: keep the actionable-only filter behavior intact (the row stays
                // expired / non-actionable in the UI; IsFromCompliance stays false on the
                // recurrence path) but populate ComplianceId + SdkCaseId from the stripped
                // compliance so any device-side write round-trips through the PK lookup
                // branch instead of the fuzzy fallback. See investigator notes for commit
                // 47f20657 — root cause: ListOpgaver→Drift only ever sees the
                // recurrence-emit model when actionability stripping removed the compliance.
                // Compute the removed-COMPLETED subset (WorkflowState=Removed +
                // backing case Status=100) up-front so we can both:
                //  (a) use it to expand the recurrence-dedup set below
                //      (`compliancesForDedup`), suppressing phantom uncompleted
                //      re-emission for dates the worker just completed.
                //  (b) EXCLUDE it from `nonActionableByPlanningDate` (the Bug A
                //      device-write routing slot). A removed-completed row
                //      points at a closed Status=100 SDK Case; if it shared a
                //      (PlanningId, Deadline.Date) with a separately-retracted
                //      row, GroupBy's first-wins ordering could non-
                //      deterministically route a device write to the closed
                //      case instead of the live retracted one. Keep
                //      nonActionableByPlanningDate focused on the original
                //      Bug A scope (retracted SDK case / soft-deleted /
                //      Status==100 inline-rotation) by stripping removed-
                //      completed before the GroupBy.
                var removedCompletedInWeek = compliancesInWeekAll
                    .Where(c => c.WorkflowState == Constants.WorkflowStates.Removed
                            && c.MicrotingSdkCaseId > 0
                            && sdkCasesById.TryGetValue(c.MicrotingSdkCaseId, out var removedSdkCase)
                            && removedSdkCase.Status == 100)
                    .ToList();
                var removedCompletedIds = new HashSet<int>(removedCompletedInWeek.Select(c => c.Id));
                // ID-based HashSet membership (vs `List.Contains` on the Compliance
                // object) keeps the GroupBy filter below O(n) — addresses
                // Copilot's L256 perf note on PR 847.
                var compliancesInWeekIds = new HashSet<int>(compliancesInWeek.Select(c => c.Id));

                nonActionableByPlanningDate = compliancesInWeekAll
                    .Where(c => !compliancesInWeekIds.Contains(c.Id) && !removedCompletedIds.Contains(c.Id))
                    .GroupBy(c => (c.PlanningId, c.Deadline.Date))
                    // GroupBy + first-wins guards against the (unlikely) case of multiple
                    // non-actionable compliance rows sharing a (planning, day) tuple.
                    .ToDictionary(g => g.Key, g => (ComplianceId: g.First().Id,
                        SdkCaseId: g.First().MicrotingSdkCaseId));

                // Recurrence-dedup union: the actionable subset (compliancesInWeek)
                // PLUS the removed-COMPLETED subset. The latter doesn't render to
                // mobile workers (IsComplianceActionable strips them), but their
                // (PlanningId, Deadline) tuples must still suppress recurrence-
                // expansion emit for that date — otherwise the worker sees a
                // phantom uncompleted task right after completing it.
                compliancesForDedup = compliancesInWeek.Concat(removedCompletedInWeek).ToList();
            }

            // Build sets for dedup: by exact date and by planningId. compliancesForDedup
            // is the actionable subset for non-ActionableOnly callers (already includes
            // removed-completed rows per the broadened query at line 100-104), and for
            // ActionableOnly callers is actionable ∪ removed-completed so the recurrence-
            // emit loop doesn't double-fire a date the worker just completed.
            //
            // Defect E in #935 — only ACTIONABLE Compliance rows should suppress the
            // recurrence-emit. A poisoned Compliance row (MicrotingSdkCaseId == 0 with
            // a non-Removed WorkflowState — the legacy shape produced by the
            // pre-defect-B code path) is NOT emitted by the Compliance loop downstream
            // (which gates on a valid backing SDK case), so without this filter such
            // a row would skip the recurrence-emit AND get no Compliance-loop emit
            // either — producing zero tiles for that (planning, date). For
            // non-Removed rows we require a real SDK case linkage; removed-completed
            // rows (which carry a valid SdkCaseId > 0 by construction in
            // removedCompletedInWeek above) pass through unchanged so the worker
            // doesn't see a phantom uncompleted task right after completing it.
            // Filter is necessary in the non-ActionableOnly branch (where compliancesInWeek
            // includes MicrotingSdkCaseId == 0 rows) and a no-op in the ActionableOnly
            // branch — placement at the union point is intentional for single-source-of-truth
            // semantics.
            var complianceDateSet = new HashSet<string>(
                compliancesForDedup
                    .Where(c => c.WorkflowState == Constants.WorkflowStates.Removed
                                || c.MicrotingSdkCaseId > 0)
                    .Select(c => $"{c.PlanningId}:{c.Deadline.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"));

            // 1. Query AreaRulePlannings (future/active and inactive tasks).
            // Inactive (Status=false) plannings are included so the calendar can
            // render them dimmed; the frontend keys the dim style off Status.
            var areaRulePlannings = await backendConfigurationPnDbContext.AreaRulePlannings
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.PropertyId == requestModel.PropertyId)
                .Include(x => x.AreaRule)
                    .ThenInclude(x => x.AreaRuleTranslations)
                .Include(x => x.PlanningSites)
                .Include(x => x.AreaRulePlanningTags)
                .Include(x => x.AreaRulePlanningFiles)
                    .ThenInclude(f => f.GoogleOAuthToken)
                .ToListAsync();

            // Batch-load plannings to avoid N+1 queries.
            // Soft-deleted plannings are INCLUDED so the calendar can still
            // show inactive (Status=false) tasks dimmed — the cascade in
            // TaskWizardService.UpdateTask soft-deletes the underlying Planning
            // when Status flips OFF, but we still want the AreaRulePlanning
            // to surface in the response so the frontend can render it dimmed.
            var planningIds = areaRulePlannings.Select(x => x.ItemPlanningId).Distinct().ToList();
            var planningsDict = await itemsPlanningPnDbContext.Plannings
                .Where(x => planningIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id);

            // Batch-load calendar configurations
            var arpIds = areaRulePlannings.Select(x => x.Id).ToList();
            var calConfigsDict = await backendConfigurationPnDbContext.CalendarConfigurations
                .Where(x => arpIds.Contains(x.AreaRulePlanningId))
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .ToDictionaryAsync(x => x.AreaRulePlanningId);

            // Batch-load occurrence exceptions for this week
            var exceptionsInWeek = await backendConfigurationPnDbContext.CalendarOccurrenceExceptions
                .Where(x => arpIds.Contains(x.AreaRulePlanningId))
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x =>
                    (x.OriginalDate >= weekStart && x.OriginalDate <= weekEnd) ||
                    (x.NewDate.HasValue && x.NewDate.Value >= weekStart && x.NewDate.Value <= weekEnd))
                .Include(x => x.ExceptionSites)
                .ToListAsync();

            var exceptionsByArp = exceptionsInWeek
                .GroupBy(x => x.AreaRulePlanningId)
                .ToDictionary(
                    g => g.Key,
                    g => g.ToDictionary(x => x.OriginalDate.Date));

            var movedInExceptions = exceptionsInWeek
                .Where(x => x.NewDate.HasValue
                    && !x.IsDeleted
                    && (x.OriginalDate < weekStart || x.OriginalDate > weekEnd)
                    && x.NewDate.Value >= weekStart && x.NewDate.Value <= weekEnd)
                .ToList();

            // Batch-load tags for all ARPs
            var allArpTags = await backendConfigurationPnDbContext.AreaRulePlanningTags
                .Where(x => arpIds.Contains(x.AreaRulePlanningId))
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .ToListAsync();

            var tagItemIds = allArpTags.Select(x => x.ItemPlanningTagId).Distinct().ToList();
            var planningTagNames = await itemsPlanningPnDbContext.PlanningTags
                .Where(x => tagItemIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name);

            foreach (var arp in areaRulePlannings)
            {
                if (!planningsDict.TryGetValue(arp.ItemPlanningId, out var planning))
                    continue;

                // Compute all occurrence dates within the requested week.
                // Pass arp.RepeatWeekdaysCsv so multi-day weekly rules
                // (e.g. "1,3,5") expand to multiple occurrences per week.
                var occurrences = GetOccurrencesInWeek(planning, weekStart, weekEnd, arp.RepeatWeekdaysCsv,
                    arp.RepeatOrdinalWeek, arp.DayOfWeek);

                // Filter by repeat end mode
                if (arp.RepeatEndMode == 2 && arp.RepeatUntilDate.HasValue)
                    occurrences.RemoveAll(d => d > arp.RepeatUntilDate.Value);
                else if (arp.RepeatEndMode == 1 && arp.RepeatOccurrences.HasValue)
                {
                    // Use EnumerateOccurrences (week-loop iterator) instead of
                    // GetOccurrencesInWeek for the cumulative count: the latter's
                    // multi-day weekly branch emits at most one matching week, so
                    // the after-cap would never fire for CSV rules. Upper bound
                    // on EnumerateOccurrences is exclusive — add a day.
                    var allOccsSince = EnumerateOccurrences(planning,
                        planning.StartDate.Date, weekEnd.AddDays(1),
                        arp.RepeatWeekdaysCsv, arp.RepeatOrdinalWeek, arp.DayOfWeek).ToList();
                    var maxOcc = arp.RepeatOccurrences.Value;
                    if (allOccsSince.Count > maxOcc)
                    {
                        var cutoff = allOccsSince[maxOcc - 1];
                        occurrences.RemoveAll(d => d > cutoff);
                    }
                }

                // Even when the rule generates no occurrences for this week,
                // we still need to consider per-occurrence exceptions whose
                // OriginalDate falls inside the requested window — they
                // render via the orphan-anchor pass below.
                var hasInWeekExceptions = exceptionsByArp.TryGetValue(arp.Id, out var inWeekArpExceptions)
                    && inWeekArpExceptions.Values.Any(x =>
                        !x.IsDeleted
                        && x.OriginalDate >= weekStart && x.OriginalDate <= weekEnd
                        && (!x.NewDate.HasValue || x.NewDate.Value.Date == x.OriginalDate.Date));

                if (occurrences.Count == 0 && !hasInWeekExceptions)
                    continue;

                calConfigsDict.TryGetValue(arp.Id, out var calConfig);
                var isRepeatAlways = arp.RepeatType.HasValue && arp.RepeatType.Value == 1 && (arp.RepeatEvery ?? 0) == 0;
                var hasNonAlwaysRepeat = arp.RepeatType.HasValue && arp.RepeatType.Value > 0 && !isRepeatAlways;
                var isAllDay = calConfig == null && !hasNonAlwaysRepeat;

                var title = arp.AreaRule?.AreaRuleTranslations?
                    .Where(t => t.LanguageId == userLanguageId)
                    .Select(t => t.Name)
                    .FirstOrDefault() ?? arp.AreaRule?.AreaRuleTranslations?.FirstOrDefault()?.Name ?? "";

                var tags = allArpTags
                    .Where(x => x.AreaRulePlanningId == arp.Id)
                    .Select(x => planningTagNames.TryGetValue(x.ItemPlanningTagId, out var name) ? name : null)
                    .Where(x => x != null)
                    .ToList();

                var assigneeIds = arp.PlanningSites?
                    .Where(ps => ps.WorkflowState != Constants.WorkflowStates.Removed)
                    .Select(ps => (int)ps.SiteId)
                    .ToList() ?? [];

                foreach (var occurrenceDate in occurrences)
                {
                    // Suppress recurrence-emit ONLY for the specific date(s) that
                    // already have a compliance row (rendered by the compliance
                    // loop below) — keyed per (planning, date). The previous gate
                    // keyed per planningId, so completing ONE occurrence of a
                    // multi-day series (e.g. Mon-Fri) created a compliance row
                    // and then suppressed EVERY sibling occurrence, making the
                    // other (uncompleted) days disappear from the week.
                    if (complianceDateSet.Contains($"{arp.ItemPlanningId}:{occurrenceDate.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"))
                        continue;

                    CalendarOccurrenceException exception = null;
                    if (exceptionsByArp.TryGetValue(arp.Id, out var arpExceptions))
                    {
                        arpExceptions.TryGetValue(occurrenceDate.Date, out exception);
                    }

                    if (exception is { IsDeleted: true })
                        continue;

                    var effectiveDate = exception?.NewDate?.Date ?? occurrenceDate;
                    var effectiveStartHour = exception?.StartHour ?? (isAllDay ? 0 : calConfig?.StartHour ?? 9.0);
                    var effectiveDuration = exception?.Duration ?? (isAllDay ? 0 : calConfig?.Duration ?? 1.0);
                    var effectiveAssignees = exception?.ExceptionSites is { Count: > 0 }
                        ? exception.ExceptionSites
                            .Where(s => s.WorkflowState != Constants.WorkflowStates.Removed)
                            .Select(s => s.SiteId)
                            .ToList()
                        : assigneeIds;

                    var model = new CalendarTaskResponseModel
                    {
                        Id = arp.Id,
                        Title = title,
                        StartHour = effectiveStartHour,
                        Duration = effectiveDuration,
                        TaskDate = effectiveDate.ToString("yyyy-MM-dd"),
                        Tags = tags,
                        AssigneeIds = effectiveAssignees,
                        BoardId = calConfig?.BoardId ?? defaultBoardId,
                        Color = calConfig?.Color,
                        RepeatType = arp.RepeatType ?? 0,
                        RepeatEvery = arp.RepeatEvery ?? 1,
                        RepeatEndMode = arp.RepeatEndMode,
                        RepeatOccurrences = arp.RepeatOccurrences,
                        RepeatUntilDate = arp.RepeatUntilDate,
                        DayOfWeek = arp.DayOfWeek,
                        DayOfMonth = arp.DayOfMonth,
                        RepeatOrdinalWeek = arp.RepeatOrdinalWeek,
                        RepeatWeekdaysCsv = arp.RepeatWeekdaysCsv,
                        Completed = false,
                        Status = arp.Status,
                        ComplianceEnabled = arp.ComplianceEnabled,
                        PropertyId = arp.PropertyId,
                        IsFromCompliance = false,
                        NextExecutionTime = planning.NextExecutionTime,
                        PlanningId = planning.Id,
                        IsAllDay = isAllDay,
                        ExceptionId = exception?.Id,
                        EformId = arp.AreaRule?.EformId,
                        ItemPlanningTagId = arp.ItemPlanningTagId,
                        DescriptionHtml = planning.Description,
                        Attachments = MapAttachments(arp)
                    };

                    // Per-occurrence field overrides from a "this"-scope edit (#885).
                    ApplyOccurrenceFieldOverrides(model, exception);

                    // Bug A fix: if a non-actionable compliance was stripped for this
                    // (planningId, occurrenceDate), propagate its ComplianceId + SdkCaseId
                    // so any device-side write routes through the PK lookup. Leave
                    // IsFromCompliance=false (we are on the recurrence path and there is
                    // no actionable compliance to materialise as a calendar row).
                    if (nonActionableByPlanningDate.TryGetValue(
                            (arp.ItemPlanningId, occurrenceDate.Date), out var stripped))
                    {
                        model.ComplianceId = stripped.ComplianceId;
                        model.SdkCaseId = stripped.SdkCaseId;
                    }

                    if (ShouldIncludeTask(model, requestModel))
                    {
                        result.Add(model);
                    }
                }

                // Render any in-week exceptions whose OriginalDate is NOT
                // covered by the recurrence rule (e.g. past anchors created
                // when a 'thisAndFollowing' move shifted planning.StartDate
                // forward). Without this, those past occurrences would
                // silently disappear from the calendar view.
                if (exceptionsByArp.TryGetValue(arp.Id, out var allArpExceptions))
                {
                    var renderedDates = new HashSet<DateTime>(occurrences.Select(o => o.Date));
                    foreach (var orphan in allArpExceptions.Values)
                    {
                        if (orphan.IsDeleted) continue;
                        if (renderedDates.Contains(orphan.OriginalDate.Date)) continue;
                        if (orphan.OriginalDate < weekStart || orphan.OriginalDate > weekEnd) continue;
                        // Skip exceptions whose NewDate moves them to a
                        // different date — those are handled by the
                        // movedInExceptions pass at the destination week.
                        if (orphan.NewDate.HasValue && orphan.NewDate.Value.Date != orphan.OriginalDate.Date) continue;
                        // A compliance row already renders this date with its real
                        // completion state and case binding. Emitting an orphan
                        // anchor on top would stack a second, hard-coded
                        // uncompleted/unopenable tile over it — the completed task
                        // "resurrecting" as not-done (#930) and the duplicate
                        // sibling tiles (#928). Let the compliance loop own it.
                        if (complianceDateSet.Contains(
                                $"{planning.Id}:{orphan.OriginalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"))
                            continue;

                        var orphanStartHour = orphan.StartHour ?? (isAllDay ? 0 : calConfig?.StartHour ?? 9.0);
                        var orphanDuration = orphan.Duration ?? (isAllDay ? 0 : calConfig?.Duration ?? 1.0);
                        var orphanAssignees = orphan.ExceptionSites is { Count: > 0 }
                            ? orphan.ExceptionSites
                                .Where(s => s.WorkflowState != Constants.WorkflowStates.Removed)
                                .Select(s => s.SiteId)
                                .ToList()
                            : assigneeIds;

                        var orphanModel = new CalendarTaskResponseModel
                        {
                            Id = arp.Id,
                            Title = title,
                            StartHour = orphanStartHour,
                            Duration = orphanDuration,
                            TaskDate = orphan.OriginalDate.ToString("yyyy-MM-dd"),
                            Tags = tags,
                            AssigneeIds = orphanAssignees,
                            BoardId = calConfig?.BoardId ?? defaultBoardId,
                            Color = calConfig?.Color,
                            RepeatType = arp.RepeatType ?? 0,
                            RepeatEvery = arp.RepeatEvery ?? 1,
                            RepeatEndMode = arp.RepeatEndMode,
                            RepeatOccurrences = arp.RepeatOccurrences,
                            RepeatUntilDate = arp.RepeatUntilDate,
                            DayOfWeek = arp.DayOfWeek,
                            DayOfMonth = arp.DayOfMonth,
                            RepeatOrdinalWeek = arp.RepeatOrdinalWeek,
                            RepeatWeekdaysCsv = arp.RepeatWeekdaysCsv,
                            Completed = false,
                            Status = arp.Status,
                            ComplianceEnabled = arp.ComplianceEnabled,
                            PropertyId = arp.PropertyId,
                            IsFromCompliance = false,
                            NextExecutionTime = planning.NextExecutionTime,
                            PlanningId = planning.Id,
                            IsAllDay = isAllDay,
                            ExceptionId = orphan.Id,
                            EformId = arp.AreaRule?.EformId,
                            ItemPlanningTagId = arp.ItemPlanningTagId,
                            DescriptionHtml = planning.Description,
                            Attachments = MapAttachments(arp)
                        };

                        ApplyOccurrenceFieldOverrides(orphanModel, orphan);

                        if (ShouldIncludeTask(orphanModel, requestModel))
                        {
                            result.Add(orphanModel);
                        }
                    }
                }
            }

            // Add occurrences that were moved INTO this week from outside
            foreach (var movedIn in movedInExceptions)
            {
                var arp = areaRulePlannings.FirstOrDefault(a => a.Id == movedIn.AreaRulePlanningId);
                if (arp == null) continue;
                if (!planningsDict.TryGetValue(arp.ItemPlanningId, out var movedPlanning)) continue;

                calConfigsDict.TryGetValue(arp.Id, out var movedCalConfig);
                var isRepeatAlways = arp.RepeatType.HasValue && arp.RepeatType.Value == 1 && (arp.RepeatEvery ?? 0) == 0;
                var hasNonAlwaysRepeat = arp.RepeatType.HasValue && arp.RepeatType.Value > 0 && !isRepeatAlways;
                var isAllDay = movedCalConfig == null && !hasNonAlwaysRepeat;

                var title = arp.AreaRule?.AreaRuleTranslations?
                    .Where(t => t.LanguageId == userLanguageId)
                    .Select(t => t.Name)
                    .FirstOrDefault() ?? arp.AreaRule?.AreaRuleTranslations?.FirstOrDefault()?.Name ?? "";

                var movedTags = allArpTags
                    .Where(x => x.AreaRulePlanningId == arp.Id)
                    .Select(x => planningTagNames.TryGetValue(x.ItemPlanningTagId, out var name) ? name : null)
                    .Where(x => x != null)
                    .ToList();

                var movedAssignees = movedIn.ExceptionSites is { Count: > 0 }
                    ? movedIn.ExceptionSites
                        .Where(s => s.WorkflowState != Constants.WorkflowStates.Removed)
                        .Select(s => s.SiteId)
                        .ToList()
                    : arp.PlanningSites?
                        .Where(ps => ps.WorkflowState != Constants.WorkflowStates.Removed)
                        .Select(ps => (int)ps.SiteId)
                        .ToList() ?? [];

                var movedModel = new CalendarTaskResponseModel
                {
                    Id = arp.Id,
                    Title = title,
                    StartHour = movedIn.StartHour ?? (isAllDay ? 0 : movedCalConfig?.StartHour ?? 9.0),
                    Duration = movedIn.Duration ?? (isAllDay ? 0 : movedCalConfig?.Duration ?? 1.0),
                    TaskDate = movedIn.NewDate!.Value.ToString("yyyy-MM-dd"),
                    Tags = movedTags,
                    AssigneeIds = movedAssignees,
                    BoardId = movedCalConfig?.BoardId ?? defaultBoardId,
                    Color = movedCalConfig?.Color,
                    RepeatType = arp.RepeatType ?? 0,
                    RepeatEvery = arp.RepeatEvery ?? 1,
                    RepeatEndMode = arp.RepeatEndMode,
                    RepeatOccurrences = arp.RepeatOccurrences,
                    RepeatUntilDate = arp.RepeatUntilDate,
                    DayOfWeek = arp.DayOfWeek,
                    DayOfMonth = arp.DayOfMonth,
                    RepeatOrdinalWeek = arp.RepeatOrdinalWeek,
                    RepeatWeekdaysCsv = arp.RepeatWeekdaysCsv,
                    Completed = false,
                    Status = arp.Status,
                    ComplianceEnabled = arp.ComplianceEnabled,
                    PropertyId = arp.PropertyId,
                    IsFromCompliance = false,
                    NextExecutionTime = movedPlanning.NextExecutionTime,
                    PlanningId = movedPlanning.Id,
                    IsAllDay = isAllDay,
                    ExceptionId = movedIn.Id,
                    EformId = arp.AreaRule?.EformId,
                    ItemPlanningTagId = arp.ItemPlanningTagId,
                    DescriptionHtml = movedPlanning.Description,
                    Attachments = MapAttachments(arp)
                };

                ApplyOccurrenceFieldOverrides(movedModel, movedIn);

                if (ShouldIncludeTask(movedModel, requestModel))
                {
                    result.Add(movedModel);
                }
            }

            // 2. Query Compliances (past/historical tasks) — reuse pre-loaded data
            var compliances = compliancesInWeek;

            // Batch-load AreaRulePlannings for compliances
            var compliancePlanningIds = compliances.Select(x => x.PlanningId).Distinct().ToList();
            var complianceArps = await backendConfigurationPnDbContext.AreaRulePlannings
                .Where(x => compliancePlanningIds.Contains(x.ItemPlanningId))
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Include(x => x.AreaRule)
                    .ThenInclude(x => x.AreaRuleTranslations)
                .Include(x => x.PlanningSites)
                .Include(x => x.AreaRulePlanningFiles)
                    .ThenInclude(f => f.GoogleOAuthToken)
                .ToListAsync();
            var complianceArpDict = complianceArps.ToDictionary(x => x.ItemPlanningId);

            // Batch-load calendar configs for compliance ARPs
            var complianceArpIds = complianceArps.Select(x => x.Id).ToList();
            var complianceCalConfigs = await backendConfigurationPnDbContext.CalendarConfigurations
                .Where(x => complianceArpIds.Contains(x.AreaRulePlanningId))
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .ToDictionaryAsync(x => x.AreaRulePlanningId);

            // Top up exceptionsByArp with any exceptions for compliance ARPs not
            // already covered. arpIds now contains all non-Removed plannings
            // (Status filter dropped at line 288); this top-up therefore only
            // fires for compliance rows whose ARP has WorkflowState=Removed —
            // a narrow edge case kept for safety.
            var complianceOnlyArpIds = complianceArpIds.Except(arpIds).ToList();
            if (complianceOnlyArpIds.Count > 0)
            {
                var extraExceptions = await backendConfigurationPnDbContext.CalendarOccurrenceExceptions
                    .Where(x => complianceOnlyArpIds.Contains(x.AreaRulePlanningId))
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x =>
                        (x.OriginalDate >= weekStart && x.OriginalDate <= weekEnd) ||
                        (x.NewDate.HasValue && x.NewDate.Value >= weekStart && x.NewDate.Value <= weekEnd))
                    .Include(x => x.ExceptionSites)
                    .ToListAsync();
                foreach (var ex in extraExceptions)
                {
                    if (!exceptionsByArp.TryGetValue(ex.AreaRulePlanningId, out var perArpDict))
                    {
                        perArpDict = new Dictionary<DateTime, CalendarOccurrenceException>();
                        exceptionsByArp[ex.AreaRulePlanningId] = perArpDict;
                    }
                    perArpDict[ex.OriginalDate.Date] = ex;
                }
            }

            // Batch-load tags for compliance ARPs
            var complianceArpTags = await backendConfigurationPnDbContext.AreaRulePlanningTags
                .Where(x => complianceArpIds.Contains(x.AreaRulePlanningId))
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .ToListAsync();

            var complianceTagItemIds = complianceArpTags.Select(x => x.ItemPlanningTagId).Distinct().ToList();
            var compliancePlanningTagNames = await itemsPlanningPnDbContext.PlanningTags
                .Where(x => complianceTagItemIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name);

            // Batch-load compliance plannings so we can read description from Planning.
            // Soft-deleted plannings are INCLUDED — see comment on planningsDict
            // above (line ~300) for the same rationale.
            var compliancePlanningsDict = await itemsPlanningPnDbContext.Plannings
                .Where(x => compliancePlanningIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id);

            // Batch-load the SDK Cases backing these compliance rows so the
            // response can report Completed = (case.Status == 100) per
            // occurrence. Without this lookup the calendar UI's drag/resize
            // gate on task.completed would never fire and completed
            // compliance occurrences would remain visually editable until
            // rejected by the backend guards in MoveTask/ResizeTask.
            var weekComplianceCaseIds = compliances
                .Select(c => c.MicrotingSdkCaseId)
                .Where(id => id > 0)
                .Distinct()
                .ToList();
            var weekComplianceCasesById = new Dictionary<int, Microting.eForm.Infrastructure.Data.Entities.Case>();
            if (weekComplianceCaseIds.Count > 0)
            {
                var sdkCoreForCompletion = await coreHelper.GetCore().ConfigureAwait(false);
                var sdkDbContextForCompletion = sdkCoreForCompletion.DbContextHelper.GetDbContext();
                weekComplianceCasesById = await sdkDbContextForCompletion.Cases
                    .Where(c => weekComplianceCaseIds.Contains(c.Id))
                    .ToDictionaryAsync(c => c.Id);
            }

            foreach (var compliance in compliances)
            {
                complianceArpDict.TryGetValue(compliance.PlanningId, out var arp);
                if (arp == null) continue;
                CalendarConfiguration calConfig = null;
                complianceCalConfigs.TryGetValue(arp.Id, out calConfig);

                var title = compliance.ItemName ?? "";
                if (arp?.AreaRule?.AreaRuleTranslations != null)
                {
                    title = arp.AreaRule.AreaRuleTranslations
                        .Where(t => t.LanguageId == userLanguageId)
                        .Select(t => t.Name)
                        .FirstOrDefault() ?? title;
                }

                var tags = arp != null
                    ? complianceArpTags
                        .Where(x => x.AreaRulePlanningId == arp.Id)
                        .Select(x => compliancePlanningTagNames.TryGetValue(x.ItemPlanningTagId, out var name) ? name : null)
                        .Where(x => x != null)
                        .ToList()
                    : [];

                var compIsRepeatAlways = arp?.RepeatType.HasValue == true && arp.RepeatType.Value == 1 && (arp.RepeatEvery ?? 0) == 0;
                var compHasNonAlwaysRepeat = arp?.RepeatType.HasValue == true && arp.RepeatType.Value > 0 && !compIsRepeatAlways;
                var compIsAllDay = calConfig == null && !compHasNonAlwaysRepeat;

                var complianceCompleted = compliance.MicrotingSdkCaseId > 0
                    && weekComplianceCasesById.TryGetValue(compliance.MicrotingSdkCaseId, out var weekSdkCase)
                    && weekSdkCase.Status == 100;

                // Apply any "this"-scope move/resize exception that overrides this past
                // compliance occurrence's date / start-hour. Without this consultation, a
                // user-applied move via MoveTask scope='this' would write an exception that
                // the compliance loop never reads, and the event would snap back to
                // compliance.Deadline on the next fetch. Mirrors the recurrence-expansion
                // loop's exception handling above (around line 313).
                CalendarOccurrenceException complianceException = null;
                if (arp != null
                    && exceptionsByArp.TryGetValue(arp.Id, out var complianceArpExceptions))
                {
                    complianceArpExceptions.TryGetValue(compliance.Deadline.Date, out complianceException);
                }

                // Soft-deleted occurrence: hide it.
                if (complianceException?.IsDeleted == true) continue;

                // Moved out of the current week: hide it here (the destination week's
                // movedInExceptions pass at line ~387 renders it).
                if (complianceException?.NewDate is { } movedDate
                    && (movedDate < weekStart || movedDate > weekEnd))
                {
                    continue;
                }

                var effectiveTaskDate = complianceException?.NewDate?.Date ?? compliance.Deadline.Date;
                var effectiveStartHour = complianceException?.StartHour ?? calConfig?.StartHour ?? 9.0;
                var effectiveDuration = complianceException?.Duration ?? calConfig?.Duration ?? 1.0;

                var model = new CalendarTaskResponseModel
                {
                    Id = arp?.Id ?? 0,
                    Title = title,
                    StartHour = compIsAllDay ? 0 : effectiveStartHour,
                    Duration = compIsAllDay ? 0 : effectiveDuration,
                    TaskDate = effectiveTaskDate.ToString("yyyy-MM-dd"),
                    Tags = tags,
                    AssigneeIds = arp?.PlanningSites?
                        .Where(ps => ps.WorkflowState != Constants.WorkflowStates.Removed)
                        .Select(ps => (int)ps.SiteId)
                        .ToList() ?? [],
                    BoardId = calConfig?.BoardId ?? defaultBoardId,
                    Color = calConfig?.Color,
                    RepeatType = arp?.RepeatType ?? 0,
                    RepeatEvery = arp?.RepeatEvery ?? 1,
                    RepeatEndMode = arp?.RepeatEndMode,
                    RepeatOccurrences = arp?.RepeatOccurrences,
                    RepeatUntilDate = arp?.RepeatUntilDate,
                    DayOfWeek = arp?.DayOfWeek,
                    DayOfMonth = arp?.DayOfMonth,
                    RepeatOrdinalWeek = arp?.RepeatOrdinalWeek,
                    RepeatWeekdaysCsv = arp?.RepeatWeekdaysCsv,
                    Completed = complianceCompleted,
                    // Orphan compliance rows (no live ARP) render as dimmed
                    // inactive — visually distinct from a healthy active row.
                    Status = arp?.Status ?? false,
                    ComplianceEnabled = arp?.ComplianceEnabled ?? false,
                    PropertyId = compliance.PropertyId,
                    ComplianceId = compliance.Id,
                    IsFromCompliance = true,
                    Deadline = compliance.Deadline,
                    PlanningId = compliance.PlanningId,
                    IsAllDay = compIsAllDay,
                    EformId = arp?.AreaRule?.EformId,
                    SdkCaseId = compliance.MicrotingSdkCaseId,
                    ItemPlanningTagId = arp?.ItemPlanningTagId,
                    DescriptionHtml = compliancePlanningsDict.TryGetValue(compliance.PlanningId, out var cp)
                        ? cp.Description
                        : null,
                    Attachments = MapAttachments(arp),
                    ExceptionId = complianceException?.Id,
                };

                ApplyOccurrenceFieldOverrides(model, complianceException);

                if (ShouldIncludeTask(model, requestModel))
                {
                    result.Add(model);
                }
            }

            return new OperationDataResult<List<CalendarTaskResponseModel>>(true, result);
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationCalendarService.GetTasksForWeek: {Message}", e.Message);
            return new OperationDataResult<List<CalendarTaskResponseModel>>(false,
                $"{localizationService.GetString("ErrorWhileGettingCalendarTasks")}: {e.Message}");
        }
    }

    public async Task<OperationDataResult<int>> CreateTask(CalendarTaskCreateRequestModel createModel)
    {
        try
        {
            // Validate: cannot create task in the past
            var taskDateTime = createModel.StartDate.AddHours(createModel.StartHour);
            if (taskDateTime < DateTime.UtcNow)
            {
                return new OperationDataResult<int>(false,
                    localizationService.GetString("CannotCreateTaskInThePast"));
            }

            // Validate: at least one worker must be assigned. Events without
            // an assignee would be downgraded to NotActive by task-wizard and
            // render as a dimmed inactive task with no one to perform it.
            // Reject here with a clear error rather than silently creating
            // an orphan event.
            if (createModel.Sites is null || createModel.Sites.Count == 0)
            {
                return new OperationDataResult<int>(false,
                    localizationService.GetString("AtLeastOneWorkerMustBeAssigned"));
            }

            // Resolve FolderId: if not provided, find or create the "00. Logbøger" folder
            var resolvedFolderId = createModel.FolderId;
            if (resolvedFolderId is null or 0)
            {
                resolvedFolderId = await ResolveOrCreateLogbøgerFolderAsync(createModel.PropertyId);
            }

            // Build TaskWizardCreateModel from the calendar request
            var wizardModel = new TaskWizardCreateModel
            {
                PropertyId = createModel.PropertyId,
                FolderId = resolvedFolderId,
                ItemPlanningTagId = createModel.ItemPlanningTagId,
                TagIds = createModel.TagIds,
                Translates = createModel.Translates,
                EformId = createModel.EformId,
                StartDate = createModel.StartDate,
                RepeatType = (Infrastructure.Enums.RepeatType)createModel.RepeatType,
                RepeatEvery = createModel.RepeatEvery,
                Status = (Infrastructure.Enums.TaskWizardStatuses)createModel.Status,
                Sites = createModel.Sites,
                ComplianceEnabled = createModel.ComplianceEnabled
            };

            var result = await taskWizardService.CreateTask(wizardModel);
            if (!result.Success)
            {
                return new OperationDataResult<int>(false, result.Message);
            }

            // Find the AreaRulePlanning created by TaskWizard for this specific task
            var latestArp = await backendConfigurationPnDbContext.AreaRulePlannings
                .Where(x => x.PropertyId == createModel.PropertyId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Include(x => x.AreaRule)
                .Where(x => x.AreaRule.CreatedInGuide == true)
                .Where(x => x.AreaRule.EformId == createModel.EformId)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync();

            if (latestArp != null)
            {
                // Persist repeat-end and weekday-CSV fields. The CSV column is
                // always written (including null) so changing a multi-day
                // weekly back to a single-day rule clears the stale list. The
                // DayOfMonth column follows the same "always clear stale"
                // rule — switching from monthly back to weekly nukes the
                // previously-saved DOM, so a future switch-back-to-monthly
                // doesn't silently resurrect a stale value.
                var hasRepeatEndChange = createModel.RepeatEndMode.HasValue;
                latestArp.RepeatWeekdaysCsv = createModel.RepeatWeekdaysCsv;
                latestArp.DayOfMonth = createModel.DayOfMonth ?? 0;
                latestArp.RepeatOrdinalWeek = createModel.RepeatOrdinalWeek;
                // Capture the planned weekday from the start date so the
                // monthlyByDay iterator (Nth weekday of month) has the target
                // weekday available, and so a plain weekly rule reports the
                // correct weekday in the edit dialog instead of defaulting to
                // Sunday (DayOfWeek=0) when the FE sends a null weekday CSV (#929).
                if (createModel.RepeatOrdinalWeek.HasValue
                    || createModel.RepeatType == (int)Infrastructure.Enums.RepeatType.Week)
                {
                    latestArp.DayOfWeek = (int)createModel.StartDate.DayOfWeek;
                }
                if (hasRepeatEndChange)
                {
                    latestArp.RepeatEndMode = createModel.RepeatEndMode;
                    latestArp.RepeatOccurrences = createModel.RepeatOccurrences;
                    latestArp.RepeatUntilDate = createModel.RepeatUntilDate;
                }
                await latestArp.Update(backendConfigurationPnDbContext);

                // Persist description on the linked Planning row (not on ARP)
                var planning = await itemsPlanningPnDbContext.Plannings
                    .FirstOrDefaultAsync(x => x.Id == latestArp.ItemPlanningId
                        && x.WorkflowState != Constants.WorkflowStates.Removed);
                if (planning != null)
                {
                    planning.Description = createModel.DescriptionHtml ?? string.Empty;
                    planning.UpdatedByUserId = userService.UserId;
                    await planning.Update(itemsPlanningPnDbContext);
                }

                var calConfig = new CalendarConfiguration
                {
                    AreaRulePlanningId = latestArp.Id,
                    StartHour = createModel.StartHour,
                    Duration = createModel.Duration,
                    BoardId = createModel.BoardId,
                    Color = createModel.Color,
                    CreatedByUserId = userService.UserId,
                    UpdatedByUserId = userService.UserId
                };
                await calConfig.Create(backendConfigurationPnDbContext);
            }

            // latestArp may be null in the rare edge case where TaskWizard
            // succeeded but did not produce an ARP we can correlate to (e.g.
            // EformId resolution skew). Return success with id=0 — frontend
            // treats 0 as "no id, skip post-save uploads".
            return new OperationDataResult<int>(true,
                localizationService.GetString("CalendarTaskCreatedSuccessfully"),
                latestArp?.Id ?? 0);
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationCalendarService.CreateTask: {Message}", e.Message);
            return new OperationDataResult<int>(false,
                $"{localizationService.GetString("ErrorWhileCreatingCalendarTask")}: {e.Message}");
        }
    }

    public async Task<OperationResult> UpdateTask(CalendarTaskUpdateRequestModel updateModel)
    {
        try
        {
            // Validate: cannot update task to the past
            var taskDateTime = updateModel.StartDate.AddHours(updateModel.StartHour);
            if (taskDateTime < DateTime.UtcNow)
            {
                return new OperationResult(false,
                    localizationService.GetString("CannotCreateTaskInThePast"));
            }

            // Validate: at least one worker must remain assigned. Clearing
            // assignees would downgrade the task to NotActive (same as the
            // Create path); reject rather than silently producing an
            // inactive task with no one to perform it.
            if (updateModel.Sites is null || updateModel.Sites.Count == 0)
            {
                return new OperationResult(false,
                    localizationService.GetString("AtLeastOneWorkerMustBeAssigned"));
            }

            // Scope-aware edit (issue #885). "this"/"thisAndFollowing" must NOT
            // relocate the series anchor (which the task wizard's StartDate
            // write does); they record per-occurrence overrides on a
            // CalendarOccurrenceException instead, mirroring MoveTask/
            // ResizeTask/DeleteTask. Only "all" (the default) falls through to
            // the full-series wizard update below.
            var scope = updateModel.Scope ?? "all";

            // Scope only diverges for a recurring series. A one-off event has a
            // single occurrence, so "this"/"thisAndFollowing" are equivalent to
            // "all". The frontend defaults a non-recurring edit's scope to
            // "this" (and always sends originalDate); without this guard such an
            // edit would be stored as an occurrence exception instead of
            // updating the event itself.
            if (scope != "all")
            {
                var isRecurringSeries = await backendConfigurationPnDbContext.AreaRulePlannings
                    .Where(x => x.Id == updateModel.Id)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .AnyAsync(x => x.RepeatType.HasValue && x.RepeatType.Value > 0);
                if (!isRecurringSeries)
                {
                    scope = "all";
                }
            }

            if (scope == "this" && !string.IsNullOrEmpty(updateModel.OriginalDate))
            {
                return await UpdateTaskThisOccurrence(updateModel);
            }
            if (scope == "thisAndFollowing" && !string.IsNullOrEmpty(updateModel.OriginalDate))
            {
                return await UpdateTaskThisAndFollowing(updateModel);
            }

            // scope == "all": full-series update via the wizard. Preserve the
            // series anchor when the edited occurrence's date was NOT changed
            // (a time/field-only edit) — otherwise the wizard would relocate
            // StartDate onto the clicked occurrence and drop earlier
            // occurrences, the same #885 symptom for the "all" case (E08 wants
            // every occurrence, past and future, to reflect the change). A
            // genuine date change, or a one-off (whose anchor == its own date),
            // still flows through to relocate as before.
            var wizardStartDate = updateModel.StartDate;
            if (!string.IsNullOrEmpty(updateModel.OriginalDate))
            {
                var parsedOriginalDate = DateTime.Parse(updateModel.OriginalDate, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).Date;
                if (updateModel.StartDate.Date == parsedOriginalDate)
                {
                    var currentStartDate = await backendConfigurationPnDbContext.AreaRulePlannings
                        .Where(x => x.Id == updateModel.Id)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Select(x => x.StartDate)
                        .FirstOrDefaultAsync();
                    if (currentStartDate.HasValue)
                    {
                        wizardStartDate = currentStartDate.Value;
                    }
                }
            }

            // Delegate to TaskWizard service for full task field updates
            var wizardModel = new TaskWizardCreateModel
            {
                Id = updateModel.Id,
                PropertyId = updateModel.PropertyId,
                FolderId = updateModel.FolderId,
                ItemPlanningTagId = updateModel.ItemPlanningTagId,
                TagIds = updateModel.TagIds,
                Translates = updateModel.Translates,
                EformId = updateModel.EformId,
                StartDate = wizardStartDate,
                RepeatType = (Infrastructure.Enums.RepeatType)updateModel.RepeatType,
                RepeatEvery = updateModel.RepeatEvery,
                Status = (Infrastructure.Enums.TaskWizardStatuses)updateModel.Status,
                Sites = updateModel.Sites,
                ComplianceEnabled = updateModel.ComplianceEnabled
            };

            var wizardResult = await taskWizardService.UpdateTask(wizardModel);
            if (!wizardResult.Success)
            {
                return wizardResult;
            }

            // Persist description on the linked Planning row (not on ARP),
            // plus the repeat-end + multi-day-weekday CSV fields on the ARP.
            // CSV is written unconditionally so switching from a custom
            // multi-day rule back to a single-day rule clears the stale list.
            var arp = await backendConfigurationPnDbContext.AreaRulePlannings
                .FirstOrDefaultAsync(x => x.Id == updateModel.Id
                    && x.WorkflowState != Constants.WorkflowStates.Removed);
            if (arp != null)
            {
                // Write end-mode + recurrence fields unconditionally so
                // switching kinds clears stale state. Same rationale as
                // RepeatWeekdaysCsv above; DayOfMonth follows the same rule.
                arp.RepeatWeekdaysCsv = updateModel.RepeatWeekdaysCsv;
                arp.DayOfMonth = updateModel.DayOfMonth ?? 0;
                arp.RepeatOrdinalWeek = updateModel.RepeatOrdinalWeek;
                // Mirror the create handler: keep DayOfWeek in sync with the
                // start date so the monthlyByDay iterator reads the right
                // target weekday after edits that move the anchor date, and so
                // a plain weekly rule reports the correct weekday in the edit
                // dialog instead of defaulting to Sunday (#929).
                if (updateModel.RepeatOrdinalWeek.HasValue
                    || updateModel.RepeatType == (int)Infrastructure.Enums.RepeatType.Week)
                {
                    arp.DayOfWeek = (int)updateModel.StartDate.DayOfWeek;
                }
                arp.RepeatEndMode = updateModel.RepeatEndMode;
                arp.RepeatOccurrences = updateModel.RepeatOccurrences;
                arp.RepeatUntilDate = updateModel.RepeatUntilDate;
                await arp.Update(backendConfigurationPnDbContext);

                var planning = await itemsPlanningPnDbContext.Plannings
                    .FirstOrDefaultAsync(x => x.Id == arp.ItemPlanningId
                        && x.WorkflowState != Constants.WorkflowStates.Removed);
                if (planning != null)
                {
                    planning.Description = updateModel.DescriptionHtml ?? string.Empty;
                    planning.UpdatedByUserId = userService.UserId;
                    await planning.Update(itemsPlanningPnDbContext);
                }
            }

            // Update or create CalendarConfiguration for calendar-specific fields
            var calConfig = await backendConfigurationPnDbContext.CalendarConfigurations
                .Where(x => x.AreaRulePlanningId == updateModel.Id)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync();

            if (calConfig != null)
            {
                calConfig.StartHour = updateModel.StartHour;
                calConfig.Duration = updateModel.Duration;
                calConfig.BoardId = updateModel.BoardId;
                calConfig.Color = updateModel.Color;
                calConfig.UpdatedByUserId = userService.UserId;
                await calConfig.Update(backendConfigurationPnDbContext);
            }
            else
            {
                calConfig = new CalendarConfiguration
                {
                    AreaRulePlanningId = updateModel.Id,
                    StartHour = updateModel.StartHour,
                    Duration = updateModel.Duration,
                    BoardId = updateModel.BoardId,
                    Color = updateModel.Color,
                    CreatedByUserId = userService.UserId,
                    UpdatedByUserId = userService.UserId
                };
                await calConfig.Create(backendConfigurationPnDbContext);
            }

            return new OperationResult(true,
                localizationService.GetString("CalendarTaskUpdatedSuccessfully"));
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationCalendarService.UpdateTask: {Message}", e.Message);
            return new OperationResult(false,
                $"{localizationService.GetString("ErrorWhileUpdatingCalendarTask")}: {e.Message}");
        }
    }

    // Edit scope="this" (#885): record a single-occurrence override on a
    // CalendarOccurrenceException without touching the series. Reuses an
    // existing exception for the date (e.g. from a prior move/resize) so a
    // date+field edit collapses into one row. Per-occurrence assignees use
    // ExceptionSites; eForm/tags/status/compliance/repeat stay series-level.
    private async Task<OperationResult> UpdateTaskThisOccurrence(CalendarTaskUpdateRequestModel updateModel)
    {
        var originalDate = DateTime.Parse(updateModel.OriginalDate, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).Date;
        var newDate = updateModel.StartDate.Date;
        var title = updateModel.Translates?.FirstOrDefault()?.Name;

        var exception = await backendConfigurationPnDbContext.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == updateModel.Id)
            .Where(x => x.OriginalDate == originalDate)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .FirstOrDefaultAsync();

        if (exception == null)
        {
            exception = new CalendarOccurrenceException
            {
                AreaRulePlanningId = updateModel.Id,
                OriginalDate = originalDate,
                IsDeleted = false,
                NewDate = newDate != originalDate ? newDate : null,
                StartHour = updateModel.StartHour,
                Duration = updateModel.Duration,
                Title = title,
                DescriptionHtml = updateModel.DescriptionHtml,
                BoardId = updateModel.BoardId,
                Color = updateModel.Color,
                CreatedByUserId = userService.UserId,
                UpdatedByUserId = userService.UserId
            };
            await exception.Create(backendConfigurationPnDbContext);
        }
        else
        {
            // Re-editing an occurrence that was previously deleted via
            // scope="this" must bring it back, otherwise the edit is stored
            // but GetTasksForWeek keeps skipping it (IsDeleted gate).
            exception.IsDeleted = false;
            exception.NewDate = newDate != originalDate ? newDate : null;
            exception.StartHour = updateModel.StartHour;
            exception.Duration = updateModel.Duration;
            exception.Title = title;
            exception.DescriptionHtml = updateModel.DescriptionHtml;
            exception.BoardId = updateModel.BoardId;
            exception.Color = updateModel.Color;
            exception.UpdatedByUserId = userService.UserId;
            await exception.Update(backendConfigurationPnDbContext);
        }

        // Replace the per-occurrence assignee set with the edited Sites so
        // this occurrence reflects the change without mutating the series.
        var currentSites = await backendConfigurationPnDbContext.CalendarOccurrenceExceptionSites
            .Where(x => x.CalendarOccurrenceExceptionId == exception.Id)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        foreach (var s in currentSites)
        {
            await s.Delete(backendConfigurationPnDbContext);
        }
        foreach (var siteId in updateModel.Sites)
        {
            var exceptionSite = new CalendarOccurrenceExceptionSite
            {
                CalendarOccurrenceExceptionId = exception.Id,
                SiteId = siteId,
                CreatedByUserId = userService.UserId,
                UpdatedByUserId = userService.UserId
            };
            await exceptionSite.Create(backendConfigurationPnDbContext);
        }

        return new OperationResult(true,
            localizationService.GetString("CalendarTaskUpdatedSuccessfully"));
    }

    // Edit scope="thisAndFollowing" (#885): anchor every PAST occurrence with
    // the OLD field values, then apply the NEW values to the series WITHOUT
    // relocating its StartDate (the wizard is given the unchanged anchor), and
    // clear exceptions from originalDate forward so future occurrences render
    // from the updated series. Mirrors the MoveTask thisAndFollowing handler.
    private async Task<OperationResult> UpdateTaskThisAndFollowing(CalendarTaskUpdateRequestModel updateModel)
    {
        var originalDate = DateTime.Parse(updateModel.OriginalDate, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).Date;

        var arp = await backendConfigurationPnDbContext.AreaRulePlannings
            .Include(x => x.AreaRule)
                .ThenInclude(x => x.AreaRuleTranslations)
            .FirstOrDefaultAsync(x => x.Id == updateModel.Id
                && x.WorkflowState != Constants.WorkflowStates.Removed);
        if (arp == null || arp.StartDate == null)
        {
            // No live series (or a malformed row without an anchor) — refuse
            // rather than fall back to the clicked date, which would relocate
            // the series and reintroduce the #885 bug.
            return new OperationResult(false,
                localizationService.GetString("ErrorWhileUpdatingCalendarTask"));
        }

        var planning = await itemsPlanningPnDbContext.Plannings
            .FirstOrDefaultAsync(x => x.Id == arp.ItemPlanningId
                && x.WorkflowState != Constants.WorkflowStates.Removed);
        var calConfig = await backendConfigurationPnDbContext.CalendarConfigurations
            .Where(x => x.AreaRulePlanningId == updateModel.Id)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .FirstOrDefaultAsync();

        // Capture OLD field values before mutating the series so past
        // occurrences keep showing what they showed before the edit.
        var oldTitle = arp.AreaRule?.AreaRuleTranslations?.FirstOrDefault()?.Name;
        var oldDescription = planning?.Description;
        var oldBoardId = calConfig?.BoardId;
        var oldColor = calConfig?.Color;
        var oldStartHour = calConfig?.StartHour ?? 9.0;
        var oldDuration = calConfig?.Duration ?? 1.0;

        if (planning != null)
        {
            var existingPastDates = await backendConfigurationPnDbContext.CalendarOccurrenceExceptions
                .Where(x => x.AreaRulePlanningId == updateModel.Id)
                .Where(x => x.OriginalDate < originalDate)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Select(x => x.OriginalDate)
                .ToListAsync();
            var existingSet = new HashSet<DateTime>(existingPastDates);

            // Dates that already deployed a case (incl. completed ones whose
            // Compliance row was soft-deleted after Status=100) are rendered by
            // GetTasksForWeek's compliance loop. Backfilling an orphan anchor for
            // them would resurrect a completed occurrence as not-done (#930), so
            // skip those dates and only anchor genuinely orphaned past dates.
            var compliancePastDates = new HashSet<DateTime>(
                await backendConfigurationPnDbContext.Compliances
                    .Where(x => x.PlanningId == planning.Id)
                    .Where(x => x.Deadline < originalDate)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed
                                || x.MicrotingSdkCaseId > 0)
                    .Select(x => x.Deadline.Date)
                    .ToListAsync());

            foreach (var occDate in EnumerateOccurrences(planning, planning.StartDate.Date, originalDate,
                         arp.RepeatWeekdaysCsv, arp.RepeatOrdinalWeek, arp.DayOfWeek))
            {
                if (existingSet.Contains(occDate)) continue;
                if (compliancePastDates.Contains(occDate)) continue;
                var anchor = new CalendarOccurrenceException
                {
                    AreaRulePlanningId = updateModel.Id,
                    OriginalDate = occDate,
                    IsDeleted = false,
                    NewDate = null,
                    StartHour = oldStartHour,
                    Duration = oldDuration,
                    Title = oldTitle,
                    DescriptionHtml = oldDescription,
                    BoardId = oldBoardId,
                    Color = oldColor,
                    CreatedByUserId = userService.UserId,
                    UpdatedByUserId = userService.UserId
                };
                await anchor.Create(backendConfigurationPnDbContext);
            }
        }

        // Distinguish a pure field edit from a date move. When the new start
        // date equals the edited occurrence's date, the user only changed
        // fields (title/time/colour) — keep the series anchor where it is so we
        // don't drag the whole series onto the clicked occurrence (#885). When
        // the date actually changed, a "this and following" edit re-anchors the
        // series forward to the new date (exactly like a thisAndFollowing drag):
        // the rule from that date on follows the new weekday/ordinal, while past
        // occurrences stay pinned by the backfill anchors created above. Without
        // this re-anchor the iterator keeps generating from the old anchor and
        // the moved occurrence snaps to a stale weekday (#927).
        var newAnchor = DateTime.SpecifyKind(updateModel.StartDate, DateTimeKind.Utc).Date;
        var dateChanged = newAnchor != originalDate;
        if (dateChanged)
        {
            arp.StartDate = newAnchor;
            arp.UpdatedByUserId = userService.UserId;
            await arp.Update(backendConfigurationPnDbContext);
            if (planning != null)
            {
                planning.StartDate = newAnchor;
                planning.UpdatedByUserId = userService.UserId;
                await planning.Update(itemsPlanningPnDbContext);
            }
        }

        // Apply NEW field values to the series. The wizard re-derives the
        // items-planning weekday/day-of-month from this StartDate, so it must
        // receive the (possibly re-anchored) series start to keep both
        // scheduler masters in agreement.
        var wizardModel = new TaskWizardCreateModel
        {
            Id = updateModel.Id,
            PropertyId = updateModel.PropertyId,
            FolderId = updateModel.FolderId,
            ItemPlanningTagId = updateModel.ItemPlanningTagId,
            TagIds = updateModel.TagIds,
            Translates = updateModel.Translates,
            EformId = updateModel.EformId,
            StartDate = arp.StartDate.Value,
            RepeatType = (Infrastructure.Enums.RepeatType)updateModel.RepeatType,
            RepeatEvery = updateModel.RepeatEvery,
            Status = (Infrastructure.Enums.TaskWizardStatuses)updateModel.Status,
            Sites = updateModel.Sites,
            ComplianceEnabled = updateModel.ComplianceEnabled
        };
        var wizardResult = await taskWizardService.UpdateTask(wizardModel);
        if (!wizardResult.Success)
        {
            return wizardResult;
        }

        arp.RepeatWeekdaysCsv = updateModel.RepeatWeekdaysCsv;
        arp.DayOfMonth = updateModel.DayOfMonth ?? 0;
        arp.RepeatOrdinalWeek = updateModel.RepeatOrdinalWeek;
        if (updateModel.RepeatOrdinalWeek.HasValue
            || updateModel.RepeatType == (int)Infrastructure.Enums.RepeatType.Week)
        {
            arp.DayOfWeek = (int)updateModel.StartDate.DayOfWeek;
        }
        arp.RepeatEndMode = updateModel.RepeatEndMode;
        arp.RepeatOccurrences = updateModel.RepeatOccurrences;
        arp.RepeatUntilDate = updateModel.RepeatUntilDate;
        await arp.Update(backendConfigurationPnDbContext);

        if (planning != null)
        {
            planning.Description = updateModel.DescriptionHtml ?? string.Empty;
            planning.UpdatedByUserId = userService.UserId;
            await planning.Update(itemsPlanningPnDbContext);
        }

        if (calConfig != null)
        {
            calConfig.StartHour = updateModel.StartHour;
            calConfig.Duration = updateModel.Duration;
            calConfig.BoardId = updateModel.BoardId;
            calConfig.Color = updateModel.Color;
            calConfig.UpdatedByUserId = userService.UserId;
            await calConfig.Update(backendConfigurationPnDbContext);
        }
        else
        {
            calConfig = new CalendarConfiguration
            {
                AreaRulePlanningId = updateModel.Id,
                StartHour = updateModel.StartHour,
                Duration = updateModel.Duration,
                BoardId = updateModel.BoardId,
                Color = updateModel.Color,
                CreatedByUserId = userService.UserId,
                UpdatedByUserId = userService.UserId
            };
            await calConfig.Create(backendConfigurationPnDbContext);
        }

        // From the re-anchored series' start forward, the updated series is the
        // source of truth; drop any pre-edit overrides (including past anchors
        // just backfilled for dates the new series now regenerates) so they
        // don't shadow the new values. For a backward date move the new anchor
        // is earlier than originalDate, so the cutoff is the new anchor — else a
        // backfilled anchor at/after the new anchor double-renders the same day
        // with stale values (#927).
        var staleCutoff = dateChanged && newAnchor < originalDate ? newAnchor : originalDate;
        var staleExceptions = await backendConfigurationPnDbContext.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == updateModel.Id)
            .Where(x => x.OriginalDate >= staleCutoff)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        foreach (var stale in staleExceptions)
        {
            await stale.Delete(backendConfigurationPnDbContext);
        }

        return new OperationResult(true,
            localizationService.GetString("CalendarTaskUpdatedSuccessfully"));
    }

    // Overlay a per-occurrence exception's field overrides (#885) onto a
    // rendered task. Null override fields inherit the series value, so this is
    // safe to call for move/resize/delete exceptions that carry no field edits.
    private static void ApplyOccurrenceFieldOverrides(CalendarTaskResponseModel model, CalendarOccurrenceException exception)
    {
        if (exception == null) return;
        if (!string.IsNullOrEmpty(exception.Title)) model.Title = exception.Title;
        if (exception.DescriptionHtml != null) model.DescriptionHtml = exception.DescriptionHtml;
        if (exception.BoardId.HasValue) model.BoardId = exception.BoardId;
        if (!string.IsNullOrEmpty(exception.Color)) model.Color = exception.Color;
    }

    public async Task<OperationResult> DeleteTask(CalendarTaskDeleteRequestModel deleteModel)
    {
        try
        {
            var scope = deleteModel.Scope ?? "all";

            if (scope == "this" && !string.IsNullOrEmpty(deleteModel.OriginalDate))
            {
                var originalDate = DateTime.Parse(deleteModel.OriginalDate, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).Date;

                var exceptions = await backendConfigurationPnDbContext.CalendarOccurrenceExceptions
                    .Where(x => x.AreaRulePlanningId == deleteModel.Id)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .ToListAsync();

                // Match the occurrence's own exception by OriginalDate first. If the
                // occurrence was previously moved (scope=this), it now renders at its
                // NewDate and the delete payload sends that displayed date — so fall
                // back to matching by NewDate, flipping the SAME row's IsDeleted instead
                // of creating a duplicate exception that leaves the moved occurrence
                // visible (#915).
                var existing = exceptions.FirstOrDefault(x => x.OriginalDate.Date == originalDate)
                    ?? exceptions.FirstOrDefault(x => x.NewDate.HasValue && x.NewDate.Value.Date == originalDate);

                if (existing != null)
                {
                    existing.IsDeleted = true;
                    existing.UpdatedByUserId = userService.UserId;
                    await existing.Update(backendConfigurationPnDbContext);
                }
                else
                {
                    var exception = new CalendarOccurrenceException
                    {
                        AreaRulePlanningId = deleteModel.Id,
                        OriginalDate = originalDate,
                        IsDeleted = true,
                        CreatedByUserId = userService.UserId,
                        UpdatedByUserId = userService.UserId
                    };
                    await exception.Create(backendConfigurationPnDbContext);
                }

                return new OperationResult(true,
                    localizationService.GetString("CalendarTaskDeletedSuccessfully"));
            }
            else if (scope == "thisAndFollowing" && !string.IsNullOrEmpty(deleteModel.OriginalDate))
            {
                var originalDate = DateTime.Parse(deleteModel.OriginalDate, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).Date;

                var arp = await backendConfigurationPnDbContext.AreaRulePlannings
                    .Where(x => x.Id == deleteModel.Id)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .FirstOrDefaultAsync();

                if (arp == null)
                {
                    return new OperationResult(false,
                        localizationService.GetString("AreaRulePlanningNotFound"));
                }

                if (originalDate <= arp.StartDate)
                {
                    return await DeleteEntireSeries(deleteModel.Id);
                }

                arp.EndDate = originalDate.AddDays(-1);
                arp.UpdatedByUserId = userService.UserId;
                await arp.Update(backendConfigurationPnDbContext);

                var planning = await itemsPlanningPnDbContext.Plannings
                    .Where(x => x.Id == arp.ItemPlanningId)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .FirstOrDefaultAsync();

                if (planning != null)
                {
                    planning.RepeatUntil = originalDate.AddDays(-1);
                    planning.UpdatedByUserId = userService.UserId;
                    await planning.Update(itemsPlanningPnDbContext);
                }

                var staleExceptions = await backendConfigurationPnDbContext.CalendarOccurrenceExceptions
                    .Where(x => x.AreaRulePlanningId == deleteModel.Id)
                    .Where(x => x.OriginalDate >= originalDate)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .ToListAsync();

                foreach (var stale in staleExceptions)
                {
                    await stale.Delete(backendConfigurationPnDbContext);
                }

                return new OperationResult(true,
                    localizationService.GetString("CalendarTaskDeletedSuccessfully"));
            }
            else
            {
                return await DeleteEntireSeries(deleteModel.Id);
            }
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationCalendarService.DeleteTask: {Message}", e.Message);
            return new OperationResult(false,
                $"{localizationService.GetString("ErrorWhileDeletingCalendarTask")}: {e.Message}");
        }
    }

    private async Task<OperationResult> DeleteEntireSeries(int arpId)
    {
        var calConfig = await backendConfigurationPnDbContext.CalendarConfigurations
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .FirstOrDefaultAsync();

        if (calConfig != null)
        {
            await calConfig.Delete(backendConfigurationPnDbContext);
        }

        var exceptions = await backendConfigurationPnDbContext.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();

        foreach (var ex in exceptions)
        {
            await ex.Delete(backendConfigurationPnDbContext);
        }

        var wizardResult = await taskWizardService.DeleteTask(arpId);
        if (!wizardResult.Success)
        {
            return wizardResult;
        }

        return new OperationResult(true,
            localizationService.GetString("CalendarTaskDeletedSuccessfully"));
    }

    // True iff the (planning, day) occurrence is backed by an SDK Case whose
    // Status == 100 (canonical "done" code; see e.g. line ~2500 in this file
    // and BackendConfigurationCompliancesService.cs). Non-compliance recurring
    // events have no backing case and always return false.
    //
    // Compliance.Deadline is a non-Kind DateTime; occurrenceDate arrives Kind=Utc
    // from the request parser. To avoid Kind-drift around UTC offsets / DST, we
    // pull a 3-day window around the target day and filter the exact match
    // in-memory by `Deadline.Date == occurrenceDate.Date`.
    private async Task<bool> IsTaskOccurrenceCompleted(int planningId, DateTime occurrenceDate)
    {
        var windowStart = occurrenceDate.Date.AddDays(-1);
        var windowEnd = occurrenceDate.Date.AddDays(2);
        var candidates = await backendConfigurationPnDbContext.Compliances
            .Where(c => c.PlanningId == planningId)
            .Where(c => c.Deadline >= windowStart && c.Deadline < windowEnd)
            .Where(c => c.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        var compliance = candidates.FirstOrDefault(c => c.Deadline.Date == occurrenceDate.Date);
        if (compliance == null || compliance.MicrotingSdkCaseId <= 0) return false;

        var sdkCore = await coreHelper.GetCore().ConfigureAwait(false);
        var sdkDbContext = sdkCore.DbContextHelper.GetDbContext();
        var sdkCase = await sdkDbContext.Cases
            .Where(c => c.Id == compliance.MicrotingSdkCaseId)
            .FirstOrDefaultAsync();
        return sdkCase?.Status == 100;
    }

    public async Task<OperationResult> MoveTask(CalendarTaskMoveRequestModel moveModel)
    {
        try
        {
            var newDate = DateTime.Parse(moveModel.NewDate, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

            var taskDateTime = newDate.AddHours(moveModel.NewStartHour);

            var arp = await backendConfigurationPnDbContext.AreaRulePlannings
                .Where(x => x.Id == moveModel.Id)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync();

            if (arp == null)
            {
                return new OperationResult(false,
                    localizationService.GetString("AreaRulePlanningNotFound"));
            }

            // Defence-in-depth move guards. The frontend already prevents
            // both cases (drag handle hidden on completed tasks, drop
            // rejected when a future task is dropped before now), but a
            // direct API call could bypass that.
            if (!string.IsNullOrEmpty(moveModel.OriginalDate))
            {
                var origDate = DateTime.Parse(moveModel.OriginalDate,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).Date;

                if (await IsTaskOccurrenceCompleted(arp.ItemPlanningId, origDate))
                {
                    return new OperationResult(false,
                        localizationService.GetString("CannotMoveCompletedTask"));
                }

                var oldCalConfig = await backendConfigurationPnDbContext.CalendarConfigurations
                    .Where(x => x.AreaRulePlanningId == moveModel.Id)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .FirstOrDefaultAsync();
                var origStartHour = oldCalConfig?.StartHour ?? 9.0;
                var origDateTime = origDate.AddHours(origStartHour);
                var nowUtc = DateTime.UtcNow;
                if (origDateTime >= nowUtc && taskDateTime < nowUtc)
                {
                    return new OperationResult(false,
                        localizationService.GetString("CannotMoveFutureTaskToPast"));
                }
            }

            var scope = moveModel.Scope ?? "all";

            if (scope == "this" && !string.IsNullOrEmpty(moveModel.OriginalDate))
            {
                var originalDate = DateTime.Parse(moveModel.OriginalDate, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).Date;

                // The frontend sends the DISPLAYED date as OriginalDate. Prefer
                // an exception keyed on that natural date; otherwise (if a prior
                // scope=this move relocated a DIFFERENT occurrence onto the
                // displayed date) reuse THAT move's exception, matched by its
                // NewDate. Without the NewDate fallback, dragging an
                // already-moved occurrence again — or back to its natural day —
                // orphans the original row and the tile never moves (#953,
                // mirrors the delete-after-move fix #915).
                var exception = await backendConfigurationPnDbContext.CalendarOccurrenceExceptions
                    .Where(x => x.AreaRulePlanningId == moveModel.Id)
                    .Where(x => x.OriginalDate == originalDate)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .FirstOrDefaultAsync();

                if (exception == null)
                {
                    // No natural-keyed exception: look for an occurrence a prior
                    // move relocated onto this displayed date. The
                    // OriginalDate != displayedDate guard keeps this strictly to
                    // moved-away occurrences (a same-day override stores
                    // NewDate = null, so it is never matched here anyway).
                    exception = await backendConfigurationPnDbContext.CalendarOccurrenceExceptions
                        .Where(x => x.AreaRulePlanningId == moveModel.Id)
                        .Where(x => x.NewDate.HasValue && x.NewDate.Value.Date == originalDate
                                    && x.OriginalDate.Date != originalDate)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .FirstOrDefaultAsync();
                }

                if (exception != null)
                {
                    // Clear the override when the occurrence lands back on its
                    // own natural day; otherwise relocate it.
                    exception.NewDate = newDate.Date != exception.OriginalDate.Date ? newDate : null;
                    exception.StartHour = moveModel.NewStartHour;
                    exception.UpdatedByUserId = userService.UserId;
                    await exception.Update(backendConfigurationPnDbContext);
                }
                else
                {
                    exception = new CalendarOccurrenceException
                    {
                        AreaRulePlanningId = moveModel.Id,
                        OriginalDate = originalDate,
                        IsDeleted = false,
                        NewDate = newDate.Date != originalDate ? newDate : null,
                        StartHour = moveModel.NewStartHour,
                        CreatedByUserId = userService.UserId,
                        UpdatedByUserId = userService.UserId
                    };
                    await exception.Create(backendConfigurationPnDbContext);
                }
            }
            else if (scope == "thisAndFollowing" && !string.IsNullOrEmpty(moveModel.OriginalDate))
            {
                var originalDate = DateTime.Parse(moveModel.OriginalDate, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).Date;

                // Anchor every PAST occurrence with an exception holding the
                // OLD calConfig values BEFORE we shift planning.StartDate
                // forward. Past occurrences are then rendered by
                // GetTasksForWeek's orphan-exception branch (the recurrence
                // rule will no longer generate them after the shift).
                var oldPlanning = await itemsPlanningPnDbContext.Plannings
                    .Where(x => x.Id == arp.ItemPlanningId)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .FirstOrDefaultAsync();
                var oldCalConfig = await backendConfigurationPnDbContext.CalendarConfigurations
                    .Where(x => x.AreaRulePlanningId == moveModel.Id)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .FirstOrDefaultAsync();

                if (oldPlanning != null)
                {
                    var oldStartHour = oldCalConfig?.StartHour ?? 9.0;
                    var oldDuration = oldCalConfig?.Duration ?? 1.0;

                    var existingPastDates = await backendConfigurationPnDbContext.CalendarOccurrenceExceptions
                        .Where(x => x.AreaRulePlanningId == moveModel.Id)
                        .Where(x => x.OriginalDate < originalDate)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Select(x => x.OriginalDate)
                        .ToListAsync();
                    var existingSet = new HashSet<DateTime>(existingPastDates);

                    // Skip dates that already deployed a case (incl. completed
                    // ones whose Compliance row was soft-deleted after
                    // Status=100): the compliance loop renders those, so an
                    // orphan anchor here would resurrect a completed occurrence
                    // as not-done (#930).
                    var compliancePastDates = new HashSet<DateTime>(
                        await backendConfigurationPnDbContext.Compliances
                            .Where(x => x.PlanningId == oldPlanning.Id)
                            .Where(x => x.Deadline < originalDate)
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed
                                        || x.MicrotingSdkCaseId > 0)
                            .Select(x => x.Deadline.Date)
                            .ToListAsync());

                    foreach (var occDate in EnumerateOccurrences(oldPlanning, oldPlanning.StartDate.Date, originalDate, arp.RepeatWeekdaysCsv, arp.RepeatOrdinalWeek, arp.DayOfWeek))
                    {
                        if (existingSet.Contains(occDate)) continue;
                        if (compliancePastDates.Contains(occDate)) continue;
                        var anchor = new CalendarOccurrenceException
                        {
                            AreaRulePlanningId = moveModel.Id,
                            OriginalDate = occDate,
                            IsDeleted = false,
                            NewDate = null,
                            StartHour = oldStartHour,
                            Duration = oldDuration,
                            CreatedByUserId = userService.UserId,
                            UpdatedByUserId = userService.UserId
                        };
                        await anchor.Create(backendConfigurationPnDbContext);
                    }
                }
                else
                {
                    logger.LogWarning(
                        "MoveTask thisAndFollowing backfill skipped: planning {ItemPlanningId} for AreaRulePlanning {ArpId} not found",
                        arp.ItemPlanningId, moveModel.Id);
                }

                arp.StartDate = newDate;
                // Re-derive the recurrence weekday from the drop date so the
                // moved series follows the new weekday instead of snapping back
                // to the original one (#926). For an Nth-weekday-of-month rule
                // also re-derive the ordinal week; for a single-day weekly rule
                // rewrite the explicit weekday CSV. Multi-day weekly CSVs are
                // left untouched (a single drag has no single target weekday).
                if (arp.RepeatOrdinalWeek.HasValue)
                {
                    arp.DayOfWeek = (int)newDate.DayOfWeek;
                    arp.RepeatOrdinalWeek = (newDate.Day - 1) / 7 + 1;
                }
                else if (!string.IsNullOrEmpty(arp.RepeatWeekdaysCsv)
                         && !arp.RepeatWeekdaysCsv.Contains(','))
                {
                    arp.RepeatWeekdaysCsv = ((int)newDate.DayOfWeek).ToString(CultureInfo.InvariantCulture);
                    arp.DayOfWeek = (int)newDate.DayOfWeek;
                }
                // Yearly rules anchor on a fixed (month, day-of-month). The
                // month follows StartDate, but the day-of-month is read from
                // DayOfMonth by the yearly render; carry it to the drop day or
                // the occurrence re-pins to the original day and snaps back
                // (#952).
                if (arp.RepeatType == (int)Infrastructure.Enums.RepeatType.Year)
                {
                    arp.DayOfMonth = newDate.Day;
                }
                arp.UpdatedByUserId = userService.UserId;
                await arp.Update(backendConfigurationPnDbContext);

                if (oldPlanning != null)
                {
                    oldPlanning.StartDate = newDate;
                    // Keep the items-planning scheduler master in sync with the
                    // new weekday so NextExecutionTime does not pull the series
                    // back to the old day (the two-master defect, #925/#926).
                    oldPlanning.DayOfWeek = newDate.DayOfWeek;
                    if (arp.RepeatType == (int)Infrastructure.Enums.RepeatType.Year)
                    {
                        oldPlanning.DayOfMonth = newDate.Day;
                    }
                    oldPlanning.UpdatedByUserId = userService.UserId;
                    await oldPlanning.Update(itemsPlanningPnDbContext);
                }

                if (oldCalConfig != null)
                {
                    oldCalConfig.StartHour = moveModel.NewStartHour;
                    oldCalConfig.UpdatedByUserId = userService.UserId;
                    await oldCalConfig.Update(backendConfigurationPnDbContext);
                }
                else
                {
                    var calConfig = new CalendarConfiguration
                    {
                        AreaRulePlanningId = moveModel.Id,
                        StartHour = moveModel.NewStartHour,
                        Duration = 1.0,
                        CreatedByUserId = userService.UserId,
                        UpdatedByUserId = userService.UserId
                    };
                    await calConfig.Create(backendConfigurationPnDbContext);
                }

                // For a backward move the new anchor precedes originalDate, so
                // drop backfilled anchors at/after the new anchor too — otherwise
                // they double-render the re-anchored series' own days with stale
                // values (companion to the #927 cutoff fix).
                var staleCutoff = newDate < originalDate ? newDate : originalDate;
                var staleExceptions = await backendConfigurationPnDbContext.CalendarOccurrenceExceptions
                    .Where(x => x.AreaRulePlanningId == moveModel.Id)
                    .Where(x => x.OriginalDate >= staleCutoff)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .ToListAsync();

                foreach (var stale in staleExceptions)
                {
                    await stale.Delete(backendConfigurationPnDbContext);
                }
            }
            else
            {
                arp.StartDate = newDate;
                // Re-derive the recurrence weekday/ordinal from the drop date so
                // a whole-series move follows the new weekday instead of snapping
                // back to the original one (#926), mirroring the thisAndFollowing
                // branch above.
                if (arp.RepeatOrdinalWeek.HasValue)
                {
                    arp.DayOfWeek = (int)newDate.DayOfWeek;
                    arp.RepeatOrdinalWeek = (newDate.Day - 1) / 7 + 1;
                }
                else if (!string.IsNullOrEmpty(arp.RepeatWeekdaysCsv)
                         && !arp.RepeatWeekdaysCsv.Contains(','))
                {
                    arp.RepeatWeekdaysCsv = ((int)newDate.DayOfWeek).ToString(CultureInfo.InvariantCulture);
                    arp.DayOfWeek = (int)newDate.DayOfWeek;
                }
                // Carry the yearly day-of-month to the drop day (#952); see the
                // thisAndFollowing branch above for the rationale.
                if (arp.RepeatType == (int)Infrastructure.Enums.RepeatType.Year)
                {
                    arp.DayOfMonth = newDate.Day;
                }
                arp.UpdatedByUserId = userService.UserId;
                await arp.Update(backendConfigurationPnDbContext);

                var planning = await itemsPlanningPnDbContext.Plannings
                    .Where(x => x.Id == arp.ItemPlanningId)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .FirstOrDefaultAsync();

                if (planning != null)
                {
                    planning.StartDate = newDate;
                    planning.DayOfWeek = newDate.DayOfWeek;
                    if (arp.RepeatType == (int)Infrastructure.Enums.RepeatType.Year)
                    {
                        planning.DayOfMonth = newDate.Day;
                    }
                    planning.UpdatedByUserId = userService.UserId;
                    await planning.Update(itemsPlanningPnDbContext);
                }

                var calConfig = await backendConfigurationPnDbContext.CalendarConfigurations
                    .Where(x => x.AreaRulePlanningId == moveModel.Id)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .FirstOrDefaultAsync();

                if (calConfig != null)
                {
                    calConfig.StartHour = moveModel.NewStartHour;
                    calConfig.UpdatedByUserId = userService.UserId;
                    await calConfig.Update(backendConfigurationPnDbContext);
                }
                else
                {
                    calConfig = new CalendarConfiguration
                    {
                        AreaRulePlanningId = moveModel.Id,
                        StartHour = moveModel.NewStartHour,
                        Duration = 1.0,
                        CreatedByUserId = userService.UserId,
                        UpdatedByUserId = userService.UserId
                    };
                    await calConfig.Create(backendConfigurationPnDbContext);
                }

                var allExceptions = await backendConfigurationPnDbContext.CalendarOccurrenceExceptions
                    .Where(x => x.AreaRulePlanningId == moveModel.Id)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .ToListAsync();

                foreach (var ex in allExceptions)
                {
                    await ex.Delete(backendConfigurationPnDbContext);
                }
            }

            // #954: a series-level move ("thisAndFollowing"/"all") shifts the
            // recurrence anchor but leaves the dragged occurrence's Compliance
            // row at its old Deadline, so the compliance loop keeps painting a
            // tile on the old day while the recurrence paints one on the new day
            // — a duplicate. Carry that occurrence's Compliance row to the new
            // date so a single tile remains (the dedup gate then suppresses the
            // recurrence tile on that day). Scope "this" is excluded: it is
            // rendered through the per-occurrence exception the compliance loop
            // already consults. The move guard above rejected completed
            // occurrences, so the row at originalDate is open; Compliance rows on
            // other dates (incl. completed history) stay pinned. A 3-day window
            // + in-memory .Date match avoids DateTime.Kind drift (mirrors
            // IsTaskOccurrenceCompleted).
            if (scope != "this" && !string.IsNullOrEmpty(moveModel.OriginalDate))
            {
                var origComplianceDate = DateTime.Parse(moveModel.OriginalDate,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).Date;
                var windowStart = origComplianceDate.AddDays(-1);
                var windowEnd = origComplianceDate.AddDays(2);
                var complianceCandidates = await backendConfigurationPnDbContext.Compliances
                    .Where(x => x.PlanningId == arp.ItemPlanningId)
                    .Where(x => x.Deadline >= windowStart && x.Deadline < windowEnd)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .ToListAsync();
                var complianceAtOriginalDate = complianceCandidates
                    .Where(c => c.Deadline.Date == origComplianceDate)
                    .ToList();

                // Never relocate a COMPLETED occurrence's row: completed history
                // stays pinned to its date even if (data anomaly) it shares the
                // day with the open occurrence the move guard cleared. The guard
                // only inspects one row per day (FirstOrDefault), so re-check
                // completion here against the backing SDK case (Status == 100).
                var caseIds = complianceAtOriginalDate
                    .Select(c => c.MicrotingSdkCaseId)
                    .Where(id => id > 0)
                    .Distinct()
                    .ToList();
                var completedCaseIds = new HashSet<int>();
                if (caseIds.Count > 0)
                {
                    var sdkCore = await coreHelper.GetCore().ConfigureAwait(false);
                    await using var sdkDbContext = sdkCore.DbContextHelper.GetDbContext();
                    completedCaseIds = (await sdkDbContext.Cases
                        .Where(c => caseIds.Contains(c.Id) && c.Status == 100)
                        .Select(c => c.Id)
                        .ToListAsync()).ToHashSet();
                }

                foreach (var compliance in complianceAtOriginalDate
                             .Where(c => !(c.MicrotingSdkCaseId > 0
                                           && completedCaseIds.Contains(c.MicrotingSdkCaseId))))
                {
                    compliance.Deadline = newDate.Date;
                    compliance.UpdatedByUserId = userService.UserId;
                    await compliance.Update(backendConfigurationPnDbContext);
                }
            }

            return new OperationResult(true,
                localizationService.GetString("CalendarTaskMovedSuccessfully"));
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationCalendarService.MoveTask: {Message}", e.Message);
            return new OperationResult(false,
                $"{localizationService.GetString("ErrorWhileMovingCalendarTask")}: {e.Message}");
        }
    }

    public async Task<OperationResult> ResizeTask(CalendarTaskResizeRequestModel resizeModel)
    {
        try
        {
            // Resize is allowed on past events (extending a currently-running
            // task is legitimate). The block is on completed tasks — their
            // outcome is already recorded and the duration should not shift
            // retroactively.

            var arp = await backendConfigurationPnDbContext.AreaRulePlannings
                .Where(x => x.Id == resizeModel.Id)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync();

            if (arp == null)
            {
                return new OperationResult(false,
                    localizationService.GetString("AreaRulePlanningNotFound"));
            }

            if (!string.IsNullOrEmpty(resizeModel.OriginalDate))
            {
                var origDate = DateTime.Parse(resizeModel.OriginalDate,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).Date;
                if (await IsTaskOccurrenceCompleted(arp.ItemPlanningId, origDate))
                {
                    return new OperationResult(false,
                        localizationService.GetString("CannotMoveCompletedTask"));
                }
            }

            var scope = resizeModel.Scope ?? "all";

            if (scope == "this" && !string.IsNullOrEmpty(resizeModel.OriginalDate))
            {
                var originalDate = DateTime.Parse(resizeModel.OriginalDate, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).Date;

                var exception = await backendConfigurationPnDbContext.CalendarOccurrenceExceptions
                    .Where(x => x.AreaRulePlanningId == resizeModel.Id)
                    .Where(x => x.OriginalDate == originalDate)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .FirstOrDefaultAsync();

                if (exception != null)
                {
                    exception.StartHour = resizeModel.NewStartHour;
                    exception.Duration = resizeModel.NewDuration;
                    exception.UpdatedByUserId = userService.UserId;
                    await exception.Update(backendConfigurationPnDbContext);
                }
                else
                {
                    exception = new CalendarOccurrenceException
                    {
                        AreaRulePlanningId = resizeModel.Id,
                        OriginalDate = originalDate,
                        IsDeleted = false,
                        NewDate = null, // resize does not change the date
                        StartHour = resizeModel.NewStartHour,
                        Duration = resizeModel.NewDuration,
                        CreatedByUserId = userService.UserId,
                        UpdatedByUserId = userService.UserId
                    };
                    await exception.Create(backendConfigurationPnDbContext);
                }
            }
            else
            {
                // 'thisAndFollowing' and 'all' both update the series-wide
                // CalendarConfiguration; we deliberately do NOT touch
                // arp.StartDate or planning.StartDate (resize is not a move).
                var calConfig = await backendConfigurationPnDbContext.CalendarConfigurations
                    .Where(x => x.AreaRulePlanningId == resizeModel.Id)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .FirstOrDefaultAsync();

                // For 'thisAndFollowing', anchor every PAST occurrence to its
                // CURRENT (pre-resize) StartHour/Duration before we mutate
                // calConfig — otherwise past occurrences without their own
                // exception row would resolve through the new calConfig and
                // visually shift to the new times. (See GetTasksForWeek's
                // `exception ?? calConfig ?? defaults` resolution chain.)
                if (scope == "thisAndFollowing" && !string.IsNullOrEmpty(resizeModel.OriginalDate))
                {
                    var anchorDate = DateTime.Parse(resizeModel.OriginalDate, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).Date;
                    var oldStartHour = calConfig?.StartHour ?? 9.0;
                    var oldDuration = calConfig?.Duration ?? 1.0;

                    var planning = await itemsPlanningPnDbContext.Plannings
                        .Where(x => x.Id == arp.ItemPlanningId)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .FirstOrDefaultAsync();

                    if (planning == null)
                    {
                        // calConfig is about to be updated; without anchors, past
                        // occurrences will silently shift to the new times. Log so
                        // the issue is observable rather than invisible.
                        logger.LogWarning(
                            "ResizeTask thisAndFollowing backfill skipped: planning {ItemPlanningId} for AreaRulePlanning {ArpId} not found",
                            arp.ItemPlanningId, resizeModel.Id);
                    }
                    else
                    {
                        var existingPastDates = await backendConfigurationPnDbContext.CalendarOccurrenceExceptions
                            .Where(x => x.AreaRulePlanningId == resizeModel.Id)
                            .Where(x => x.OriginalDate < anchorDate)
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                            .Select(x => x.OriginalDate)
                            .ToListAsync();
                        var existingSet = new HashSet<DateTime>(existingPastDates);

                        foreach (var occDate in EnumerateOccurrences(planning, planning.StartDate.Date, anchorDate, arp.RepeatWeekdaysCsv, arp.RepeatOrdinalWeek, arp.DayOfWeek))
                        {
                            if (existingSet.Contains(occDate)) continue;
                            var anchor = new CalendarOccurrenceException
                            {
                                AreaRulePlanningId = resizeModel.Id,
                                OriginalDate = occDate,
                                IsDeleted = false,
                                NewDate = null,
                                StartHour = oldStartHour,
                                Duration = oldDuration,
                                CreatedByUserId = userService.UserId,
                                UpdatedByUserId = userService.UserId
                            };
                            await anchor.Create(backendConfigurationPnDbContext);
                        }
                    }
                }

                if (calConfig != null)
                {
                    calConfig.StartHour = resizeModel.NewStartHour;
                    calConfig.Duration = resizeModel.NewDuration;
                    calConfig.UpdatedByUserId = userService.UserId;
                    await calConfig.Update(backendConfigurationPnDbContext);
                }
                else
                {
                    calConfig = new CalendarConfiguration
                    {
                        AreaRulePlanningId = resizeModel.Id,
                        StartHour = resizeModel.NewStartHour,
                        Duration = resizeModel.NewDuration,
                        CreatedByUserId = userService.UserId,
                        UpdatedByUserId = userService.UserId
                    };
                    await calConfig.Create(backendConfigurationPnDbContext);
                }

                // For thisAndFollowing, drop per-occurrence overrides from
                // OriginalDate forward — they are superseded by the new
                // series-wide values. For 'all', drop every override.
                IQueryable<CalendarOccurrenceException> staleQuery =
                    backendConfigurationPnDbContext.CalendarOccurrenceExceptions
                        .Where(x => x.AreaRulePlanningId == resizeModel.Id)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed);

                if (scope == "thisAndFollowing" && !string.IsNullOrEmpty(resizeModel.OriginalDate))
                {
                    var originalDate = DateTime.Parse(resizeModel.OriginalDate, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).Date;
                    staleQuery = staleQuery.Where(x => x.OriginalDate >= originalDate);
                }

                var stales = await staleQuery.ToListAsync();
                foreach (var stale in stales)
                {
                    await stale.Delete(backendConfigurationPnDbContext);
                }
            }

            return new OperationResult(true,
                localizationService.GetString("CalendarTaskUpdatedSuccessfully"));
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationCalendarService.ResizeTask: {Message}", e.Message);
            return new OperationResult(false,
                $"{localizationService.GetString("ErrorWhileUpdatingCalendarTask")}: {e.Message}");
        }
    }

    public async Task<OperationDataResult<CalendarToggleCompleteResult>> ToggleComplete(
        int id, bool completed, int? complianceId, string? occurrenceDate)
    {
        // Calendar "complete from indicator" — resolves the specific Compliance
        // occurrence the user clicked (via complianceId from the calendar
        // response), then either completes the SDK case in place (no mandatory
        // fields) or returns RequiresForm=true with the route params the
        // frontend needs to open the compliance form.
        //
        // When the nightly batch has not yet deployed the occurrence (no
        // complianceId in the row, or the lookup misses), we materialise it
        // on demand via IEventDeployService so the user does not have to
        // wait until the next morning to complete a future event.
        //
        // See spec: docs/superpowers/specs/2026-05-21-calendar-complete-case-from-indicator-design.md
        //           docs/superpowers/specs/2026-05-21-calendar-ensure-compliance-on-complete-design.md
        try
        {
            if (!completed)
            {
                return new OperationDataResult<CalendarToggleCompleteResult>(false,
                    localizationService.GetString("UncompleteNotSupported"));
            }

            var arp = await backendConfigurationPnDbContext.AreaRulePlannings
                .Include(x => x.AreaRule)
                .Include(x => x.PlanningSites)
                .Where(x => x.Id == id)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync();

            if (arp == null)
            {
                return new OperationDataResult<CalendarToggleCompleteResult>(false,
                    localizationService.GetString("AreaRulePlanningNotFound"));
            }

            // Look up the SPECIFIC compliance row the user clicked. Previously
            // this queried by PlanningId and took "latest by Deadline" — that
            // silently picked the wrong week when a planning had multiple
            // compliance occurrences (e.g. an overdue January row alongside a
            // pending May row), completing or navigating to the wrong case.
            Compliance? compliance = null;
            if (complianceId is > 0)
            {
                compliance = await backendConfigurationPnDbContext.Compliances
                    .Where(c => c.Id == complianceId.Value
                             && c.PlanningId == arp.ItemPlanningId
                             && c.WorkflowState != Constants.WorkflowStates.Removed
                             && c.MicrotingSdkCaseId > 0)
                    .FirstOrDefaultAsync();
            }

            if (compliance == null)
            {
                // The user clicked an occurrence whose Compliance row has not
                // yet been materialised (nightly batch has not run, or this
                // is a future-day recurrence). Try to materialise on demand.
                //
                // Genuinely non-compliance events (no EformId on the AreaRule)
                // still get the deterministic "no-op" result — there is
                // nothing to complete for those.
                if (arp.AreaRule == null
                    || arp.AreaRule.EformId == null
                    || arp.AreaRule.EformId == 0)
                {
                    return new OperationDataResult<CalendarToggleCompleteResult>(false,
                        localizationService.GetString("TaskHasNoComplianceCase"));
                }

                var planningSite = arp.PlanningSites?
                    .FirstOrDefault(s => s.WorkflowState != Constants.WorkflowStates.Removed);
                if (planningSite == null)
                {
                    return new OperationDataResult<CalendarToggleCompleteResult>(false,
                        localizationService.GetString("NoAssignedWorker"));
                }

                if (string.IsNullOrWhiteSpace(occurrenceDate))
                {
                    return new OperationDataResult<CalendarToggleCompleteResult>(false,
                        localizationService.GetString("TaskHasNoComplianceCase"));
                }

                // Compliance.Deadline is persisted as DateTimeKind.Unspecified
                // 00:00 calendar-day — parse strictly so we never end up
                // with an off-by-one across the UTC/local boundary.
                if (!DateTime.TryParseExact(occurrenceDate, "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                {
                    return new OperationDataResult<CalendarToggleCompleteResult>(false,
                        localizationService.GetString("TaskHasNoComplianceCase"));
                }
                var deadline = parsedDate.Date;

                var ensure = await eventDeployService
                    .EnsureComplianceForOccurrenceAsync(arp, deadline, planningSite.SiteId)
                    .ConfigureAwait(false);
                if (ensure == null || ensure.ComplianceId <= 0)
                {
                    return new OperationDataResult<CalendarToggleCompleteResult>(false,
                        localizationService.GetString("TaskHasNoComplianceCase"));
                }

                compliance = await backendConfigurationPnDbContext.Compliances
                    .FirstOrDefaultAsync(c => c.Id == ensure.ComplianceId
                                           && c.WorkflowState != Constants.WorkflowStates.Removed
                                           && c.MicrotingSdkCaseId > 0);

                if (compliance == null)
                {
                    return new OperationDataResult<CalendarToggleCompleteResult>(false,
                        localizationService.GetString("TaskHasNoComplianceCase"));
                }
            }

            var sdkCore = await coreHelper.GetCore().ConfigureAwait(false);
            await using var sdkDbContext = sdkCore.DbContextHelper.GetDbContext();

            var sdkCase = await sdkDbContext.Cases
                .FirstOrDefaultAsync(c => c.Id == compliance.MicrotingSdkCaseId);

            if (sdkCase == null)
            {
                return new OperationDataResult<CalendarToggleCompleteResult>(false,
                    localizationService.GetString("SdkCaseNotFound"));
            }

            // No CheckListId → no template to inspect; treat as form-required so the
            // user opens the case form path (which has its own error handling).
            if (sdkCase.CheckListId == null)
            {
                return new OperationDataResult<CalendarToggleCompleteResult>(false,
                    localizationService.GetString("SdkCaseNotFound"));
            }

            // Canonical event start (UTC) for this occurrence — Compliance.Deadline
            // gives the calendar day (Kind=Unspecified, semantically UTC midnight
            // per the rest of this file's convention); CalendarConfiguration.StartHour
            // gives the hour-of-day the calendar shows for the ARP, and any "this"-
            // scope CalendarOccurrenceException overrides per-rotation. Same triple
            // the read path uses to project StartHour at line 703.
            //
            // We default to 9.0 (matches the read path's calConfig?.StartHour ?? 9.0
            // fallback) when no CalendarConfiguration exists for the ARP.
            var calConfig = await backendConfigurationPnDbContext.CalendarConfigurations
                .Where(x => x.AreaRulePlanningId == arp.Id)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync();
            var complianceException = await backendConfigurationPnDbContext.CalendarOccurrenceExceptions
                .Where(x => x.AreaRulePlanningId == arp.Id)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.OriginalDate.Date == compliance.Deadline.Date)
                .FirstOrDefaultAsync();
            var effectiveStartHour = complianceException?.StartHour ?? calConfig?.StartHour ?? 9.0;
            var startHourWhole = (int)Math.Floor(effectiveStartHour);
            var startMinuteWhole = (int)Math.Round((effectiveStartHour - startHourWhole) * 60);
            var deadlineDayUtc = DateTime.SpecifyKind(compliance.Deadline.Date, DateTimeKind.Utc);
            var eventStart = deadlineDayUtc.AddHours(startHourWhole).AddMinutes(startMinuteWhole);
            var eventStartIso = eventStart.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

            var hasMandatory = await HasMandatoryFields(sdkCore, sdkCase.CheckListId.Value)
                .ConfigureAwait(false);

            if (hasMandatory)
            {
                return new OperationDataResult<CalendarToggleCompleteResult>(true,
                    new CalendarToggleCompleteResult
                    {
                        RequiresForm = true,
                        SdkCaseId = sdkCase.Id,
                        TemplateId = sdkCase.CheckListId,
                        PropertyId = compliance.PropertyId,
                        ComplianceId = compliance.Id,
                        WorkerId = sdkCase.SiteId,
                        // ISO 8601 with millisecond precision to match the format
                        // the task-tracker uses (`task.deadlineTask.toISOString()`
                        // at task-tracker-table.component.ts:187). The
                        // compliance-case route resolver expects this shape.
                        //
                        // Compliance.Deadline is persisted as DateTimeKind.Unspecified
                        // but semantically holds a UTC instant (matches how the
                        // existing code in this file treats it — e.g. the week-range
                        // filter at line 86). Calling ToUniversalTime() on an
                        // Unspecified-kind would *shift* by the server's local
                        // offset; SpecifyKind(..., Utc) re-tags without shifting,
                        // then ToString("…Z") just emits the raw clock value as UTC.
                        Deadline = DateTime.SpecifyKind(compliance.Deadline, DateTimeKind.Utc)
                            .ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
                        // event.start (Deadline day + calendar-config StartHour, UTC) so the
                        // modal can default Case.DoneAt / DoneAtUserModifiable to the scheduled
                        // moment rather than "now".
                        EventStart = eventStartIso
                    });
            }

            // No mandatory fields → complete the SDK case in place. Mirrors
            // CompliancesGrpcService:159-174 (the form-submit path) — set
            // Status=100 + done-at timestamps so subsequent reads of the SDK
            // case report the case as fully completed. DoneAt mirrors the
            // calendar's scheduled event-start moment (Deadline day + StartHour)
            // rather than "now" so reports reflect when the work was scheduled,
            // not when the user happened to tap Complete.
            sdkCase.Status = 100;
            sdkCase.WorkflowState = Constants.WorkflowStates.Created;
            sdkCase.DoneAt = eventStart;
            sdkCase.DoneAtUserModifiable = eventStart;
            await sdkCase.Update(sdkDbContext).ConfigureAwait(false);

            return new OperationDataResult<CalendarToggleCompleteResult>(true,
                new CalendarToggleCompleteResult
                {
                    RequiresForm = false
                });
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationCalendarService.ToggleComplete: {Message}", e.Message);
            return new OperationDataResult<CalendarToggleCompleteResult>(false,
                $"{localizationService.GetString("ErrorWhileUpdatingCalendarTask")}: {e.Message}");
        }
    }

    /// <summary>
    /// Returns true iff the eForm template referenced by <paramref name="checkListId"/>
    /// contains at least one mandatory <see cref="Field"/>. Recurses through
    /// <see cref="FieldContainer"/> so grouped/container-nested fields are inspected too.
    /// Used by <see cref="ToggleComplete"/> to decide whether the calendar can complete
    /// the case in place or must hand the user off to the form route.
    /// </summary>
    private async Task<bool> HasMandatoryFields(eFormCore.Core core, int checkListId)
    {
        var sdkDbContext = core.DbContextHelper.GetDbContext();
        var language = await sdkDbContext.Languages.FirstAsync().ConfigureAwait(false);
        var mainElement = await core.ReadeForm(checkListId, language).ConfigureAwait(false);

        if (mainElement?.ElementList == null) return false;

        foreach (var element in mainElement.ElementList)
        {
            if (element is DataElement dataElement)
            {
                if (AnyMandatoryDataItem(dataElement.DataItemList)) return true;

                // DataItemGroup in this SDK doesn't nest further groups
                // (it has only DataItemList; groups-inside-groups isn't
                // representable in the type), so a single-level walk
                // covers every group-scoped field.
                if (dataElement.DataItemGroupList != null)
                {
                    foreach (var group in dataElement.DataItemGroupList)
                    {
                        if (AnyMandatoryDataItem(group?.DataItemList)) return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool AnyMandatoryDataItem(List<DataItem> items)
    {
        if (items == null) return false;

        foreach (var item in items)
        {
            if (item == null) continue;

            // Containers (FieldContainer / group-as-container) hold nested items; recurse
            // before checking Mandatory so a container itself with Mandatory=false doesn't
            // mask a mandatory child.
            if (item is FieldContainer container)
            {
                if (AnyMandatoryDataItem(container.DataItemList)) return true;
                continue;
            }

            if (item.Mandatory) return true;
        }

        return false;
    }

    public async Task<OperationDataResult<List<CalendarBoardModel>>> GetBoards(int propertyId)
    {
        try
        {
            var boards = await backendConfigurationPnDbContext.CalendarBoards
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.PropertyId == propertyId)
                .Select(x => new CalendarBoardModel
                {
                    Id = x.Id,
                    Name = x.Name,
                    Color = x.Color,
                    PropertyId = x.PropertyId,
                })
                .ToListAsync();

            // Auto-create "Default" board if none exist
            if (boards.Count == 0)
            {
                var defaultBoard = new CalendarBoard
                {
                    Name = "Default",
                    Color = "#c30000",
                    PropertyId = propertyId,
                };
                await defaultBoard.Create(backendConfigurationPnDbContext);

                boards.Add(new CalendarBoardModel
                {
                    Id = defaultBoard.Id,
                    Name = defaultBoard.Name,
                    Color = defaultBoard.Color,
                    PropertyId = defaultBoard.PropertyId,
                });
            }

            return new OperationDataResult<List<CalendarBoardModel>>(true, boards);
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationCalendarService.GetBoards: {Message}", e.Message);
            return new OperationDataResult<List<CalendarBoardModel>>(false,
                $"{localizationService.GetString("ErrorWhileGettingCalendarBoards")}: {e.Message}");
        }
    }

    public async Task<OperationResult> CreateBoard(CalendarBoardCreateModel model)
    {
        try
        {
            var board = new CalendarBoard
            {
                Name = model.Name,
                Color = model.Color,
                PropertyId = model.PropertyId,
            };
            await board.Create(backendConfigurationPnDbContext);

            return new OperationResult(true,
                localizationService.GetString("CalendarBoardCreatedSuccessfully"));
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationCalendarService.CreateBoard: {Message}", e.Message);
            return new OperationResult(false,
                $"{localizationService.GetString("ErrorWhileCreatingCalendarBoard")}: {e.Message}");
        }
    }

    public async Task<OperationResult> UpdateBoard(CalendarBoardUpdateModel model)
    {
        try
        {
            var board = await backendConfigurationPnDbContext.CalendarBoards
                .Where(x => x.Id == model.Id)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync();

            if (board == null)
            {
                return new OperationResult(false,
                    localizationService.GetString("CalendarBoardNotFound"));
            }

            board.Name = model.Name;
            board.Color = model.Color;
            await board.Update(backendConfigurationPnDbContext);

            return new OperationResult(true,
                localizationService.GetString("CalendarBoardUpdatedSuccessfully"));
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationCalendarService.UpdateBoard: {Message}", e.Message);
            return new OperationResult(false,
                $"{localizationService.GetString("ErrorWhileUpdatingCalendarBoard")}: {e.Message}");
        }
    }

    public async Task<OperationResult> DeleteBoard(int id)
    {
        try
        {
            var board = await backendConfigurationPnDbContext.CalendarBoards
                .Where(x => x.Id == id)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync();

            if (board == null)
            {
                return new OperationResult(false,
                    localizationService.GetString("CalendarBoardNotFound"));
            }

            await board.Delete(backendConfigurationPnDbContext);

            return new OperationResult(true,
                localizationService.GetString("CalendarBoardDeletedSuccessfully"));
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationCalendarService.DeleteBoard: {Message}", e.Message);
            return new OperationResult(false,
                $"{localizationService.GetString("ErrorWhileDeletingCalendarBoard")}: {e.Message}");
        }
    }

    // Parses a comma-separated weekday CSV (e.g. "1,3,5") into a sorted,
    // de-duplicated array of JS-style weekday ints (0=Sun..6=Sat). Returns
    // an empty array on null/empty/all-invalid input — callers treat empty
    // as "no multi-day expansion, fall back to single-day weekly behavior".
    private static int[] ParseWeekdaysCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return [];
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var n) ? n : -1)
            .Where(n => n is >= 0 and <= 6)
            .Distinct()
            .OrderBy(n => n)
            .ToArray();
    }

    // Yields every occurrence of `planning` whose date is in
    // [fromInclusive, toExclusive). Unlike GetOccurrencesInWeek (which
    // assumes a week-sized range and caps Month/Year iteration), this is
    // safe for arbitrary multi-month / multi-year ranges. Used by
    // ResizeTask's 'thisAndFollowing' past-anchor backfill.
    //
    // Returns empty for non-recurring plannings (RepeatType.None / default
    // branch) — there are no past occurrences to anchor in that case.
    //
    // When repeatWeekdaysCsv is non-empty and the planning is RepeatType.Week,
    // the weekly branch emits one occurrence per matching weekday in each
    // matching week (anchored to startDate's week, every repeatEvery weeks).
    // Null/empty CSV preserves the legacy single-day-per-week behavior.
    private static IEnumerable<DateTime> EnumerateOccurrences(
        Microting.ItemsPlanningBase.Infrastructure.Data.Entities.Planning planning,
        DateTime fromInclusive, DateTime toExclusive,
        string? repeatWeekdaysCsv = null,
        int? repeatOrdinalWeek = null,
        int? dayOfWeekOverride = null)
    {
        var startDate = planning.StartDate.Date;
        var rangeStart = fromInclusive.Date > startDate ? fromInclusive.Date : startDate;
        var rangeEnd = toExclusive.Date;
        if (rangeEnd <= rangeStart) yield break;
        var repeatEvery = Math.Max(planning.RepeatEvery, 1);

        switch (planning.RepeatType)
        {
            case Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType.Day:
            {
                var step = repeatEvery;
                var daysSinceStart = (rangeStart - startDate).Days;
                var skip = daysSinceStart > 0 ? (int)Math.Ceiling((double)daysSinceStart / step) : 0;
                var candidate = startDate.AddDays(skip * step);
                while (candidate < rangeEnd)
                {
                    if (candidate >= rangeStart) yield return candidate;
                    candidate = candidate.AddDays(step);
                }
                break;
            }
            case Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType.Week:
            {
                var weekdays = ParseWeekdaysCsv(repeatWeekdaysCsv);
                if (weekdays.Length == 0)
                {
                    // Legacy single-day path: step 7*repeatEvery from startDate.
                    var step = repeatEvery * 7;
                    var daysSinceStart = (rangeStart - startDate).Days;
                    var skip = daysSinceStart > 0 ? (int)Math.Ceiling((double)daysSinceStart / step) : 0;
                    var candidate = startDate.AddDays(skip * step);
                    while (candidate < rangeEnd)
                    {
                        if (candidate >= rangeStart) yield return candidate;
                        candidate = candidate.AddDays(step);
                    }
                }
                else
                {
                    // Multi-day path: anchor week is the Sunday-based week
                    // containing startDate (matches JS getDay() numbering).
                    // For each candidate day in [rangeStart, rangeEnd), emit
                    // it iff its weekday is in the CSV AND its week is a
                    // multiple of repeatEvery weeks from the anchor week.
                    var anchorWeekStart = startDate.AddDays(-(int)startDate.DayOfWeek);
                    var rangeStartWeek = rangeStart.AddDays(-(int)rangeStart.DayOfWeek);
                    // Align rangeStart back to its week-start so we iterate
                    // whole-week buckets cleanly.
                    var weeksFromAnchor = (rangeStartWeek - anchorWeekStart).Days / 7;
                    if (weeksFromAnchor < 0)
                    {
                        // Range begins before the anchor week — clamp.
                        weeksFromAnchor = 0;
                        rangeStartWeek = anchorWeekStart;
                    }
                    // Skip forward to the next "matching" week (k*repeatEvery
                    // weeks past the anchor).
                    var remainder = ((weeksFromAnchor % repeatEvery) + repeatEvery) % repeatEvery;
                    if (remainder != 0)
                    {
                        rangeStartWeek = rangeStartWeek.AddDays((repeatEvery - remainder) * 7);
                    }
                    var weekCursor = rangeStartWeek;
                    while (weekCursor < rangeEnd)
                    {
                        foreach (var wd in weekdays)
                        {
                            var candidate = weekCursor.AddDays(wd);
                            if (candidate < startDate) continue;
                            if (candidate < rangeStart) continue;
                            if (candidate >= rangeEnd) continue;
                            yield return candidate;
                        }
                        weekCursor = weekCursor.AddDays(repeatEvery * 7);
                    }
                }
                break;
            }
            case Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType.Month:
            {
                var monthsSinceStart = (rangeStart.Year - startDate.Year) * 12 + rangeStart.Month - startDate.Month;
                var skip = monthsSinceStart > 0 ? (int)Math.Ceiling((double)monthsSinceStart / repeatEvery) : 0;
                var candidateMonth = startDate.AddMonths(skip * repeatEvery);
                if (repeatOrdinalWeek.HasValue)
                {
                    // Nth-weekday-of-month path (e.g. "2nd Tuesday of each month").
                    int ordinal = repeatOrdinalWeek.Value; // 1..5
                    int targetDow = dayOfWeekOverride ?? (int)startDate.DayOfWeek; // 0=Sun..6=Sat
                    while (true)
                    {
                        var firstOfMonth = new DateTime(candidateMonth.Year, candidateMonth.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                        int dowOffset = (targetDow - (int)firstOfMonth.DayOfWeek + 7) % 7;
                        var candidate = firstOfMonth.AddDays(dowOffset + (ordinal - 1) * 7);
                        // If ordinal spills into the next month (e.g. 5th occurrence
                        // in a month that only has 4), skip this month.
                        if (candidate.Month != candidateMonth.Month)
                        {
                            candidateMonth = candidateMonth.AddMonths(repeatEvery);
                            continue;
                        }
                        if (candidate >= rangeEnd) break;
                        if (candidate >= rangeStart) yield return candidate;
                        candidateMonth = candidateMonth.AddMonths(repeatEvery);
                    }
                }
                else
                {
                    // Legacy day-of-month path.
                    var dom = Math.Min(planning.DayOfMonth ?? startDate.Day, 28);
                    while (true)
                    {
                        var daysInMonth = DateTime.DaysInMonth(candidateMonth.Year, candidateMonth.Month);
                        var candidate = new DateTime(candidateMonth.Year, candidateMonth.Month,
                            Math.Min(dom, daysInMonth), 0, 0, 0, DateTimeKind.Utc);
                        if (candidate >= rangeEnd) break;
                        if (candidate >= rangeStart) yield return candidate;
                        candidateMonth = candidateMonth.AddMonths(repeatEvery);
                    }
                }
                break;
            }
            // NOTE: GetOccurrencesInWeek has a `(RepeatType)4 // Year` branch
            // but the RepeatType enum only defines Day/Week/Month — the cast
            // is dead code. Not propagating it here. Add a real Year case
            // when the enum gains a member.
            default:
                // Non-recurring (RepeatType.None) — no past occurrences to anchor.
                yield break;
        }
    }

    private static List<DateTime> GetOccurrencesInWeek(
        Microting.ItemsPlanningBase.Infrastructure.Data.Entities.Planning planning,
        DateTime weekStart, DateTime weekEnd,
        string? repeatWeekdaysCsv = null,
        int? repeatOrdinalWeek = null,
        int? dayOfWeekOverride = null)
    {
        var occurrences = new List<DateTime>();
        var startDate = planning.StartDate.Date;
        var repeatEvery = Math.Max(planning.RepeatEvery, 1);

        switch (planning.RepeatType)
        {
            case Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType.Day:
            {
                // Find the first occurrence on or after weekStart
                if (startDate > weekEnd) break;
                var daysSinceStart = (weekStart.Date - startDate).Days;
                var periods = daysSinceStart > 0 ? (int)Math.Ceiling((double)daysSinceStart / repeatEvery) : 0;
                var candidate = startDate.AddDays(periods * repeatEvery);
                while (candidate <= weekEnd)
                {
                    if (candidate >= weekStart)
                        occurrences.Add(candidate);
                    candidate = candidate.AddDays(repeatEvery);
                }
                break;
            }
            case Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType.Week:
            {
                if (startDate > weekEnd) break;
                var weekdays = ParseWeekdaysCsv(repeatWeekdaysCsv);
                if (weekdays.Length == 0)
                {
                    // Legacy single-day path: step 7*repeatEvery from startDate.
                    var daysBetween = repeatEvery * 7;
                    var daysSinceStart = (weekStart.Date - startDate).Days;
                    var periods = daysSinceStart > 0 ? (int)Math.Ceiling((double)daysSinceStart / daysBetween) : 0;
                    var candidate = startDate.AddDays(periods * daysBetween);
                    while (candidate <= weekEnd)
                    {
                        if (candidate >= weekStart)
                            occurrences.Add(candidate);
                        candidate = candidate.AddDays(daysBetween);
                    }
                }
                else
                {
                    // Multi-day path: only emit occurrences in this week if
                    // the requested week is a multiple of repeatEvery weeks
                    // past the anchor week.
                    //
                    // Bucket weeks MONDAY-aligned (ISO: Mon=0..Sun=6) so all 7
                    // days of one Mon–Sun week share a single stride bucket. A
                    // Sunday-aligned grid puts the trailing Sunday in the NEXT
                    // bucket, so an every-Nth-week rule (N>1) dropped it — an
                    // all-days every-2nd-week rule lost Sunday (#922 CR04), and
                    // a mixed Wed+Sun set split across two buckets. The per-day
                    // projection below MUST still use the caller's weekStart
                    // (the caller's week may be Mon-Sun while wd uses JS
                    // getDay() Sun=0..Sat=6), so candidates land in
                    // [weekStart, weekStart+6]; only the stride bucketing is
                    // Monday-aligned.
                    var anchorWeekStart = startDate.AddDays(-(((int)startDate.DayOfWeek + 6) % 7));
                    var weekStartDow = (int)weekStart.Date.DayOfWeek;
                    foreach (var wd in weekdays)
                    {
                        // Days from weekStart to the date in the same
                        // 7-day window with DayOfWeek == wd. candidate is by
                        // construction in [weekStart, weekStart+6].
                        var offset = ((wd - weekStartDow) % 7 + 7) % 7;
                        var candidate = weekStart.Date.AddDays(offset);
                        if (candidate < startDate) continue;
                        // Gate the stride PER CANDIDATE on the candidate's own
                        // Monday-aligned week, so every day of a Mon–Sun week
                        // (including the trailing Sunday) maps to the same week
                        // bucket as its anchor. This keeps all-days and mixed
                        // Wed+Sun multi-day sets together under every-Nth-week
                        // cadences instead of splitting the Sunday off (#922).
                        var candidateWeekStart = candidate.AddDays(-(((int)candidate.DayOfWeek + 6) % 7));
                        var weeksFromAnchor = (candidateWeekStart - anchorWeekStart).Days / 7;
                        if (weeksFromAnchor >= 0 && weeksFromAnchor % repeatEvery == 0)
                            occurrences.Add(candidate);
                    }
                }
                break;
            }
            case Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType.Month:
            {
                if (startDate > weekEnd) break;
                // Find starting month
                var monthsSinceStart = (weekStart.Year - startDate.Year) * 12 + weekStart.Month - startDate.Month;
                var periods = monthsSinceStart > 0 ? (int)Math.Ceiling((double)monthsSinceStart / repeatEvery) : 0;
                var candidateMonth = startDate.AddMonths(periods * repeatEvery);
                if (repeatOrdinalWeek.HasValue)
                {
                    // Nth-weekday-of-month path (e.g. "2nd Tuesday of each month").
                    // At most 3 candidate months can overlap a 7-day window.
                    int ordinal = repeatOrdinalWeek.Value; // 1..5
                    int targetDow = dayOfWeekOverride ?? (int)startDate.DayOfWeek; // 0=Sun..6=Sat
                    for (var i = 0; i < 3; i++)
                    {
                        var firstOfMonth = new DateTime(candidateMonth.Year, candidateMonth.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                        int dowOffset = (targetDow - (int)firstOfMonth.DayOfWeek + 7) % 7;
                        var candidate = firstOfMonth.AddDays(dowOffset + (ordinal - 1) * 7);
                        // Skip months where the ordinal spills into the next month.
                        if (candidate.Month != candidateMonth.Month)
                        {
                            candidateMonth = candidateMonth.AddMonths(repeatEvery);
                            continue;
                        }
                        if (candidate > weekEnd) break;
                        if (candidate >= weekStart)
                            occurrences.Add(candidate);
                        candidateMonth = candidateMonth.AddMonths(repeatEvery);
                    }
                }
                else
                {
                    // Legacy day-of-month path.
                    var dom = Math.Min(planning.DayOfMonth ?? startDate.Day, 28);
                    for (var i = 0; i < 3; i++) // at most 3 months can overlap a week
                    {
                        var candidate = new DateTime(candidateMonth.Year, candidateMonth.Month,
                            Math.Min(dom, DateTime.DaysInMonth(candidateMonth.Year, candidateMonth.Month)),
                            0, 0, 0, DateTimeKind.Utc);
                        if (candidate > weekEnd) break;
                        if (candidate >= weekStart)
                            occurrences.Add(candidate);
                        candidateMonth = candidateMonth.AddMonths(repeatEvery);
                    }
                }
                break;
            }
            case (Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType)4: // Year
            {
                if (startDate > weekEnd) break;
                // Yearly stays in a fixed month, so keep the real day-of-month
                // and clamp it to the candidate month's length below (#922) —
                // unlike Month, which caps to 28 to dodge short-month overflow.
                var yearDom = planning.DayOfMonth ?? startDate.Day;
                var yearMonth = startDate.Month;
                var yearsSinceStart = weekStart.Year - startDate.Year;
                if (yearsSinceStart < 0) break;
                var yearPeriods = yearsSinceStart > 0 ? (int)Math.Ceiling((double)yearsSinceStart / repeatEvery) : 0;
                for (var i = 0; i < 2; i++)
                {
                    var candidateYear = startDate.Year + (yearPeriods + i) * repeatEvery;
                    var daysInMonth = DateTime.DaysInMonth(candidateYear, yearMonth);
                    var candidate = new DateTime(candidateYear, yearMonth,
                        Math.Min(yearDom, daysInMonth), 0, 0, 0, DateTimeKind.Utc);
                    if (candidate > weekEnd) break;
                    if (candidate >= weekStart)
                        occurrences.Add(candidate);
                }
                break;
            }
            default:
            {
                // No repeat — show on StartDate if it falls in the week
                if (startDate >= weekStart && startDate <= weekEnd)
                    occurrences.Add(startDate);
                break;
            }
        }

        // Respect RepeatUntil if set
        if (planning.RepeatUntil.HasValue)
            occurrences.RemoveAll(d => d > planning.RepeatUntil.Value);

        return occurrences;
    }

    private static bool ShouldIncludeTask(CalendarTaskResponseModel task, CalendarTaskRequestModel filter)
    {
        if (filter.BoardIds is { Count: > 0 } && task.BoardId.HasValue &&
            !filter.BoardIds.Contains(task.BoardId.Value))
        {
            return false;
        }

        if (filter.TagNames is { Count: > 0 } &&
            !task.Tags.Any(t => filter.TagNames.Contains(t)))
        {
            return false;
        }

        if (filter.SiteIds is { Count: > 0 } &&
            !task.AssigneeIds.Any(id => filter.SiteIds.Contains(id)))
        {
            return false;
        }

        return true;
    }

    private async Task<int?> ResolveOrCreateLogbøgerFolderAsync(int propertyId)
    {
        // 1) Folder already linked to this property? Use it.
        var existingFolder = await backendConfigurationPnDbContext.ProperyAreaFolders
            .Include(f => f.AreaProperty)
            .ThenInclude(a => a.Area)
            .ThenInclude(a => a.AreaTranslations)
            .Where(f => f.AreaProperty.PropertyId == propertyId)
            .Where(f => f.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(f => f.AreaProperty.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(f => f.AreaProperty.Area.AreaTranslations
                .Any(t => t.Name == "00. Logbøger"))
            .FirstOrDefaultAsync();

        if (existingFolder != null)
        {
            return existingFolder.FolderId;
        }

        // 2) Resolve the Logbøger Area — if it's missing (or its translations are),
        // seed from BackendConfigurationSeedAreas — same source the plugin-init
        // seed loop uses (EformBackendConfigurationPlugin.SeedDatabase).
        int areaId;
        List<Microting.eForm.Infrastructure.Models.CommonTranslationsModel> areaFolderTranslations;

        var existingAreaTranslations = await backendConfigurationPnDbContext.AreaTranslations
            .Where(x => x.Name == "00. Logbøger")
            .ToListAsync();

        if (existingAreaTranslations.Count > 0)
        {
            var sampleAreaId = existingAreaTranslations[0].AreaId;
            areaId = sampleAreaId;
            areaFolderTranslations = await backendConfigurationPnDbContext.AreaTranslations
                .Where(x => x.AreaId == sampleAreaId)
                .Select(x => new Microting.eForm.Infrastructure.Models.CommonTranslationsModel
                {
                    Name = x.Name,
                    LanguageId = x.LanguageId,
                    Description = ""
                })
                .ToListAsync();
        }
        else
        {
            var seededArea = Infrastructure.Data.Seed.Data.BackendConfigurationSeedAreas.AreasSeed
                .Where(a => a.IsDisabled == false)
                .FirstOrDefault(a => a.AreaTranslations != null
                    && a.AreaTranslations.Any(t => t.Name == "00. Logbøger"));
            if (seededArea == null)
            {
                logger.LogError("Logbøger area is missing from seed data — cannot resolve folder for property {PropertyId}", propertyId);
                return null;
            }

            var existingArea = await backendConfigurationPnDbContext.Areas
                .FirstOrDefaultAsync(a => a.Id == seededArea.Id);
            if (existingArea == null)
            {
                // Fresh Area + its translations — cascades via EF navigation.
                await seededArea.Create(backendConfigurationPnDbContext).ConfigureAwait(false);
                areaId = seededArea.Id;
            }
            else
            {
                // Area row exists but translations don't — reseed translations only.
                foreach (var translation in seededArea.AreaTranslations)
                {
                    var translationCopy = new AreaTranslation
                    {
                        AreaId = existingArea.Id,
                        LanguageId = translation.LanguageId,
                        Name = translation.Name,
                        Description = translation.Description,
                        InfoBox = translation.InfoBox,
                        Placeholder = translation.Placeholder,
                        NewItemName = translation.NewItemName
                    };
                    await translationCopy.Create(backendConfigurationPnDbContext).ConfigureAwait(false);
                }
                areaId = existingArea.Id;
            }

            areaFolderTranslations = seededArea.AreaTranslations
                .Select(t => new Microting.eForm.Infrastructure.Models.CommonTranslationsModel
                {
                    Name = t.Name,
                    LanguageId = t.LanguageId,
                    Description = ""
                })
                .ToList();
        }

        // 3) Inline only the creation portion of BackendConfigurationPropertyAreasServiceHelper.Update's
        // default branch — create AreaProperty + SDK folder + ProperyAreaFolder + seed AreaRules.
        // We skip the Update(...) call because it also computes assignmentsForDelete, which would
        // destroy any OTHER active AreaProperties this property already has.
        var core = await coreHelper.GetCore().ConfigureAwait(false);
        var sdkDbContext = core.DbContextHelper.GetDbContext();

        var property = await backendConfigurationPnDbContext.Properties
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(x => x.Id == propertyId)
            .FirstAsync();

        var newAreaProperty = new AreaProperty
        {
            CreatedByUserId = userService.UserId,
            UpdatedByUserId = userService.UserId,
            AreaId = areaId,
            PropertyId = propertyId,
            Checked = true
        };
        await newAreaProperty.Create(backendConfigurationPnDbContext).ConfigureAwait(false);

        var folderId = await core.FolderCreate(areaFolderTranslations, property.FolderId).ConfigureAwait(false);

        var newAreaFolder = new ProperyAreaFolder
        {
            FolderId = folderId,
            ProperyAreaAsignmentId = newAreaProperty.Id
        };
        await newAreaFolder.Create(backendConfigurationPnDbContext).ConfigureAwait(false);

        foreach (var seedRule in Infrastructure.Data.Seed.Data.BackendConfigurationSeedAreas.AreaRules
                     .Where(x => x.AreaId == areaId))
        {
            seedRule.PropertyId = property.Id;
            seedRule.FolderId = folderId;
            seedRule.CreatedByUserId = userService.UserId;
            seedRule.UpdatedByUserId = userService.UserId;
            seedRule.ComplianceModifiable = true;
            seedRule.NotificationsModifiable = true;
            if (!string.IsNullOrEmpty(seedRule.EformName))
            {
                var eformId = await sdkDbContext.CheckListTranslations
                    .Where(x => x.Text == seedRule.EformName)
                    .Select(x => x.CheckListId)
                    .FirstOrDefaultAsync();
                if (eformId != 0)
                {
                    seedRule.EformId = eformId;
                }
            }

            await seedRule.Create(backendConfigurationPnDbContext).ConfigureAwait(false);
        }

        return folderId;
    }

    // ---------------------------------------------------------------------
    // Attachment-related helpers + endpoints (calendar event-attachments)
    // ---------------------------------------------------------------------

    private const long MaxAttachmentBytes = 25L * 1024 * 1024;
    private const int MaxAttachmentsPerPlanning = 10;

    private static readonly Dictionary<string, string[]> AllowedMimeExtensions = new()
    {
        ["application/pdf"] = new[] { ".pdf" },
        ["image/png"] = new[] { ".png" },
        ["image/jpeg"] = new[] { ".jpg", ".jpeg" }
    };

    /// <summary>
    /// Project the eager-loaded AreaRulePlanningFiles collection (filtered to
    /// non-removed rows) onto the calendar response DTO. Returns an empty
    /// list when the navigation is null or all rows are soft-deleted.
    /// </summary>
    private static List<CalendarTaskAttachmentDto> MapAttachments(AreaRulePlanning? arp)
    {
        if (arp?.AreaRulePlanningFiles == null) return new List<CalendarTaskAttachmentDto>();
        return arp.AreaRulePlanningFiles
            .Where(f => f.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(f => new CalendarTaskAttachmentDto
            {
                Id = f.Id,
                OriginalFileName = f.OriginalFileName ?? string.Empty,
                MimeType = f.MimeType ?? string.Empty,
                SizeBytes = f.SizeBytes,
                DownloadUrl = $"/api/backend-configuration-pn/calendar/tasks/{arp.Id}/files/{f.Id}",
                DriveFileId = f.DriveFileId,
                DriveModifiedTime = f.DriveModifiedTime,
                // PR-8: only Drive-sourced rows carry refresh/revoke metadata.
                // Use DriveModifiedTime as the proxy for "last refreshed at"
                // (the change-processor advances it on every accepted refetch
                // — see PR-7). For non-Drive rows both fields stay null/false.
                LastRefreshedAt = f.DriveFileId != null ? f.DriveModifiedTime : null,
                DriveRevoked = f.DriveFileId != null
                    && f.GoogleOAuthToken != null
                    && f.GoogleOAuthToken.RevokedAt != null
            })
            .ToList();
    }

    public async Task<OperationDataResult<CalendarTaskAttachmentDto>> UploadFile(int taskId, IFormFile file)
    {
        try
        {
            // Defensive: reject empty multipart parts immediately so the
            // remainder of the pipeline can assume a real binary.
            if (file == null || file.Length == 0)
            {
                return new OperationDataResult<CalendarTaskAttachmentDto>(false,
                    localizationService.GetString("FileNotFound"));
            }

            if (file.Length > MaxAttachmentBytes)
            {
                return new OperationDataResult<CalendarTaskAttachmentDto>(false,
                    localizationService.GetString("FileTooLarge"));
            }

            // Browsers may attach a parameter such as ";charset=binary" to the
            // Content-Type header — strip parameters before comparing against
            // the allow-list, otherwise legitimate uploads get rejected.
            var mimeType = (file.ContentType ?? string.Empty)
                .Split(';')[0]
                .Trim()
                .ToLowerInvariant();
            if (!AllowedMimeExtensions.TryGetValue(mimeType, out var allowedExts))
            {
                return new OperationDataResult<CalendarTaskAttachmentDto>(false,
                    localizationService.GetString("FileTypeNotAllowed"));
            }

            // Defence-in-depth: even when the MIME is one we accept, the file
            // extension must agree — otherwise an attacker could upload an
            // executable disguised as a PDF and rely on the browser sniffing
            // the content type back to something dangerous.
            var ext = Path.GetExtension(file.FileName ?? string.Empty).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext) || !allowedExts.Contains(ext))
            {
                return new OperationDataResult<CalendarTaskAttachmentDto>(false,
                    localizationService.GetString("FileExtensionMimeMismatch"));
            }

            var planning = await backendConfigurationPnDbContext.AreaRulePlannings
                .Where(x => x.Id == taskId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync();
            if (planning == null)
            {
                return new OperationDataResult<CalendarTaskAttachmentDto>(false,
                    localizationService.GetString("AreaRulePlanningNotFound"));
            }

            var existingCount = await backendConfigurationPnDbContext.AreaRulePlanningFiles
                .Where(x => x.AreaRulePlanningId == taskId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .CountAsync();
            if (existingCount >= MaxAttachmentsPerPlanning)
            {
                return new OperationDataResult<CalendarTaskAttachmentDto>(false,
                    localizationService.GetString("AttachmentLimitReached"));
            }

            // We stage the upload to an intermediate file first so we can MD5
            // the on-disk copy — the same pattern used by
            // BackendConfigurationFilesService.Create and EFormFilesController.
            // Once we know the checksum we move the bytes to a deterministic
            // canonical path keyed on the checksum, then hand it to the SDK
            // for storage. The intermediate (ticks/guid-named) file is
            // *always* deleted in the finally block — that prevents the
            // disk leak that the previous implementation produced. The
            // canonical-named file is what FileLocation records and is what
            // the S3-disabled fallback in DownloadFile reads from, so it is
            // intentionally retained.
            var folder = Path.Combine(Path.GetTempPath(), "calendar-attachments");
            Directory.CreateDirectory(folder);
            var intermediatePath = Path.Combine(folder, $"{DateTime.UtcNow.Ticks}_{Guid.NewGuid():N}{ext}");

            try
            {
                await using (var stream = new FileStream(intermediatePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                string checksum;
                using (var md5 = MD5.Create())
                {
                    await using var stream = System.IO.File.OpenRead(intermediatePath);
                    var hashBytes = await md5.ComputeHashAsync(stream);
                    checksum = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                }

                var storageFileName = $"{checksum}{ext}";
                var canonicalPath = Path.Combine(folder, storageFileName);

                // Move staged bytes to the canonical path. If the canonical
                // file already exists (same checksum re-upload) keep it as-is.
                if (System.IO.File.Exists(canonicalPath))
                {
                    System.IO.File.Delete(intermediatePath);
                }
                else
                {
                    System.IO.File.Move(intermediatePath, canonicalPath);
                }

                var core = await coreHelper.GetCore().ConfigureAwait(false);
                var sdkDbContext = core.DbContextHelper.GetDbContext();

                // Mirror EFormFilesController.AddNewImage's UploadedData
                // shape — the SDK UploadedData does NOT carry the audit
                // fields (those live on the Backend-Configuration-side
                // UploadedData, a different entity). We attribute the
                // upload to the user via UploaderId; UploaderType is left
                // unset to match the canonical platform pattern (the
                // earlier "system" value mis-reported a user-initiated
                // upload as a background-system action).
                var uploadedData = new SdkUploadedData
                {
                    Checksum = checksum,
                    FileName = storageFileName,
                    FileLocation = canonicalPath,
                    Extension = ext.TrimStart('.'),
                    CurrentFile = storageFileName,
                    UploaderId = userService.UserId
                };
                await uploadedData.Create(sdkDbContext).ConfigureAwait(false);

                // SDK PutFileToStorageSystem is a no-op when S3 is disabled.
                // In that case the canonical file we just moved IS the
                // persistence layer, and DownloadFile reads it back via
                // FileLocation. When S3 is enabled the SDK uploads from the
                // canonical path; the canonical local file is left in place
                // (matching the existing platform behaviour in
                // BackendConfigurationFilesService.Create).
                await core.PutFileToStorageSystem(canonicalPath, storageFileName).ConfigureAwait(false);

                var arpFile = new AreaRulePlanningFile
                {
                    AreaRulePlanningId = taskId,
                    UploadedDataId = uploadedData.Id,
                    OriginalFileName = file.FileName ?? string.Empty,
                    MimeType = mimeType,
                    SizeBytes = file.Length,
                    CreatedByUserId = userService.UserId,
                    UpdatedByUserId = userService.UserId
                };
                await arpFile.Create(backendConfigurationPnDbContext).ConfigureAwait(false);

                return new OperationDataResult<CalendarTaskAttachmentDto>(true, new CalendarTaskAttachmentDto
                {
                    Id = arpFile.Id,
                    OriginalFileName = arpFile.OriginalFileName,
                    MimeType = arpFile.MimeType,
                    SizeBytes = arpFile.SizeBytes,
                    DownloadUrl = $"/api/backend-configuration-pn/calendar/tasks/{taskId}/files/{arpFile.Id}"
                });
            }
            finally
            {
                // Belt-and-braces: ensure the intermediate (ticks/guid-named)
                // staging file is gone regardless of which code path ran.
                // The canonical (checksum-named) file is the one we keep.
                try
                {
                    if (System.IO.File.Exists(intermediatePath))
                    {
                        System.IO.File.Delete(intermediatePath);
                    }
                }
                catch
                {
                    // Cleanup is best-effort — we don't want a stale-handle
                    // exception masking the original outcome.
                }
            }
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationCalendarService.UploadFile: {Message}", e.Message);
            return new OperationDataResult<CalendarTaskAttachmentDto>(false,
                $"{localizationService.GetString("ErrorWhileUploadingAttachment")}: {e.Message}");
        }
    }

    public async Task<OperationDataResult<List<CalendarTaskAttachmentDto>>> ListFiles(int taskId)
    {
        try
        {
            var files = await backendConfigurationPnDbContext.AreaRulePlanningFiles
                .Where(x => x.AreaRulePlanningId == taskId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .OrderBy(x => x.Id)
                .Select(x => new CalendarTaskAttachmentDto
                {
                    Id = x.Id,
                    OriginalFileName = x.OriginalFileName ?? string.Empty,
                    MimeType = x.MimeType ?? string.Empty,
                    SizeBytes = x.SizeBytes,
                    DownloadUrl = $"/api/backend-configuration-pn/calendar/tasks/{taskId}/files/{x.Id}",
                    DriveFileId = x.DriveFileId,
                    DriveModifiedTime = x.DriveModifiedTime,
                    // PR-8: same proxy as MapAttachments — DriveModifiedTime
                    // is what the change-processor bumps on every accepted
                    // refetch, so it doubles as "last refreshed at" for the UI.
                    LastRefreshedAt = x.DriveFileId != null ? x.DriveModifiedTime : null,
                    DriveRevoked = x.DriveFileId != null
                        && x.GoogleOAuthToken != null
                        && x.GoogleOAuthToken.RevokedAt != null
                })
                .ToListAsync();
            return new OperationDataResult<List<CalendarTaskAttachmentDto>>(true, files);
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationCalendarService.ListFiles: {Message}", e.Message);
            return new OperationDataResult<List<CalendarTaskAttachmentDto>>(false,
                $"{localizationService.GetString("ErrorWhileListingAttachments")}: {e.Message}");
        }
    }

    public async Task<CalendarFileDownload?> DownloadFile(int taskId, int fileId)
    {
        try
        {
            var arpFile = await backendConfigurationPnDbContext.AreaRulePlanningFiles
                .Where(x => x.Id == fileId)
                .Where(x => x.AreaRulePlanningId == taskId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync();
            if (arpFile == null) return null;

            var core = await coreHelper.GetCore().ConfigureAwait(false);
            var sdkDbContext = core.DbContextHelper.GetDbContext();

            var uploadedData = await sdkDbContext.UploadedDatas
                .Where(x => x.Id == arpFile.UploadedDataId)
                .FirstOrDefaultAsync();
            if (uploadedData == null) return null;

            // Determine S3-vs-local through the SAME mechanism the SDK itself
            // uses for PutFileToStorageSystem (Core.GetSdkSetting). This way
            // the read path can never disagree with the write path: if the
            // SDK persisted to S3, we read from S3; if the SDK no-op'd, we
            // read the canonical local file that UploadFile retained at
            // FileLocation.
            var s3Setting = await core.GetSdkSetting(Settings.s3Enabled).ConfigureAwait(false);
            var s3Enabled = string.Equals(s3Setting, "true", StringComparison.OrdinalIgnoreCase);

            Stream content;
            if (s3Enabled)
            {
                var s3Response = await core.GetFileFromS3Storage(uploadedData.FileName);
                content = s3Response.ResponseStream;
            }
            else
            {
                if (!System.IO.File.Exists(uploadedData.FileLocation))
                {
                    return null;
                }
                content = new FileStream(uploadedData.FileLocation, FileMode.Open, FileAccess.Read);
            }

            return new CalendarFileDownload
            {
                Content = content,
                MimeType = string.IsNullOrEmpty(arpFile.MimeType) ? "application/octet-stream" : arpFile.MimeType,
                FileName = arpFile.OriginalFileName ?? string.Empty
            };
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationCalendarService.DownloadFile: {Message}", e.Message);
            return null;
        }
    }

    public async Task<OperationResult> DeleteFile(int taskId, int fileId)
    {
        try
        {
            var arpFile = await backendConfigurationPnDbContext.AreaRulePlanningFiles
                .Where(x => x.Id == fileId)
                .Where(x => x.AreaRulePlanningId == taskId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync();
            if (arpFile == null)
            {
                return new OperationResult(false, localizationService.GetString("FileNotFound"));
            }

            arpFile.UpdatedByUserId = userService.UserId;
            // Soft-delete the join row; intentionally do NOT delete the SDK
            // UploadedData so the audit chain to the original blob survives.
            await arpFile.Delete(backendConfigurationPnDbContext).ConfigureAwait(false);

            return new OperationResult(true);
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationCalendarService.DeleteFile: {Message}", e.Message);
            return new OperationResult(false,
                $"{localizationService.GetString("ErrorWhileDeletingAttachment")}: {e.Message}");
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Implementation mirrors the compliance branch of
    /// <see cref="GetTasksForWeek"/> (lines 514-583) AND the angular
    /// <c>BackendConfigurationTaskTrackerHelper.Index</c> path
    /// (Infrastructure/Helpers/BackendConfigurationTaskTrackerHelper.cs:46-351),
    /// but without a deadline window — every non-removed compliance under
    /// the property is returned. The SDK Case is loaded in one batched IN
    /// query so we can populate <see cref="CalendarTaskResponseModel.Completed"/>
    /// (<c>Case.Status == 100</c>) and
    /// <see cref="CalendarTaskResponseModel.TaskIsExpired"/>
    /// (<c>(Case.WorkflowState=Removed AND Status=77) OR
    /// (compliance.Deadline &lt; UtcNow AND Status != 100)</c>).
    /// </remarks>
    public async Task<OperationDataResult<List<CalendarTaskResponseModel>>> GetTaskTrackerList(
        int propertyId, int? sdkSiteIdForFilter)
    {
        try
        {
            var userLanguageId = (await userService.GetCurrentUserLanguage()).Id;
            var dateTimeNow = DateTime.UtcNow;
            var result = new List<CalendarTaskResponseModel>();

            // Default board for missing-board fallback (parity with GetTasksForWeek line 53-59).
            var defaultBoard = await backendConfigurationPnDbContext.CalendarBoards
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.PropertyId == propertyId)
                .OrderBy(x => x.Id)
                .FirstOrDefaultAsync();
            var defaultBoardId = defaultBoard?.Id;

            // Full-scope compliance load — no deadline window, property scoped.
            // Mirrors BackendConfigurationTaskTrackerHelper.cs:59-65 + 67-76.
            // WorkflowState NULL is treated as "not removed" to match the
            // ActionableOnly branch convention applied to other mobile-worker
            // queries on this service.
            var compliances = await backendConfigurationPnDbContext.Compliances
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed || x.WorkflowState == null)
                .Where(x => x.PropertyId == propertyId)
                .OrderBy(x => x.Deadline)
                .ToListAsync();

            if (compliances.Count == 0)
            {
                return new OperationDataResult<List<CalendarTaskResponseModel>>(true, result);
            }

            // Batch-fetch the SDK Cases backing those compliances so we can
            // derive Completed + TaskIsExpired without an N+1 round-trip.
            var sdkCaseIds = compliances
                .Select(c => c.MicrotingSdkCaseId)
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            var sdkCore = await coreHelper.GetCore().ConfigureAwait(false);
            var sdkDbContextLocal = sdkCore.DbContextHelper.GetDbContext();
            var sdkCasesById = await sdkDbContextLocal.Cases
                .Where(c => sdkCaseIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id);

            var planningIds = compliances.Select(x => x.PlanningId).Distinct().ToList();

            // Batch-load AreaRulePlannings (mirrors GetTasksForWeek lines 480-488).
            var complianceArps = await backendConfigurationPnDbContext.AreaRulePlannings
                .Where(x => planningIds.Contains(x.ItemPlanningId))
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Include(x => x.AreaRule)
                    .ThenInclude(x => x.AreaRuleTranslations)
                .Include(x => x.PlanningSites)
                .Include(x => x.AreaRulePlanningFiles)
                .ToListAsync();
            var complianceArpDict = complianceArps.ToDictionary(x => x.ItemPlanningId);

            var complianceArpIds = complianceArps.Select(x => x.Id).ToList();
            var complianceCalConfigs = await backendConfigurationPnDbContext.CalendarConfigurations
                .Where(x => complianceArpIds.Contains(x.AreaRulePlanningId))
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .ToDictionaryAsync(x => x.AreaRulePlanningId);

            var complianceArpTags = await backendConfigurationPnDbContext.AreaRulePlanningTags
                .Where(x => complianceArpIds.Contains(x.AreaRulePlanningId))
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .ToListAsync();

            var complianceTagItemIds = complianceArpTags.Select(x => x.ItemPlanningTagId).Distinct().ToList();
            var compliancePlanningTagNames = await itemsPlanningPnDbContext.PlanningTags
                .Where(x => complianceTagItemIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name);

            var compliancePlanningsDict = await itemsPlanningPnDbContext.Plannings
                .Where(x => planningIds.Contains(x.Id))
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .ToDictionaryAsync(x => x.Id);

            // PlanningSite ↔ Site mapping for the per-row Worker filter.
            // Parity with BackendConfigurationTaskTrackerHelper.cs:166-184.
            var planningSiteIdsByPlanning = await itemsPlanningPnDbContext.PlanningSites
                .Where(x => planningIds.Contains(x.PlanningId))
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .GroupBy(x => x.PlanningId)
                .ToDictionaryAsync(g => g.Key, g => g.Select(p => p.SiteId).Distinct().ToList());

            foreach (var compliance in compliances)
            {
                complianceArpDict.TryGetValue(compliance.PlanningId, out var arp);
                if (arp == null) continue;
                CalendarConfiguration calConfig = null;
                if (arp != null)
                    complianceCalConfigs.TryGetValue(arp.Id, out calConfig);

                if (!compliancePlanningsDict.TryGetValue(compliance.PlanningId, out var planning))
                {
                    // Mirrors TaskTrackerHelper.cs:142-145 — orphan compliance, skip.
                    continue;
                }

                // Per-row Worker filter (parity with TaskTrackerHelper.cs:178-192,
                // collapsed to a single sdk-site check because the mobile worker
                // call passes exactly one site id; null disables the filter for
                // admin-style callers).
                if (sdkSiteIdForFilter.HasValue)
                {
                    if (!planningSiteIdsByPlanning.TryGetValue(compliance.PlanningId, out var planningSiteIds)
                        || !planningSiteIds.Contains(sdkSiteIdForFilter.Value))
                    {
                        continue;
                    }
                }

                var title = compliance.ItemName ?? "";
                if (arp?.AreaRule?.AreaRuleTranslations != null)
                {
                    title = arp.AreaRule.AreaRuleTranslations
                        .Where(t => t.LanguageId == userLanguageId)
                        .Select(t => t.Name)
                        .FirstOrDefault() ?? title;
                }

                var tags = arp != null
                    ? complianceArpTags
                        .Where(x => x.AreaRulePlanningId == arp.Id)
                        .Select(x => compliancePlanningTagNames.TryGetValue(x.ItemPlanningTagId, out var name) ? name : null)
                        .Where(x => x != null)
                        .ToList()
                    : [];

                var compIsRepeatAlways = arp?.RepeatType.HasValue == true && arp.RepeatType.Value == 1 && (arp.RepeatEvery ?? 0) == 0;
                var compHasNonAlwaysRepeat = arp?.RepeatType.HasValue == true && arp.RepeatType.Value > 0 && !compIsRepeatAlways;
                var compIsAllDay = calConfig == null && !compHasNonAlwaysRepeat;

                // Per-row Completed + TaskIsExpired derivation. Predicate
                // matches the spec: completed = Case.Status==100;
                // task_is_expired = (Case.WorkflowState=Removed AND
                // Status=77) OR (compliance.Deadline.Date < UtcNow.Date AND
                // Status != 100). Date-only so an event scheduled for today
                // is not flagged expired once its time-of-day passes.
                // Recurrence-only or missing-Case rows fall back to the
                // deadline-only check (no Status to consult, so they are
                // treated as not-completed).
                bool completed = false;
                bool taskIsExpired;
                if (compliance.MicrotingSdkCaseId > 0
                    && sdkCasesById.TryGetValue(compliance.MicrotingSdkCaseId, out var sdkCase)
                    && sdkCase != null)
                {
                    completed = sdkCase.Status == 100;
                    var retracted = sdkCase.WorkflowState == Constants.WorkflowStates.Removed
                                    && sdkCase.Status == 77;
                    var pastDueIncomplete = compliance.Deadline.Date < dateTimeNow.Date
                                            && sdkCase.Status != 100;
                    taskIsExpired = retracted || pastDueIncomplete;
                }
                else
                {
                    taskIsExpired = compliance.Deadline.Date < dateTimeNow.Date;
                }

                var model = new CalendarTaskResponseModel
                {
                    Id = arp?.Id ?? 0,
                    Title = title,
                    StartHour = compIsAllDay ? 0 : calConfig?.StartHour ?? 9.0,
                    Duration = compIsAllDay ? 0 : calConfig?.Duration ?? 1.0,
                    TaskDate = compliance.Deadline.ToString("yyyy-MM-dd"),
                    Tags = tags,
                    AssigneeIds = arp?.PlanningSites?
                        .Where(ps => ps.WorkflowState != Constants.WorkflowStates.Removed)
                        .Select(ps => (int)ps.SiteId)
                        .ToList() ?? [],
                    BoardId = calConfig?.BoardId ?? defaultBoardId,
                    Color = calConfig?.Color,
                    RepeatType = arp?.RepeatType ?? 0,
                    RepeatEvery = arp?.RepeatEvery ?? 1,
                    RepeatEndMode = arp?.RepeatEndMode,
                    RepeatOccurrences = arp?.RepeatOccurrences,
                    RepeatUntilDate = arp?.RepeatUntilDate,
                    DayOfWeek = arp?.DayOfWeek,
                    DayOfMonth = arp?.DayOfMonth,
                    RepeatOrdinalWeek = arp?.RepeatOrdinalWeek,
                    RepeatWeekdaysCsv = arp?.RepeatWeekdaysCsv,
                    Completed = completed,
                    Status = arp?.Status ?? false,
                    ComplianceEnabled = arp?.ComplianceEnabled ?? false,
                    PropertyId = compliance.PropertyId,
                    ComplianceId = compliance.Id,
                    IsFromCompliance = true,
                    Deadline = compliance.Deadline,
                    NextExecutionTime = planning.NextExecutionTime,
                    PlanningId = compliance.PlanningId,
                    IsAllDay = compIsAllDay,
                    EformId = arp?.AreaRule?.EformId,
                    SdkCaseId = compliance.MicrotingSdkCaseId,
                    ItemPlanningTagId = arp?.ItemPlanningTagId,
                    DescriptionHtml = planning.Description,
                    Attachments = MapAttachments(arp),
                    TaskIsExpired = taskIsExpired
                };

                result.Add(model);
            }

            return new OperationDataResult<List<CalendarTaskResponseModel>>(true, result);
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationCalendarService.GetTaskTrackerList: {Message}", e.Message);
            return new OperationDataResult<List<CalendarTaskResponseModel>>(false,
                $"{localizationService.GetString("ErrorWhileGettingCalendarTasks")}: {e.Message}");
        }
    }
}
