using System;
using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;

/// <summary>
/// One compliance-report row. Every field of
/// <see cref="Calendar.CalendarComplianceReportRowModel"/> plus
/// <see cref="CheckListId"/>.
/// </summary>
public class ComplianceReportRowModel
{
    public int ComplianceId { get; set; }
    /// <summary>Occurrence date, yyyy-MM-dd (exception NewDate applied).</summary>
    public string TaskDate { get; set; }
    public double StartHour { get; set; }
    public double Duration { get; set; }
    public bool IsAllDay { get; set; }
    public string Title { get; set; }
    public int PropertyId { get; set; }
    public string PropertyName { get; set; }
    public int? BoardId { get; set; }
    public string BoardName { get; set; }
    public List<string> Tags { get; set; } = [];
    public List<string> WorkerNames { get; set; } = [];
    /// <summary>#1187: the ARP's non-removed PlanningSites site ids — the row's
    /// EXPLICIT INDIVIDUAL assignees, and the only set the complete modal pre-selects a
    /// lone assignee from. Deliberately NARROWER than <see cref="WorkerNames"/>, which
    /// since #1232 also names the live members of any worker tag the ARP is assigned
    /// to. It is neither the same set nor positionally aligned with it — see the
    /// assignment site in <c>BackendConfigurationComplianceReportService.Index</c>.
    /// The team half of the assignment is <see cref="TeamAssigneeIds"/>; the modal
    /// GROUPS on the union of the two (#1236).</summary>
    public List<int> WorkerSiteIds { get; set; } = [];

    /// <summary>#1236: the site ids assigned to this row VIA A WORKER TAG ("team") —
    /// the live members of every worker tag the ARP is assigned to, resolved through
    /// <c>IWorkerTagMembershipService</c>. Empty (never null) for a row whose ARP has
    /// no worker tag.
    ///
    /// <para>Kept BESIDE <see cref="WorkerSiteIds"/> rather than merged into it. The
    /// complete-event modal groups its worker dropdown on the union of the two, so a
    /// team's members show under "assigned to this event"; it pre-selects a lone
    /// completer from <see cref="WorkerSiteIds"/> ALONE, so nobody is recorded as
    /// having completed a case on the strength of team membership. The calendar grid
    /// feeds that same modal <c>CalendarTaskResponseModel.AssigneeIds</c> +
    /// <c>TeamAssigneeIds</c>, which is the same split — the two views therefore agree
    /// about who is assigned.</para></summary>
    public List<int> TeamAssigneeIds { get; set; } = [];
    public bool Completed { get; set; }
    public DateTime? DoneAt { get; set; }
    public int SdkCaseId { get; set; }
    public int? EformId { get; set; }
    public int PlanningId { get; set; }
    public int? AreaRulePlanningId { get; set; }

    /// <summary>SDK Case.CheckListId — the template actually answered.
    /// The template key for #1166; EformId is NOT (see #1160 finding 1).</summary>
    public int? CheckListId { get; set; }
}
