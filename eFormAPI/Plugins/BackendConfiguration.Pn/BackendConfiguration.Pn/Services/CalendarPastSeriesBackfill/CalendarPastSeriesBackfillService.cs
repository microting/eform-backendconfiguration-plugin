using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.EventDeployService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using BcRepeatType = BackendConfiguration.Pn.Infrastructure.Enums.RepeatType;
using ItemsPlanning = Microting.ItemsPlanningBase.Infrastructure.Data.Entities.Planning;
// Both of these live in a namespace whose last segment equals the class name;
// aliasing once keeps every call site readable.
using CalendarService =
    BackendConfiguration.Pn.Services.BackendConfigurationCalendarService.BackendConfigurationCalendarService;
using TaskWizardService =
    BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService.BackendConfigurationTaskWizardService;

namespace BackendConfiguration.Pn.Services.CalendarPastSeriesBackfill;

/// <summary>
/// #1122 §2 — "Batch: Ændre startdato til HVILKEN som helst dato […] så skal der
/// oprettes en rød opgave 01.01.2026. Det skal naturligvis gælde for alle
/// frekvenser og ikke kun år."
///
/// Moving a series' anchor into the past used to be impossible (the
/// CannotCreateTaskInThePast guards). With those gone, two things have to
/// happen that no existing code path does:
///
/// A. THE SCHEDULER MUST BE NEUTRALISED — this is a correctness requirement,
///    not housekeeping. ItemsPlanning's SearchListJob.ExecuteDeploy runs hourly
///    and selects plannings on three fields only:
///        (NextExecutionTime &lt;= today || NextExecutionTime == null)
///        &amp;&amp; StartDate &lt;= today &amp;&amp; Enabled
///    ComplianceEnabled is not in that filter. So a planning re-anchored into
///    the past with a null NextExecutionTime gets ONE missed occurrence
///    back-deployed per hourly run, forever, in an order nobody controls.
///    ExecuteCleanUp then re-arms NextExecutionTime = null for every planning
///    whose LastExecutedTime is null — which is why writing only
///    NextExecutionTime is useless and BOTH fields must be set.
///    (The job is additionally gated on the ItemsPlanningBaseSettings
///    StartTime/EndTime hour window and no-ops entirely when the StartTime
///    config row is absent. That is why the neutralisation is unconditional
///    here rather than "only when the job would run": we must not depend on a
///    config row existing on any given installation.)
///
/// B. THE OVERDUE OCCURRENCES MUST BE CREATED — but only when the event has
///    compliance turned on. EnsureDeployedAsync deliberately filters
///    RotationDate &gt;= todayUtc and must keep doing so, so the past range is
///    materialised here, occurrence by occurrence, through
///    EnsureComplianceForOccurrenceAsync (which tolerates a past deadline by
///    clamping the SDK case's EndDate while keeping Compliance.Deadline at the
///    true rotation date).
///
/// Enumeration is NOT re-derived here. It reuses
/// BackendConfigurationCalendarService.EnumerateOccurrences (all four
/// frequencies) plus ApplyRepeatEndBound (RepeatUntil / after-N), the same pair
/// the week renderer uses, so a backfilled occurrence and a rendered occurrence
/// can never disagree.
///
/// Lives in its own service rather than on BackendConfigurationCalendarService
/// for the same reason CalendarOccurrenceRetractionService does: that class is
/// request-shaped (IUserService, IBackendConfigurationLocalizationService) and
/// this work has to be callable from a batch/background caller too.
/// </summary>
public class CalendarPastSeriesBackfillService(
    ItemsPlanningPnDbContext itemsPlanningPnDbContext,
    IEventDeployService eventDeployService,
    ICalendarAssignmentResolver assignmentResolver,
    ILogger<CalendarPastSeriesBackfillService> logger)
    : ICalendarPastSeriesBackfillService
{
    /// <summary>
    /// Parked far enough out that no realistic clock reaches it, used when the
    /// series has already ended and there IS no next occurrence. Null would be
    /// actively harmful — it is precisely the value SearchListJob treats as
    /// "due now".
    /// </summary>
    private const int EndedSeriesSentinelYears = 50;

    public async Task<PastSeriesBackfillPlan> PlanPastSeriesBackfillAsync(
        AreaRulePlanning arp,
        DateTime? prospectiveStartDate = null,
        CancellationToken ct = default)
    {
        if (arp == null)
        {
            return PastSeriesBackfillPlan.Nothing;
        }

        // AsNoTracking: a preview must not leave a mutated Planning in the
        // change tracker for some later SaveChanges to pick up.
        var planning = await itemsPlanningPnDbContext.Plannings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == arp.ItemPlanningId
                                      && x.WorkflowState != Constants.WorkflowStates.Removed, ct)
            .ConfigureAwait(false);
        if (planning == null)
        {
            return PastSeriesBackfillPlan.Nothing;
        }

        var effectiveArp = prospectiveStartDate.HasValue
            ? ApplyProspectiveAnchor(planning, arp, prospectiveStartDate.Value)
            : arp;

        return await BuildPlanAsync(planning, effectiveArp, ct).ConfigureAwait(false);
    }

    public async Task<PastSeriesBackfillResult> BackfillPastSeriesAsync(
        AreaRulePlanning arp,
        CancellationToken ct = default)
    {
        if (arp == null)
        {
            return new PastSeriesBackfillResult(0, 0, 0, 0, null, false);
        }

        // TRACKED on purpose — the scheduler fields below are written back.
        var planning = await itemsPlanningPnDbContext.Plannings
            .FirstOrDefaultAsync(x => x.Id == arp.ItemPlanningId
                                      && x.WorkflowState != Constants.WorkflowStates.Removed, ct)
            .ConfigureAwait(false);
        if (planning == null)
        {
            return new PastSeriesBackfillResult(0, 0, 0, 0, null, false);
        }

        var plan = await BuildPlanAsync(planning, arp, ct).ConfigureAwait(false);
        if (!plan.AnchorIsInThePast)
        {
            // Forward re-anchor: nothing overdue, and the scheduler's own state
            // is still valid. Deliberately no write at all.
            return new PastSeriesBackfillResult(0, 0, 0, 0, null, false);
        }

        var today = DateTime.UtcNow.Date;

        // ── A. Scheduler neutralisation ──────────────────────────────────────
        var nextExecutionTime = plan.FirstFutureOccurrence ?? today.AddYears(EndedSeriesSentinelYears);
        planning.NextExecutionTime = nextExecutionTime;
        // Non-null is the point (ExecuteCleanUp re-arms NextExecutionTime = null
        // for any planning whose LastExecutedTime is null). An existing value is
        // a real execution record and is left alone.
        planning.LastExecutedTime ??= today;
        await planning.Update(itemsPlanningPnDbContext).ConfigureAwait(false);

        logger.LogInformation(
            "Past-series backfill START for AreaRulePlanning {ArpId} (planning {PlanningId}): anchor {Anchor}, {Occurrences} past occurrence(s) x {Sites} site(s), compliance {Compliance}, NextExecutionTime re-armed to {NextExecutionTime}",
            arp.Id, planning.Id, plan.Anchor.ToString("yyyy-MM-dd"), plan.PastOccurrences.Count,
            plan.SiteIds.Count, plan.ComplianceEnabled ? "ON" : "OFF",
            nextExecutionTime.ToString("yyyy-MM-dd"));

        // ── B. Overdue materialisation ───────────────────────────────────────
        if (!plan.ComplianceEnabled)
        {
            // Compliance OFF: no red tasks exist for this event at all, so there
            // is nothing to back-create. The series simply re-anchors and runs
            // from today. The scheduler write above still had to happen.
            logger.LogInformation(
                "Past-series backfill END for AreaRulePlanning {ArpId}: compliance disabled, {Occurrences} past occurrence(s) skipped",
                arp.Id, plan.PastOccurrences.Count);
            return new PastSeriesBackfillResult(
                plan.PastOccurrences.Count, 0, 0, 0, nextExecutionTime, ComplianceSkipped: true);
        }

        var created = 0;
        var alreadyPresent = 0;
        var failed = 0;

        // Synchronous and UNCAPPED by product decision: a daily rule re-anchored
        // six months back is ~180 occurrences x sites. Each pair is independently
        // try/catch'd so one bad site cannot abort the rest, and every pair is
        // idempotent, so a partial run can simply be repeated.
        foreach (var occurrence in plan.PastOccurrences)
        {
            foreach (var sdkSiteId in plan.SiteIds)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var ensured = await eventDeployService
                        .EnsureComplianceForOccurrenceAsync(arp, occurrence, sdkSiteId, ct)
                        .ConfigureAwait(false);

                    if (ensured == null)
                    {
                        // null == could not materialise (planning / SDK site /
                        // language missing, or eformId <= 0). That is a FAILURE
                        // for this pair, not a silent success — treating it as
                        // success would report overdue rows that do not exist.
                        failed++;
                        logger.LogWarning(
                            "Past-series backfill could not materialise occurrence {Deadline} for site {SiteId} on AreaRulePlanning {ArpId}",
                            occurrence.ToString("yyyy-MM-dd"), sdkSiteId, arp.Id);
                        continue;
                    }

                    if (ensured.Created)
                    {
                        created++;
                    }
                    else
                    {
                        alreadyPresent++;
                    }
                }
                catch (Exception e)
                {
                    failed++;
                    logger.LogError(e,
                        "Past-series backfill failed for occurrence {Deadline}, site {SiteId}, AreaRulePlanning {ArpId}",
                        occurrence.ToString("yyyy-MM-dd"), sdkSiteId, arp.Id);
                }
            }
        }

        logger.LogInformation(
            "Past-series backfill END for AreaRulePlanning {ArpId}: {Created} created, {AlreadyPresent} already present, {Failed} failed, over {Occurrences} past occurrence(s)",
            arp.Id, created, alreadyPresent, failed, plan.PastOccurrences.Count);

        return new PastSeriesBackfillResult(
            plan.PastOccurrences.Count, created, alreadyPresent, failed, nextExecutionTime,
            ComplianceSkipped: false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Planning
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<PastSeriesBackfillPlan> BuildPlanAsync(
        ItemsPlanning planning, AreaRulePlanning arp, CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;
        var anchor = planning.StartDate.Date;

        if (anchor >= today)
        {
            return PastSeriesBackfillPlan.Nothing with
            {
                Anchor = anchor,
                ComplianceEnabled = arp.ComplianceEnabled
            };
        }

        var pastOccurrences = EnumeratePastOccurrences(planning, arp, anchor, today);
        var firstFuture = FirstFutureOccurrence(planning, arp, today);

        // Resolved at plan time, deliberately: worker-tag membership is
        // evaluated NOW, so a later membership change affects future occurrences
        // only and historical overdue rows are not retroactively re-sited.
        List<int> siteIds = [];
        if (arp.ComplianceEnabled)
        {
            siteIds = (await assignmentResolver.ResolveEffectiveSiteIdsAsync(arp.Id, ct)
                    .ConfigureAwait(false))
                .OrderBy(x => x)
                .ToList();
        }

        return new PastSeriesBackfillPlan(
            anchor, AnchorIsInThePast: true, pastOccurrences, siteIds, firstFuture,
            arp.ComplianceEnabled);
    }

    /// <summary>
    /// Every occurrence in [anchor, today). Recurring series go through the
    /// shared enumerator + end bound; a NON-recurring one has exactly one
    /// occurrence — its own anchor — which EnumerateOccurrences deliberately
    /// does not yield (it returns empty for RepeatType.None), so it is added
    /// here. The recurrence signal is RepeatType != 0, NOT RepeatEvery: the
    /// calendar sends RepeatEvery = 1 even for "no repeat".
    /// </summary>
    private static List<DateTime> EnumeratePastOccurrences(
        ItemsPlanning planning, AreaRulePlanning arp, DateTime anchor, DateTime today)
    {
        if ((int)planning.RepeatType == 0)
        {
            return [anchor];
        }

        var occurrences = CalendarService
            .EnumerateOccurrences(planning, anchor, today,
                arp.RepeatWeekdaysCsv, arp.RepeatOrdinalWeek, arp.DayOfWeek)
            .ToList();

        // rangeEndInclusive is the last day the caller cares about — yesterday,
        // since the range above is exclusive of today.
        CalendarService.ApplyRepeatEndBound(planning, arp, occurrences, today.AddDays(-1));

        return occurrences;
    }

    /// <summary>
    /// First occurrence on or after today, or null when the series has already
    /// ended.
    ///
    /// AFTER-N COUNTING — a conscious choice. ApplyRepeatEndBound counts the N
    /// occurrences of a "repeat N times" rule from planning.StartDate, i.e. from
    /// the POST-edit anchor. This method does the same. So re-anchoring a
    /// "yearly, 3 times" series back to 2026-01-01 means 2026 / 2027 / 2028, not
    /// "three counted from wherever the series used to start".
    ///
    /// Why: (1) the week renderer already counts from the post-edit anchor, and
    /// backfill vs render disagreeing is the exact drift the shared helper
    /// exists to prevent; (2) "repeat 3 times" is a property of the series, and
    /// a series is identified by its anchor — counting from a superseded anchor
    /// would make the visible length of a series depend on its edit history,
    /// which is neither explainable in the UI nor representable in the DB (no
    /// original-anchor column exists).
    /// </summary>
    private static DateTime? FirstFutureOccurrence(
        ItemsPlanning planning, AreaRulePlanning arp, DateTime today)
    {
        if ((int)planning.RepeatType == 0)
        {
            // Non-recurring: its single occurrence is the anchor, which
            // BuildPlanAsync has already established is in the past.
            return null;
        }

        var farHorizon = today.AddYears(EndedSeriesSentinelYears);

        if (arp.RepeatEndMode == 1 && arp.RepeatOccurrences.HasValue)
        {
            // "After N": the series is the first N occurrences from the anchor.
            // Take(N) short-circuits the lazy enumerator, so the horizon below is
            // never actually walked.
            var withinCount = CalendarService
                .EnumerateOccurrences(planning, planning.StartDate.Date, farHorizon,
                    arp.RepeatWeekdaysCsv, arp.RepeatOrdinalWeek, arp.DayOfWeek)
                .Take(arp.RepeatOccurrences.Value)
                .Where(d => d >= today)
                .ToList();
            return withinCount.Count > 0 ? withinCount[0] : null;
        }

        var end = arp.RepeatEndMode == 2 && arp.RepeatUntilDate.HasValue
            ? arp.RepeatUntilDate.Value.Date.AddDays(1) // exclusive upper bound
            : farHorizon;
        if (end <= today)
        {
            // "Until" date already passed — the series has ended.
            return null;
        }

        // Enumerating FROM today (not from the anchor) keeps every branch's skip
        // arithmetic O(1); the first yielded value ends the walk.
        foreach (var occurrence in CalendarService
                     .EnumerateOccurrences(planning, today, end,
                         arp.RepeatWeekdaysCsv, arp.RepeatOrdinalWeek, arp.DayOfWeek))
        {
            return occurrence;
        }

        return null;
    }

    /// <summary>
    /// Mutates <paramref name="planning"/> (which MUST be an untracked/throwaway
    /// copy) and returns a detached AreaRulePlanning carrying exactly the
    /// anchor-dependent fields the save path will write, so a projection sees the
    /// pattern the series will have AFTER the save rather than the one it has now:
    ///   * Planning.StartDate         &lt;- the picked anchor
    ///   * Planning.DayOfMonth        &lt;- BackendConfigurationTaskWizardService.DeriveDayOfMonth
    ///     (BackendConfigurationTaskWizardService.UpdateTask does exactly this)
    ///   * AreaRulePlanning.DayOfWeek &lt;- BackendConfigurationCalendarService.UpdateTask,
    ///     for Week rules and Nth-weekday-of-month rules ONLY; it leaves DayOfWeek
    ///     alone otherwise.
    /// Everything else (RepeatType/Every/WeekdaysCsv/OrdinalWeek/end bound) is
    /// unchanged by a start-date-only edit.
    ///
    /// internal so #1122 §4's change-start-date PREVIEW can derive the same
    /// post-save pattern before calling
    /// BackendConfigurationCalendarService.IsSameRecurrencePeriod — that gate runs
    /// on the POST-write entities in the apply, so a preview using the pre-write
    /// ones would pick the wrong branch for exactly the weekday/day-of-month
    /// re-anchors this action is about. One copy of the derivation, no drift.
    /// </summary>
    internal static AreaRulePlanning ApplyProspectiveAnchor(
        ItemsPlanning planning, AreaRulePlanning arp, DateTime prospectiveStartDate)
    {
        var anchor = prospectiveStartDate.Date;
        planning.StartDate = anchor;
        planning.DayOfMonth =
            TaskWizardService.DeriveDayOfMonth((BcRepeatType)(int)planning.RepeatType, anchor);

        var effectiveArp = CloneArpForPlanning(arp);
        if (effectiveArp.RepeatOrdinalWeek.HasValue
            || (int)planning.RepeatType == (int)BcRepeatType.Week)
        {
            effectiveArp.DayOfWeek = (int)anchor.DayOfWeek;
        }

        return effectiveArp;
    }

    /// <summary>
    /// A detached copy carrying only the fields the enumerators read, so a
    /// PREVIEW can apply the prospective DayOfWeek without mutating the caller's
    /// (possibly EF-tracked) entity.
    /// </summary>
    private static AreaRulePlanning CloneArpForPlanning(AreaRulePlanning source) => new()
    {
        Id = source.Id,
        ItemPlanningId = source.ItemPlanningId,
        StartDate = source.StartDate,
        DayOfWeek = source.DayOfWeek,
        DayOfMonth = source.DayOfMonth,
        RepeatEvery = source.RepeatEvery,
        RepeatType = source.RepeatType,
        RepeatEndMode = source.RepeatEndMode,
        RepeatOccurrences = source.RepeatOccurrences,
        RepeatUntilDate = source.RepeatUntilDate,
        RepeatWeekdaysCsv = source.RepeatWeekdaysCsv,
        RepeatOrdinalWeek = source.RepeatOrdinalWeek,
        ComplianceEnabled = source.ComplianceEnabled
    };
}
