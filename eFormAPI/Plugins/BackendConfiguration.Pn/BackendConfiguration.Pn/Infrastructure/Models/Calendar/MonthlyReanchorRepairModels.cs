using System;
using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.Calendar;

/// <summary>
/// #1294 — what the one-time monthly re-anchor repair would do (dry run) or did
/// (real run). Every list is ordered deterministically (by id), so two dry runs
/// over the same data produce the same <see cref="PlanHash"/>.
/// </summary>
public class MonthlyReanchorRepairPlanModel
{
    /// <summary>
    /// SHA-256 over the actions the real run would WRITE (ordinal restorations,
    /// planning updates, compliance moves). The real run refuses to start unless
    /// it is handed the hash of the dry run the reviewer signed off, so what runs
    /// is exactly what was reviewed.
    /// </summary>
    public string PlanHash { get; set; } = string.Empty;

    /// <summary>The Danish (Europe/Copenhagen) date "before today" was judged against.</summary>
    public DateTime Today { get; set; }

    /// <summary>True when the repair has completed (marker "done"): the real run will refuse.</summary>
    public bool AlreadyExecuted { get; set; }

    /// <summary>The marker's value: null (never run), "running", "partial" (re-runnable) or "done".</summary>
    public string MarkerState { get; set; }

    public List<MonthlyReanchorOrdinalRestorationModel> OrdinalRestorations { get; set; } = [];
    public List<MonthlyReanchorOrdinalKeptModel> OrdinalsKept { get; set; } = [];
    public List<MonthlyReanchorPlanningUpdateModel> PlanningUpdates { get; set; } = [];
    public List<MonthlyReanchorComplianceMoveModel> ComplianceMoves { get; set; } = [];
    public List<MonthlyReanchorReviewItemModel> ReviewItems { get; set; } = [];
    public List<MonthlyReanchorSkippedOrphanModel> SkippedOrphans { get; set; } = [];
}

/// <summary>A converted "1st &lt;weekday&gt;" rule whose legacy day-of-month week is restored.</summary>
public class MonthlyReanchorOrdinalRestorationModel
{
    public int AreaRulePlanningId { get; set; }
    public int PlanningId { get; set; }
    public int DayOfWeek { get; set; }
    public int OldOrdinal { get; set; }
    public int NewOrdinal { get; set; }
    /// <summary>Planning.StartDate — the legacy anchor both weekday and ordinal come from.</summary>
    public DateTime LegacyStartDate { get; set; }
    /// <summary>CreatedAt of the conversion's CalendarConfiguration (CreatedByUserId 0).</summary>
    public DateTime ConvertedAt { get; set; }
}

/// <summary>A converted rule whose legacy week differs but is NOT restored, with why.</summary>
public class MonthlyReanchorOrdinalKeptModel
{
    public int AreaRulePlanningId { get; set; }
    public int PlanningId { get; set; }
    public int CurrentOrdinal { get; set; }
    public int LegacyOrdinal { get; set; }
    public DateTime ConvertedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int UpdatedByUserId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Planning.DayOfWeek / RepeatOrdinalWeek mirror and NextExecutionTime re-snap.</summary>
public class MonthlyReanchorPlanningUpdateModel
{
    public int PlanningId { get; set; }
    public int AreaRulePlanningId { get; set; }
    public int? OldDayOfWeek { get; set; }
    public int NewDayOfWeek { get; set; }
    public int? OldOrdinal { get; set; }
    public int NewOrdinal { get; set; }
    public DateTime? OldNextExecutionTime { get; set; }
    public DateTime? NewNextExecutionTime { get; set; }
}

/// <summary>An open compliance (live, uncompleted SDK case) moved onto its rule's day in the same month.</summary>
public class MonthlyReanchorComplianceMoveModel
{
    public int ComplianceId { get; set; }
    public int PlanningId { get; set; }
    public int AreaRulePlanningId { get; set; }
    public int SdkCaseId { get; set; }
    public int? SdkSiteId { get; set; }
    public DateTime OldDeadline { get; set; }
    public DateTime NewDeadline { get; set; }
}

/// <summary>Off-pattern data the repair deliberately leaves for a person to decide.</summary>
public class MonthlyReanchorReviewItemModel
{
    /// <summary>"Compliance", "NextExecutionTime" or "Rule" (informational, nothing is planned for it).</summary>
    public string Kind { get; set; } = string.Empty;
    public int? ComplianceId { get; set; }
    public int PlanningId { get; set; }
    public int AreaRulePlanningId { get; set; }
    public int? SdkCaseId { get; set; }
    public int? SdkSiteId { get; set; }
    public DateTime CurrentDate { get; set; }
    public DateTime TargetDate { get; set; }
    public List<string> Reasons { get; set; } = [];
}

/// <summary>A live compliance whose SDK case is removed or completed — #1325's cleanup, not this repair's.</summary>
public class MonthlyReanchorSkippedOrphanModel
{
    public int ComplianceId { get; set; }
    public int PlanningId { get; set; }
    public int SdkCaseId { get; set; }
    public DateTime Deadline { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Outcome of the real run: the plan it executed plus what actually got written.</summary>
public class MonthlyReanchorRepairRunResultModel
{
    public MonthlyReanchorRepairPlanModel Plan { get; set; } = new();
    public int RestoredOrdinals { get; set; }
    public int UpdatedPlannings { get; set; }
    public int MovedCompliances { get; set; }
    /// <summary>
    /// Planned writes that did not happen without failing: rows that changed between planning
    /// and writing, and later steps of a planning whose earlier step was not written ("not
    /// attempted"). Any entry makes the marker "partial", so the next dry run re-evaluates them.
    /// </summary>
    public List<string> Skipped { get; set; } = [];
    public List<string> Failures { get; set; } = [];
}
