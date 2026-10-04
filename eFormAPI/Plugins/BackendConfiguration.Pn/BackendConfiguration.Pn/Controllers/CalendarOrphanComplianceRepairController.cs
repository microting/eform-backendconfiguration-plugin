using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.CalendarOrphanComplianceRepair;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Controllers;

/// <summary>
/// #1383 — closes orphaned off-pattern compliances of monthly rules. Admin only, as a
/// whole class. There is deliberately no startup trigger: the real run soft-deletes
/// compliances, so it only happens when an admin POSTs it with the <c>planHash</c> of a
/// dry run they reviewed (GET). A changed plan or an empty plan is refused.
/// </summary>
[Authorize(Roles = EformRole.Admin)]
[Route("api/backend-configuration-pn/calendar-repair/orphan-off-pattern-compliances")]
public class CalendarOrphanComplianceRepairController(ICalendarOrphanComplianceRepairService repairService)
    : Controller
{
    /// <summary>Read-only: which orphans the repair would close, and which it keeps.</summary>
    [HttpGet("dry-run")]
    public async Task<OperationDataResult<OrphanOffPatternComplianceRepairPlanModel>> DryRun()
        => await repairService.DryRunAsync(HttpContext.RequestAborted);

    [HttpPost("run")]
    public async Task<OperationDataResult<OrphanOffPatternComplianceRepairRunResultModel>> Run(
        [FromQuery] string planHash)
        => await repairService.RunAsync(planHash ?? string.Empty, HttpContext.RequestAborted);
}
