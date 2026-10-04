using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.DuplicatePlanningSiteRepair;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Controllers;

/// <summary>
/// #1385 — the one-time cleanup of duplicate PlanningSites rows. Admin only, as a whole
/// class, and never triggered at startup: the real run only happens when an admin POSTs it
/// with the <c>planHash</c> of a dry run they reviewed (GET). A changed plan is refused.
/// </summary>
[Authorize(Roles = EformRole.Admin)]
[Route("api/backend-configuration-pn/calendar-repair/duplicate-planning-sites")]
public class DuplicatePlanningSiteRepairController(IDuplicatePlanningSiteRepairService repairService) : Controller
{
    /// <summary>Read-only: which rows the repair would remove.</summary>
    [HttpGet("dry-run")]
    public async Task<OperationDataResult<DuplicatePlanningSiteRepairPlanModel>> DryRun()
        => await repairService.DryRunAsync(HttpContext.RequestAborted);

    [HttpPost("run")]
    public async Task<OperationDataResult<DuplicatePlanningSiteRepairRunResultModel>> Run([FromQuery] string planHash)
        => await repairService.RunAsync(planHash ?? string.Empty, HttpContext.RequestAborted);
}
