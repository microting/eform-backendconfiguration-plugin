using System;
using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.TaskList;

public class TaskListBatchRequestModel
{
    public List<int> TaskIds { get; set; } = [];
}

public class TaskListBatchAssignModel : TaskListBatchRequestModel
{
    public int SiteId { get; set; }
}

public class TaskListBatchReassignModel : TaskListBatchRequestModel
{
    public int FromSiteId { get; set; }
    public int ToSiteId { get; set; }
}

public class TaskListBatchChangeEformModel : TaskListBatchRequestModel
{
    public int EformId { get; set; }
}

public class TaskListBatchTagsModel : TaskListBatchRequestModel
{
    public List<int> TagIds { get; set; } = [];
}

public class TaskListBatchComplianceModel : TaskListBatchRequestModel
{
    /// <summary>
    /// true  = "Overskredet opgave vises i app"  — an overdue case is moved
    ///          into the property's "00. Overdue tasks" folder by the nightly job.
    /// false = "Overskredet opgave vises ikke i app" — the job skips the task.
    /// </summary>
    public bool ComplianceEnabled { get; set; }
}

public class TaskListBatchCopyModel : TaskListBatchRequestModel
{
    public int TargetPropertyId { get; set; }
    public int TargetBoardId { get; set; }
    public DateTime StartDate { get; set; }
    public int SiteId { get; set; }
}

/// <summary>
/// #1123 — "Batch: Aktivere/de-aktivere opgaver." One boolean for the whole
/// selection; every other field of the affected tasks round-trips through
/// BuildUpdateModel unchanged.
/// </summary>
public class TaskListBatchStatusModel : TaskListBatchRequestModel
{
    /// <summary>
    /// true  = "Task visible on calendar"  — TaskWizardStatuses.Active (1).
    /// false = "Task dimmed on calendar" — TaskWizardStatuses.NotActive (2);
    ///          the open occurrences are retracted from the app while the
    ///          completed ones and their collected data are preserved.
    /// </summary>
    public bool Active { get; set; }
}

/// <summary>
/// #1122 — "Batch: Ændre startdato til HVILKEN som helst dato." The only
/// caller-supplied value is the new series anchor; every other field of the
/// affected tasks round-trips through BuildUpdateModel unchanged.
/// </summary>
public class TaskListBatchStartDateModel : TaskListBatchRequestModel
{
    public DateTime StartDate { get; set; }
}

/// <summary>
/// #1122 §5 — the read-only projection behind
/// "N opgaver · M åbne forekomster tilbagekaldes · K gennemførte bevares ·
/// L overskredne opgaver oprettes".
///
/// Every number here is produced by the SAME code the apply runs
/// (ICalendarOccurrenceRetractionService.PlanRetractionAsync and
/// ICalendarPastSeriesBackfillService.PlanPastSeriesBackfillAsync), so the
/// preview cannot promise something the save does not deliver.
/// </summary>
public class TaskListBatchStartDatePreviewModel
{
    /// <summary>Selected tasks the apply will actually be able to re-anchor.</summary>
    public int TaskCount { get; set; }

    /// <summary>
    /// Compliance ROWS that will be retracted — not distinct dates. Compliance
    /// has no site column, so one occurrence deployed to two workers is two
    /// rows and can be half-completed; counting dates would under-report.
    /// </summary>
    public int OccurrencesToRetract { get; set; }

    /// <summary>Rows left untouched because their SDK case is completed (invariant R2).</summary>
    public int CompletedPreserved { get; set; }

    /// <summary>(past occurrence x effective site) pairs the backfill will materialise.</summary>
    public int OverdueToCreate { get; set; }
}

/// <summary>
/// #1126 — "Omdøbe opgavenavn." Inline rename from the task-list grid row.
///
/// Extends <see cref="TaskListBatchRequestModel"/> even though the action is
/// SINGLE-row: the controller's shared empty-<c>TaskIds</c> guard and the
/// service's <c>RunPerTask</c>/<c>BuildUpdateModel</c>/<c>UpdateTask</c> rail
/// are what keep the translation dual-write (AreaRuleTranslation +
/// PlanningNameTranslation) and every other side effect identical to the
/// modal's. The frontend always sends a one-element list.
/// </summary>
public class TaskListRenameModel : TaskListBatchRequestModel
{
    /// <summary>
    /// The new task name, in the CALLING USER's language. Empty/whitespace is
    /// rejected before the loop (the edit modal declares the title
    /// <c>Validators.required</c>; the server honours the same rule).
    /// </summary>
    public string Title { get; set; }
}
