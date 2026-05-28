#nullable enable
namespace BackendConfiguration.Pn.Infrastructure.Models.Calendar;

/// <summary>
/// Server-decided outcome for the calendar "complete from indicator" action.
///
/// When <see cref="RequiresForm"/> is <c>false</c>, the backend has already
/// completed the SDK case in place and the frontend just needs to reload.
///
/// When <c>true</c>, the linked eForm template has at least one mandatory
/// field and the frontend must navigate the user to the existing
/// compliance-case form route, using the route params returned here.
///
/// See spec: docs/superpowers/specs/2026-05-21-calendar-complete-case-from-indicator-design.md
/// </summary>
public class CalendarToggleCompleteResult
{
    public bool RequiresForm { get; set; }
    public int? SdkCaseId { get; set; }
    public int? TemplateId { get; set; }
    public int? PropertyId { get; set; }
    public int? ComplianceId { get; set; }
    public int? WorkerId { get; set; }
    public string? Deadline { get; set; }

    /// <summary>
    /// Calendar event start — Compliance.Deadline day + CalendarConfiguration.StartHour
    /// (or any per-occurrence exception override), UTC, ISO 8601 with millisecond
    /// precision. The compliance modal defaults Case.DoneAt /
    /// Case.DoneAtUserModifiable to this value so the completion timestamp reflects
    /// when the work was scheduled rather than when the user happened to click.
    /// </summary>
    public string? EventStart { get; set; }
}
