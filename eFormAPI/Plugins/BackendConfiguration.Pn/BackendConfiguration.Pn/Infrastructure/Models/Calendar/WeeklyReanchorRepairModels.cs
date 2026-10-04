using System;
using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.Calendar;

/// <summary>
/// #1375 — what the one-time weekly re-anchor repair would do (dry run) or did (real
/// run). Lists are ordered by id, so two dry runs over the same data produce the same
/// <see cref="PlanHash"/>.
/// </summary>
public class WeeklyReanchorRepairPlanModel
{
    /// <summary>
    /// SHA-256 over the plan date, the rule updates and the review list. The real run
    /// refuses to start unless it is handed the hash of the dry run the reviewer signed off.
    /// </summary>
    public string PlanHash { get; set; } = string.Empty;

    /// <summary>The Danish (Europe/Copenhagen) date "in the past" was judged against.</summary>
    public DateTime Today { get; set; }

    /// <summary>True when the repair has completed (marker "done"): the real run will refuse.</summary>
    public bool AlreadyExecuted { get; set; }

    /// <summary>The marker's value: null (never run), "running", "partial" (re-runnable) or "done".</summary>
    public string MarkerState { get; set; }

    public List<WeeklyReanchorRuleUpdateModel> RuleUpdates { get; set; } = [];
    public List<WeeklyReanchorReviewItemModel> ReviewItems { get; set; } = [];
}

/// <summary>
/// A converted weekly rule moved onto its cadence's weekday: the planning (its weekday
/// mirror, and the next run when it must move onto the rule) and the rule
/// (weekday and weekday list). No compliance is moved.
/// </summary>
public class WeeklyReanchorRuleUpdateModel
{
    public int AreaRulePlanningId { get; set; }
    public int PlanningId { get; set; }
    public int OldRuleDayOfWeek { get; set; }
    public string OldWeekdaysCsv { get; set; }
    public int? OldPlanningDayOfWeek { get; set; }
    public int NewDayOfWeek { get; set; }
    public DateTime? OldNextExecutionTime { get; set; }
    public DateTime? NewNextExecutionTime { get; set; }
}

/// <summary>A converted weekly rule off its cadence that the repair leaves for a person to decide.</summary>
public class WeeklyReanchorReviewItemModel
{
    public int AreaRulePlanningId { get; set; }
    public int PlanningId { get; set; }
    public int RuleDayOfWeek { get; set; }
    /// <summary>The weekday the rule would move to.</summary>
    public int TargetDayOfWeek { get; set; }
    /// <summary>Open compliances whose deadline is on another weekday than the target.</summary>
    public List<int> ComplianceIds { get; set; } = [];
    public List<string> Reasons { get; set; } = [];
}

/// <summary>Outcome of the real run: the plan it executed plus what actually got written.</summary>
public class WeeklyReanchorRepairRunResultModel
{
    public WeeklyReanchorRepairPlanModel Plan { get; set; } = new();
    public int UpdatedRules { get; set; }
    /// <summary>
    /// Planned writes that did not happen without failing (the row changed since the plan,
    /// or an earlier write of the same rule was not made). Any entry makes the marker "partial".
    /// </summary>
    public List<string> Skipped { get; set; } = [];
    /// <summary>Writes that became eligible during the run; not written, so the marker stays "partial".</summary>
    public List<string> ArrivedDuringRun { get; set; } = [];
    public List<string> Failures { get; set; } = [];
}
