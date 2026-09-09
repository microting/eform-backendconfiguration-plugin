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
    /// <summary>#1187: the ARP's non-removed PlanningSites site ids, so the complete
    /// modal can group "assigned" vs "other" workers and pre-select a lone assignee.
    /// This is the row's ASSIGNMENT and is deliberately NARROWER than
    /// <see cref="WorkerNames"/>, which since #1232 also names the live members of any
    /// worker tag the ARP is assigned to. It is neither the same set nor positionally
    /// aligned with it — see the assignment site in
    /// <c>BackendConfigurationComplianceReportService.Index</c>.</summary>
    public List<int> WorkerSiteIds { get; set; } = [];
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
