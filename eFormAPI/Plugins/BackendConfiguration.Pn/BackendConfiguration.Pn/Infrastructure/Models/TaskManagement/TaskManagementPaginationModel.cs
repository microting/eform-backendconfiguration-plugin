using Microting.eFormApi.BasePn.Infrastructure.Models.Common;

namespace BackendConfiguration.Pn.Infrastructure.Models.TaskManagement;

public class TaskManagementPaginationModel : FilterAndSortModel
{
    public int Offset { get; set; }
    public int PageIndex { set; get; }
    public int PageSize { get; set; }
    public int Total { get; set; }
}