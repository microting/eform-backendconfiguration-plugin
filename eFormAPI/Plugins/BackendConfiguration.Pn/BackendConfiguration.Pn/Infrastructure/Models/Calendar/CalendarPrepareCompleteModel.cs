namespace BackendConfiguration.Pn.Infrastructure.Models.Calendar;

public class CalendarPrepareCompleteModel
{
    public int? ComplianceId { get; set; }
    /// <summary>Occurrence date, yyyy-MM-dd — required when the compliance row must be materialised on demand.</summary>
    public string OccurrenceDate { get; set; }
    /// <summary>
    /// #1300: <c>"compliance"</c> when the compliance pages (Detaljer) open the complete
    /// modal — adds the "no completing a task dated after today" block. The calendar sends
    /// nothing and keeps its intended early completion. Never relaxes any other check.
    /// </summary>
    public string Source { get; set; }
}
