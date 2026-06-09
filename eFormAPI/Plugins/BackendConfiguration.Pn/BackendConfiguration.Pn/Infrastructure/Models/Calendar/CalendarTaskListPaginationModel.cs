using Microting.eFormApi.BasePn.Infrastructure.Interfaces;

namespace BackendConfiguration.Pn.Infrastructure.Models.Calendar;

public class CalendarTaskListPaginationModel : ICommonPagination, ICommonSort
{
    /// <inheritdoc />
    public int PageSize { get; set; }

    /// <inheritdoc />
    public int Offset { get; set; }

    /// <inheritdoc />
    public string Sort { get; set; }

    /// <inheritdoc />
    public bool IsSortDsc { get; set; }
}
