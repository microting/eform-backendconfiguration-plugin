namespace BackendConfiguration.Pn.Infrastructure.Models.Calendar;

public class CalendarTaskResizeRequestModel
{
    public int Id { get; set; }
    public double NewStartHour { get; set; }
    public double NewDuration { get; set; }
    public string OriginalDate { get; set; }
    public string Scope { get; set; } // "this", "thisAndFollowing", "all"
}
