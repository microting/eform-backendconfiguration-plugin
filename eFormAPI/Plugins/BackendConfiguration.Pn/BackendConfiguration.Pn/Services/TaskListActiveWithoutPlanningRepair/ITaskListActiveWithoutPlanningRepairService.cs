using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.TaskList;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Services.TaskListActiveWithoutPlanningRepair;

/// <summary>#1376 — see <see cref="TaskListActiveWithoutPlanningRepairService"/>.</summary>
public interface ITaskListActiveWithoutPlanningRepairService
{
    /// <summary>Computes the cleanup's full plan and writes nothing.</summary>
    Task<OperationDataResult<ActiveWithoutPlanningRepairPlanModel>> DryRunAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the cleanup. Refuses unless <paramref name="planHash"/> equals the current
    /// dry run's <c>PlanHash</c> and the plan deletes something.
    /// </summary>
    Task<OperationDataResult<ActiveWithoutPlanningRepairRunResultModel>> RunAsync(
        string planHash, CancellationToken cancellationToken = default);
}
