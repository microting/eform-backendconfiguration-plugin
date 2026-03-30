namespace BackendConfiguration.Pn.Infrastructure.Models.Calendar;

public class CalendarTaskDeleteRequestModel
{
    public int Id { get; set; }
    public string OriginalDate { get; set; }
    public string Scope { get; set; } // "this", "thisAndFollowing", "all"
}
