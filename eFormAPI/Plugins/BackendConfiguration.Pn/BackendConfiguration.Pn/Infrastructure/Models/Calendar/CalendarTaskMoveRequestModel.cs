namespace BackendConfiguration.Pn.Infrastructure.Models.Calendar;

public class CalendarTaskMoveRequestModel
{
    public int Id { get; set; }
    public string NewDate { get; set; }
    public double NewStartHour { get; set; }
}
