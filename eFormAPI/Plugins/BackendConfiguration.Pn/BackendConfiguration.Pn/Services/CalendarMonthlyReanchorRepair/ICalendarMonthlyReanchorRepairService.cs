using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Services.CalendarMonthlyReanchorRepair;

/// <summary>#1294 — see <see cref="CalendarMonthlyReanchorRepairService"/>.</summary>
public interface ICalendarMonthlyReanchorRepairService
{
    /// <summary>Computes the repair's full plan and writes nothing.</summary>
    Task<OperationDataResult<MonthlyReanchorRepairPlanModel>> DryRunAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the repair. Refuses unless <paramref name="planHash"/> equals the current
    /// dry run's <c>PlanHash</c> (the data is unchanged since the review), the plan writes
    /// something, and the marker is absent, <c>partial</c>, or an abandoned <c>running</c>.
    /// </summary>
    Task<OperationDataResult<MonthlyReanchorRepairRunResultModel>> RunAsync(
        string planHash, CancellationToken cancellationToken = default);
}
