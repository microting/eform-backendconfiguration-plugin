using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Services.CalendarWeeklyReanchorRepair;

/// <summary>#1375 — see <see cref="CalendarWeeklyReanchorRepairService"/>.</summary>
public interface ICalendarWeeklyReanchorRepairService
{
    /// <summary>Computes the repair's full plan and writes nothing.</summary>
    Task<OperationDataResult<WeeklyReanchorRepairPlanModel>> DryRunAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the repair. Refuses unless <paramref name="planHash"/> equals the current
    /// dry run's <c>PlanHash</c>, the plan writes something, and the marker is absent,
    /// <c>partial</c>, or an abandoned <c>running</c>.
    /// </summary>
    Task<OperationDataResult<WeeklyReanchorRepairRunResultModel>> RunAsync(
        string planHash, CancellationToken cancellationToken = default);
}
