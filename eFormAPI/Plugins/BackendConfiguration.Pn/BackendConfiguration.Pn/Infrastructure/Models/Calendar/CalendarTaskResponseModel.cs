using System;
using System.Collections.Generic;
using Microting.eForm.Infrastructure.Models;

namespace BackendConfiguration.Pn.Infrastructure.Models.Calendar;

public class CalendarTaskResponseModel
{
    public int Id { get; set; }
    public string Title { get; set; }
    public double StartHour { get; set; }
    public double Duration { get; set; }
    public string TaskDate { get; set; }
    public List<string> Tags { get; set; } = [];
    /// <summary>The EXPLICIT individual assignees: the ARP's own non-removed
    /// PlanningSites site ids (or an occurrence exception's ExceptionSites when the
    /// occurrence carries one). Does NOT include worker-tag members — those are in
    /// <see cref="TeamAssigneeIds"/>.</summary>
    public List<int> AssigneeIds { get; set; } = [];

    /// <summary>#1236: the site ids assigned to this event VIA A WORKER TAG ("team") —
    /// the live members of every tag in <see cref="WorkerTagIds"/>, resolved through
    /// <c>IWorkerTagMembershipService</c>. Empty (never null) when the event has no
    /// worker tag, and empty on any producer that does not load worker tags.
    ///
    /// <para>Kept BESIDE <see cref="AssigneeIds"/> rather than merged into it. The
    /// complete-event modal groups its worker dropdown on the union of the two, so a
    /// team's members show under "assigned to this event"; it pre-selects a lone
    /// completer from <see cref="AssigneeIds"/> ALONE, so nobody is recorded as having
    /// completed a case on the strength of team membership. A site that is both an
    /// explicit assignee and a team member appears in both lists; the modal de-dupes
    /// when it unions them.</para></summary>
    public List<int> TeamAssigneeIds { get; set; } = [];

    // Worker tags assigned to this event (SDK Tag ids).
    public List<int> WorkerTagIds { get; set; } = [];
    public List<string> WorkerNames { get; set; } = [];
    public int? BoardId { get; set; }
    public string Color { get; set; }
    public int RepeatType { get; set; }
    public int RepeatEvery { get; set; }
    public int? RepeatEndMode { get; set; }
    public int? RepeatOccurrences { get; set; }
    public DateTime? RepeatUntilDate { get; set; }
    public int? DayOfWeek { get; set; }
    public int? DayOfMonth { get; set; }
    // Nth-weekday-of-month ordinal (1..5). Non-null when the rule uses
    // "Nth <weekday> of each month". Null for legacy day-of-month rules.
    public int? RepeatOrdinalWeek { get; set; }
    public string? RepeatWeekdaysCsv { get; set; }
    public bool Completed { get; set; }
    public bool Status { get; set; }
    public bool ComplianceEnabled { get; set; }
    public int PropertyId { get; set; }
    public int? ComplianceId { get; set; }
    public bool IsFromCompliance { get; set; }
    public DateTime? Deadline { get; set; }
    public DateTime? NextExecutionTime { get; set; }
    public int? PlanningId { get; set; }
    public bool IsAllDay { get; set; }
    public int? ExceptionId { get; set; }
    public int? EformId { get; set; }
    public int? SdkCaseId { get; set; }
    public int? ItemPlanningTagId { get; set; }
    public string? DescriptionHtml { get; set; }

    /// <summary>
    /// Per-language Title + Description for the task's AreaRule, so the edit
    /// modal can pre-fill the multi-language fields. Empty for compliance/orphan
    /// rows that have no AreaRule. The single Title/DescriptionHtml above remain
    /// the caller-language values used for rendering.
    /// </summary>
    public List<CommonTranslationsModel> Translations { get; set; } = [];
    public List<CalendarTaskAttachmentDto> Attachments { get; set; } = new();

    /// <summary>
    /// True when the underlying compliance/case is past deadline AND not
    /// completed (or retracted with SDK Case Status=77). Populated by both
    /// the GetTaskTrackerList and GetTasksForWeek service paths.
    /// </summary>
    public bool TaskIsExpired { get; set; }

    /// <summary>
    /// Completion metadata for completed compliance-backed rows: the site
    /// (worker) name that checked the case and when it was done. Null for
    /// uncompleted rows and recurrence-derived rows.
    /// </summary>
    public string? DoneByName { get; set; }
    public DateTime? DoneAt { get; set; }
}
