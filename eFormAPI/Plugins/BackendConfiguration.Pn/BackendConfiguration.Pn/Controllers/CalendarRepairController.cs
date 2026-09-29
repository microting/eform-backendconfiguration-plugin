using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.CalendarMonthlyReanchorRepair;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Controllers;

/// <summary>
/// #1294 — the one-time monthly re-anchor repair. Admin only, as a whole class.
/// There is deliberately no startup trigger: the real run changes customer data, so it
/// only happens when an admin POSTs it with the <c>planHash</c> of a dry run they
/// reviewed (GET). A changed plan or a second run is refused.
/// </summary>
[Authorize(Roles = EformRole.Admin)]
[Route("api/backend-configuration-pn/calendar-repair/monthly-reanchor")]
public class CalendarRepairController(ICalendarMonthlyReanchorRepairService repairService) : Controller
{
    /// <summary>Read-only: what the repair would do, including the review list.</summary>
    [HttpGet("dry-run")]
    public async Task<OperationDataResult<MonthlyReanchorRepairPlanModel>> DryRun()
        => await repairService.DryRunAsync(HttpContext.RequestAborted);

    [HttpPost("run")]
    public async Task<OperationDataResult<MonthlyReanchorRepairRunResultModel>> Run([FromQuery] string planHash)
        => await repairService.RunAsync(planHash ?? string.Empty, HttpContext.RequestAborted);
}
