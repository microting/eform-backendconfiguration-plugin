using Microting.eFormApi.BasePn.Infrastructure.Interfaces;

namespace BackendConfiguration.Pn.Infrastructure.Models.Calendar;

/// <summary>
/// Sort + pagination request for the calendar task list. Pagination fields are accepted for
/// parity with the task-wizard request, but the endpoint returns the full filtered+sorted list
/// and the grid paginates client-side (server applies sort only, via QueryHelper.AddSortToQuery).
/// </summary>
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
