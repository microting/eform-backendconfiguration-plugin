namespace BackendConfiguration.Pn.Infrastructure.Models.TaskManagement;

public class TaskManagementRequestModel
{
    public TaskManagementFiltersModel Filters { get; set; }

    public TaskManagementPaginationModel Pagination { get; set; }
}