namespace BackendConfiguration.Pn.Infrastructure.Models.Calendar;

public class CalendarTaskIndexRequestModel
{
    public CalendarTaskListFiltrationModel Filters { get; set; } = new();
    public CalendarTaskListPaginationModel Pagination { get; set; } = new();
}
