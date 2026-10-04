using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.TaskList;
using BackendConfiguration.Pn.Services.TaskListActiveWithoutPlanningRepair;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Controllers;

/// <summary>
/// #1376 — the one-time cleanup of active task rows without a live planning. Admin
/// only, as a whole class. No startup trigger: the real run deletes customer data, so
/// it only happens when an admin POSTs it with the <c>planHash</c> of a dry run they
/// reviewed (GET). A changed plan, or a plan with nothing to delete, is refused.
/// </summary>
[Authorize(Roles = EformRole.Admin)]
[Route("api/backend-configuration-pn/task-list-repair/active-without-planning")]
public class TaskListRepairController(ITaskListActiveWithoutPlanningRepairService repairService) : Controller
{
    /// <summary>Read-only: what the cleanup would delete, and which rows it leaves alone.</summary>
    [HttpGet("dry-run")]
    public async Task<OperationDataResult<ActiveWithoutPlanningRepairPlanModel>> DryRun()
        => await repairService.DryRunAsync(HttpContext.RequestAborted);

    [HttpPost("run")]
    public async Task<OperationDataResult<ActiveWithoutPlanningRepairRunResultModel>> Run([FromQuery] string planHash)
        => await repairService.RunAsync(planHash ?? string.Empty, HttpContext.RequestAborted);
}
