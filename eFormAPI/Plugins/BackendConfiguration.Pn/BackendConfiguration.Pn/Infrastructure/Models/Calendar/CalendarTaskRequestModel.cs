using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.Calendar;

public class CalendarTaskRequestModel
{
    public int PropertyId { get; set; }
    public string WeekStart { get; set; }
    public string WeekEnd { get; set; }
    public List<int> BoardIds { get; set; } = [];
    public List<string> TagNames { get; set; } = [];
}
