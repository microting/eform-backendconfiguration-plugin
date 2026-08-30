using System;
using System.Threading;
using System.Threading.Tasks;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

namespace BackendConfiguration.Pn.Services.CalendarOccurrenceRetraction;

/// <summary>
/// Outcome of one <see cref="ICalendarOccurrenceRetractionService.RetractNonCompletedOccurrencesAsync"/>
/// pass. The counts are what a preview endpoint needs (#1122's
/// OccurrencesToRetract / CompletedPreserved), so the write path and a read-only
/// projection can report the same numbers from the same code.
/// </summary>
/// <param name="Retracted">Rows whose case was pulled and whose Compliance row was soft-deleted.</param>
/// <param name="CompletedPreserved">Rows skipped because the occurrence is completed (invariant R2).</param>
/// <param name="Failed">Rows left untouched because retraction threw — safe to re-run.</param>
public readonly record struct OccurrenceRetractionResult(
    int Retracted,
    int CompletedPreserved,
    int Failed);

public interface ICalendarOccurrenceRetractionService
{
    /// <summary>
    /// Retracts every NON-completed deployed occurrence of the given calendar
    /// event, optionally only those on or after <paramref name="fromDate"/>.
    /// </summary>
    /// <param name="arp">
    /// The calendar event. Only <c>ItemPlanningId</c> (which occurrences are in
    /// scope) and <c>Id</c> (logging) are read, so a caller that already holds
    /// the entity need not re-load it.
    /// </param>
    /// <param name="fromDate">
    /// Inclusive lower bound on <c>Compliance.Deadline</c>, compared at day
    /// granularity (the whole of that local day is in range). Null = the whole
    /// series, past occurrences included.
    /// </param>
    Task<OccurrenceRetractionResult> RetractNonCompletedOccurrencesAsync(
        AreaRulePlanning arp,
        DateTime? fromDate = null,
        CancellationToken ct = default);

    /// <summary>
    /// What <see cref="RetractNonCompletedOccurrencesAsync"/> WOULD do, without
    /// writing anything — the read side of #1122 §5's preview panel
    /// ("M åbne forekomster tilbagekaldes · K gennemførte bevares").
    ///
    /// Shares the write path's row query, its single batched SDK completion
    /// lookup and its completion predicate verbatim (one private loader, one
    /// private <c>IsCompleted</c>), so preview and apply cannot disagree about
    /// either which rows are in scope or which of them are frozen.
    ///
    /// The counts are over Compliance ROWS, not distinct occurrence dates:
    /// Compliance carries no site column, so one occurrence deployed to two
    /// workers is two rows and may be half-answered, and the apply then retracts
    /// one of them while preserving the other.
    ///
    /// <see cref="OccurrenceRetractionResult.Failed"/> is always 0 here — a
    /// projection cannot know which cloud CaseDelete will fail.
    /// </summary>
    /// <param name="fromDate">Same meaning as on the write method. Pass the same value.</param>
    Task<OccurrenceRetractionResult> PlanRetractionAsync(
        AreaRulePlanning arp,
        DateTime? fromDate = null,
        CancellationToken ct = default);

    /// <summary>
    /// Sweeps the ORPHANS the occurrence-driven pass above cannot see: deployed
    /// <c>PlanningCaseSite</c> rows of this series that no <c>Compliance</c> row
    /// references. Completion-guarded, using the SAME predicate — an SDK case is
    /// answered when <c>Status == 100 || DoneAt.HasValue</c>.
    ///
    /// WHY IT IS A SEPARATE METHOD, and must stay one. #1122 calls
    /// <see cref="RetractNonCompletedOccurrencesAsync"/> with a
    /// <c>fromDate</c> precisely to bound the blast radius to the date range the
    /// new recurrence pattern owns; occurrences before it are deliberately left
    /// alone. A PlanningCaseSite with no Compliance row has NO deadline, so it
    /// cannot be filtered by <c>fromDate</c> at all. Folding this sweep into the
    /// bounded method would therefore make #1122's re-anchor silently retract
    /// unbounded rows it explicitly excluded. Only the two DEACTIVATE call sites
    /// — <c>DeactivateList</c> and <c>UpdateTask</c>'s deactivate branch, which
    /// retract the whole series unbounded — may call this.
    ///
    /// WHY IT EXISTS AT ALL. Before #1123 both deactivate paths walked every
    /// PlanningCase of the planning and CaseDeleted its SDK case. That was wrong
    /// only in lacking a completion guard, not in walking PlanningCaseSites:
    /// dropping the walk entirely left a deployed case that has no Compliance row
    /// live on a worker's device after the admin deactivated the task. This
    /// restores the reach with the guard the old code was missing.
    ///
    /// Run it AFTER <see cref="RetractNonCompletedOccurrencesAsync"/>. The two
    /// cannot double-handle a row: this one skips every PlanningCaseSite whose
    /// SDK case is referenced by ANY Compliance row of the planning, removed
    /// ones included (a removed row means the occurrence pass already pulled it;
    /// a live one means it either preserved it as completed or will).
    /// </summary>
    Task<OccurrenceRetractionResult> RetractDeployedCasesWithoutComplianceAsync(
        AreaRulePlanning arp,
        CancellationToken ct = default);
}
