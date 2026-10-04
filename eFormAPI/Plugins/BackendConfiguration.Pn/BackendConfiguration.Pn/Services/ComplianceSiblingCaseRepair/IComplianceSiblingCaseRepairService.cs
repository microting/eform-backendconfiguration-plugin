using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Compliances;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Services.ComplianceSiblingCaseRepair;

/// <summary>#1371 — see <see cref="ComplianceSiblingCaseRepairService"/>.</summary>
public interface IComplianceSiblingCaseRepairService
{
    /// <summary>Computes the repair's full plan and writes nothing.</summary>
    Task<OperationDataResult<ComplianceSiblingCaseRepairPlanModel>> DryRunAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the repair. Refuses unless <paramref name="planHash"/> equals the current
    /// dry run's <c>PlanHash</c> (the data is unchanged since the review) and the plan
    /// writes something.
    /// </summary>
    Task<OperationDataResult<ComplianceSiblingCaseRepairRunResultModel>> RunAsync(
        string planHash, CancellationToken cancellationToken = default);
}
