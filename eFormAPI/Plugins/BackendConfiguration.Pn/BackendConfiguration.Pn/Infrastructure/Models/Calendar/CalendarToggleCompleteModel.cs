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
}
