using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.Calendar;

public class CalendarTaskListFiltrationModel
{
    public List<int> PropertyIds { get; set; } = [];
    public List<int> BoardIds { get; set; } = [];
    public List<int> EformIds { get; set; } = [];
    public List<int> AssignToIds { get; set; } = [];
    public List<int> TagIds { get; set; } = [];
    public bool? Status { get; set; }
    public bool? ComplianceEnabled { get; set; }
    public string? NameFilter { get; set; }
}
