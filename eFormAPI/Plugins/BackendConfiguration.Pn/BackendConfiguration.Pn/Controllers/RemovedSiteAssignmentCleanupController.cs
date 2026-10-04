using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.AssignmentWorker;
using BackendConfiguration.Pn.Services.RemovedSiteAssignmentCleanup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Controllers;

/// <summary>
/// #1376 — the one-off cleanup of assignments that removed workers left on inactive legacy
/// rules. Admin only, as a whole class. There is deliberately no startup trigger: the real
/// run changes customer data, so it only happens when an admin POSTs it with the
/// <c>planHash</c> of a dry run they reviewed (GET). A changed plan or an empty one is refused.
/// </summary>
[Authorize(Roles = EformRole.Admin)]
[Route("api/backend-configuration-pn/worker-assignment-repair/removed-sites")]
public class RemovedSiteAssignmentCleanupController(IRemovedSiteAssignmentCleanupService cleanupService) : Controller
{
    /// <summary>Read-only: every assignment the cleanup would remove.</summary>
    [HttpGet("dry-run")]
    public async Task<OperationDataResult<RemovedSiteAssignmentCleanupPlanModel>> DryRun()
        => await cleanupService.DryRunAsync(HttpContext.RequestAborted);

    [HttpPost("run")]
    public async Task<OperationDataResult<RemovedSiteAssignmentCleanupRunResultModel>> Run([FromQuery] string planHash)
        => await cleanupService.RunAsync(planHash ?? string.Empty, HttpContext.RequestAborted);
}
