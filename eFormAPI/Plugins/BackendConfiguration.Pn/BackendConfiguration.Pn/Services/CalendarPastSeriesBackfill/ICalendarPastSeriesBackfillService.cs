using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

namespace BackendConfiguration.Pn.Services.CalendarPastSeriesBackfill;

/// <summary>
/// Read-only projection of what a past-series backfill WOULD do. Produced by
/// <see cref="ICalendarPastSeriesBackfillService.PlanPastSeriesBackfillAsync"/>
/// and consumed both by the write path (so the two can never enumerate
/// differently) and by #1122 §4's preview endpoint (so the number the admin is
/// shown is the number the apply produces).
/// </summary>
/// <param name="Anchor">The series anchor the plan was built from (planning.StartDate.Date, or the prospective one).</param>
/// <param name="AnchorIsInThePast">False = nothing to do; the series simply runs forward from today.</param>
/// <param name="PastOccurrences">Every occurrence in [Anchor, today), end-bound applied. Ascending, distinct days.</param>
/// <param name="SiteIds">Effective recipients resolved at plan time (explicit PlanningSites ∪ live worker-tag members).</param>
/// <param name="FirstFutureOccurrence">First occurrence on/after today, or null when the series has already ended.</param>
/// <param name="ComplianceEnabled">Mirror of AreaRulePlanning.ComplianceEnabled — false means no overdue rows are created.</param>
public sealed record PastSeriesBackfillPlan(
    DateTime Anchor,
    bool AnchorIsInThePast,
    IReadOnlyList<DateTime> PastOccurrences,
    IReadOnlyList<int> SiteIds,
    DateTime? FirstFutureOccurrence,
    bool ComplianceEnabled)
{
    public static PastSeriesBackfillPlan Nothing { get; } =
        new(default, false, [], [], null, false);

    /// <summary>
    /// #1122 §4's "L overskredne opgaver oprettes". One Compliance row is
    /// materialised per (occurrence, site), and none at all when compliance is
    /// off — that is the whole of the compliance-OFF rule, expressed once.
    /// </summary>
    public int OverdueToCreate => ComplianceEnabled ? PastOccurrences.Count * SiteIds.Count : 0;
}

/// <summary>
/// Outcome of an executed backfill.
/// </summary>
/// <param name="PastOccurrences">How many past occurrence DATES were enumerated.</param>
/// <param name="Created">(occurrence, site) pairs that produced a brand-new Compliance row.</param>
/// <param name="AlreadyPresent">Pairs the idempotence guard short-circuited — a re-run lands here.</param>
/// <param name="Failed">Pairs that threw, or that returned null (missing planning / SDK site / language / eformId ≤ 0).</param>
/// <param name="NextExecutionTime">What the scheduler was re-armed to. Null only when the anchor was not in the past.</param>
/// <param name="ComplianceSkipped">True when ComplianceEnabled was false, so no occurrence was deployed.</param>
public readonly record struct PastSeriesBackfillResult(
    int PastOccurrences,
    int Created,
    int AlreadyPresent,
    int Failed,
    DateTime? NextExecutionTime,
    bool ComplianceSkipped);

public interface ICalendarPastSeriesBackfillService
{
    /// <summary>
    /// Projects the backfill for <paramref name="arp"/> WITHOUT writing anything.
    ///
    /// Entry point for #1122 §4's <c>change-start-date/preview</c>: pass the
    /// date the admin picked as <paramref name="prospectiveStartDate"/> and the
    /// plan is computed against the pattern the series will have AFTER the save,
    /// not the one it has now.
    /// </summary>
    /// <param name="prospectiveStartDate">
    /// Null = use the persisted anchor (what <see cref="BackfillPastSeriesAsync"/>
    /// will do). Non-null = re-derive the anchor-dependent pattern fields exactly
    /// as the wizard/UpdateTask will write them, so preview and apply agree.
    /// </param>
    Task<PastSeriesBackfillPlan> PlanPastSeriesBackfillAsync(
        AreaRulePlanning arp,
        DateTime? prospectiveStartDate = null,
        CancellationToken ct = default);

    /// <summary>
    /// Re-anchors a series whose (already persisted) start date is in the past:
    ///
    /// 1. Neutralises the items-planning scheduler — ALWAYS, compliance on or
    ///    off. Without this a planning with a past StartDate and a null
    ///    NextExecutionTime is back-deployed one missed occurrence per hourly
    ///    SearchListJob run, uncontrolled.
    /// 2. When <c>arp.ComplianceEnabled</c>, synchronously materialises one
    ///    overdue Compliance row per (occurrence, effective site) for every
    ///    occurrence in [StartDate, today).
    ///
    /// No-op (and no scheduler write) when the anchor is NOT in the past — the
    /// caller may invoke it unconditionally after any date edit.
    ///
    /// MUST be called AFTER the new anchor has been persisted: it reads
    /// planning.StartDate, never a request model, so whatever normalisation or
    /// rounding the write path applied is what gets enumerated.
    /// </summary>
    Task<PastSeriesBackfillResult> BackfillPastSeriesAsync(
        AreaRulePlanning arp,
        CancellationToken ct = default);
}
