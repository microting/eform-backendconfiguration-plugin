using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;
using BackendConfiguration.Pn.Services.BackendConfigurationComplianceReportService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Controllers;

/// <summary>
/// The standalone Compliance page's backend surface (#1160). Deliberately its own
/// prefix rather than a second report shape bolted onto
/// <see cref="CompliancesController"/> (the /compliances surface task-tracker
/// depends on) or onto <see cref="CalendarController"/> (the page is not a
/// calendar view mode any more).
///
/// <c>[Authorize]</c> with no claim gating, per #1160 decision 6.
/// </summary>
[Authorize]
[Route("api/backend-configuration-pn/compliance-report")]
public class ComplianceReportController : Controller
{
    private readonly IBackendConfigurationComplianceReportService _complianceReportService;

    public ComplianceReportController(IBackendConfigurationComplianceReportService complianceReportService)
    {
        _complianceReportService = complianceReportService;
    }

    [HttpPost("index")]
    public async Task<OperationDataResult<ComplianceReportPagedModel>> Index(
        [FromBody] ComplianceReportRequestModel requestModel)
    {
        return await _complianceReportService.Index(requestModel);
    }
}
