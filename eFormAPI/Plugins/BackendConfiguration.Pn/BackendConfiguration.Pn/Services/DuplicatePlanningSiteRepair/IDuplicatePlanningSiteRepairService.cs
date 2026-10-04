using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Services.DuplicatePlanningSiteRepair;

/// <summary>#1385 — see <see cref="DuplicatePlanningSiteRepairService"/>.</summary>
public interface IDuplicatePlanningSiteRepairService
{
    /// <summary>Computes the repair's full plan and writes nothing.</summary>
    Task<OperationDataResult<DuplicatePlanningSiteRepairPlanModel>> DryRunAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the repair. Refuses unless <paramref name="planHash"/> equals the current
    /// dry run's <c>PlanHash</c> (the data is unchanged since the review) and the plan
    /// removes something.
    /// </summary>
    Task<OperationDataResult<DuplicatePlanningSiteRepairRunResultModel>> RunAsync(
        string planHash, CancellationToken cancellationToken = default);
}
