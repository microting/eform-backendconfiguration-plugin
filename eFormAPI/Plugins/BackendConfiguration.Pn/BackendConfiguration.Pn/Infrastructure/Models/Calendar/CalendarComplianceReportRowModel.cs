using System;
using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.Calendar;

public class CalendarComplianceReportRowModel
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
    public bool Completed { get; set; }
    public DateTime? DoneAt { get; set; }
    public int SdkCaseId { get; set; }
    public int? EformId { get; set; }
    public int PlanningId { get; set; }
    public int? AreaRulePlanningId { get; set; }
}
