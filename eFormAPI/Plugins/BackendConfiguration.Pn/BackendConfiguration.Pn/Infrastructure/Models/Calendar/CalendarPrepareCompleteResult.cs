namespace BackendConfiguration.Pn.Infrastructure.Models.Calendar;

public class CalendarPrepareCompleteResult
{
    public int SdkCaseId { get; set; }
    public int? TemplateId { get; set; }
    public int PropertyId { get; set; }
    public int ComplianceId { get; set; }
    /// <summary>The SDK site the case is currently deployed to — the modal's default worker preselect fallback.</summary>
    public int? AssignedSiteId { get; set; }
    /// <summary>ISO 8601 UTC (same format as CalendarToggleCompleteResult.Deadline).</summary>
    public string Deadline { get; set; }
    /// <summary>ISO 8601 UTC event start (Deadline day + effective StartHour).</summary>
    public string EventStart { get; set; }
}
