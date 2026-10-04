using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Compliances;
using BackendConfiguration.Pn.Services.ComplianceSiblingCaseRepair;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Controllers;

/// <summary>
/// #1371 — the one-time repoint of compliances completed by another assigned worker.
/// Admin only, as a whole class. No startup trigger: the real run changes customer data,
/// so it only happens when an admin POSTs it with the <c>planHash</c> of a dry run they
/// reviewed (GET). A changed plan is refused.
/// </summary>
[Authorize(Roles = EformRole.Admin)]
[Route("api/backend-configuration-pn/compliance-repair/sibling-completed-case")]
public class ComplianceRepairController(IComplianceSiblingCaseRepairService repairService) : Controller
{
    /// <summary>Read-only: the compliances the repair would repoint.</summary>
    [HttpGet("dry-run")]
    public async Task<OperationDataResult<ComplianceSiblingCaseRepairPlanModel>> DryRun()
        => await repairService.DryRunAsync(HttpContext.RequestAborted);

    [HttpPost("run")]
    public async Task<OperationDataResult<ComplianceSiblingCaseRepairRunResultModel>> Run([FromQuery] string planHash)
        => await repairService.RunAsync(planHash ?? string.Empty, HttpContext.RequestAborted);
}
