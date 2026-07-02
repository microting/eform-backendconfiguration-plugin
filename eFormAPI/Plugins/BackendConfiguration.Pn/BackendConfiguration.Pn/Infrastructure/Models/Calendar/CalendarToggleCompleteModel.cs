namespace BackendConfiguration.Pn.Infrastructure.Models.Calendar;

public class CalendarToggleCompleteModel
{
    public bool Completed { get; set; }

    // Specific compliance occurrence the user clicked. The frontend sends
    // task.complianceId from the calendar response; the backend uses this
    // to identify the exact Compliance row instead of doing a "latest by
    // Deadline" lookup on PlanningId, which would silently target the
    // wrong week when multiple compliances exist for the same planning.
    public int? ComplianceId { get; set; }

    // Calendar-day key (YYYY-MM-DD) for the specific occurrence the user
    // clicked. Forwarded to the on-demand Compliance materialiser when
    // ComplianceId is missing/zero — for recurrence rows the nightly
    // batch has not yet deployed, this is the only way the backend can
    // identify which deadline to create the Compliance row for. Sent as
    // task.taskDate from the calendar response.
    public string? OccurrenceDate { get; set; }

    // SDK site id to attribute the completion to. The calendar frontend
    // ALWAYS shows the "Vælg medarbejder" picker when completing — listing
    // every worker assigned to the event's property
    // (GetLinkedSites(propertyId, compliance: false)) — and sends the
    // picked id here. Null (gRPC / legacy callers) falls back to the
    // historical defaults: the task's first PlanningSite when the case is
    // materialised on demand, or the case's deployed site otherwise.
    public int? WorkerId { get; set; }
}
