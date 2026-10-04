using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.AssignmentWorker;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Services.RemovedSiteAssignmentCleanup;

/// <summary>#1376 — see <see cref="RemovedSiteAssignmentCleanupService"/>.</summary>
public interface IRemovedSiteAssignmentCleanupService
{
    /// <summary>Lists every assignment the cleanup would remove and writes nothing.</summary>
    Task<OperationDataResult<RemovedSiteAssignmentCleanupPlanModel>> DryRunAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the planned assignments. Refuses unless <paramref name="planHash"/> equals the
    /// current dry run's <c>PlanHash</c> and the plan removes something.
    /// </summary>
    Task<OperationDataResult<RemovedSiteAssignmentCleanupRunResultModel>> RunAsync(
        string planHash, CancellationToken cancellationToken = default);
}
