namespace BackendConfiguration.Pn.Infrastructure.Models.Calendar;

public class CalendarPrepareCompleteModel
{
    public int? ComplianceId { get; set; }
    /// <summary>Occurrence date, yyyy-MM-dd — required when the compliance row must be materialised on demand.</summary>
    public string OccurrenceDate { get; set; }
}
