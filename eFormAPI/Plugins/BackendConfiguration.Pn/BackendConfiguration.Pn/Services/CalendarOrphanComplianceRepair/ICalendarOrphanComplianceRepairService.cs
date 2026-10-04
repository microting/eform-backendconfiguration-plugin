using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Services.CalendarOrphanComplianceRepair;

/// <summary>#1383 — see <see cref="CalendarOrphanComplianceRepairService"/>.</summary>
public interface ICalendarOrphanComplianceRepairService
{
    /// <summary>Computes which orphans the repair would close, and writes nothing.</summary>
    Task<OperationDataResult<OrphanOffPatternComplianceRepairPlanModel>> DryRunAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the planned orphans. Refuses unless <paramref name="planHash"/> equals the
    /// current dry run's <c>PlanHash</c> and the plan closes something.
    /// </summary>
    Task<OperationDataResult<OrphanOffPatternComplianceRepairRunResultModel>> RunAsync(
        string planHash, CancellationToken cancellationToken = default);
}
