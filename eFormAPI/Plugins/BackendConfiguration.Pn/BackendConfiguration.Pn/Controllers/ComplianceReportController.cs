using System.Collections.Generic;
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

    /// <summary>
    /// The Oversigt view's aggregation (#1162): one compliance summary row per
    /// property plus a weighted totals row.
    ///
    /// Unpaged and unsorted deliberately — one row per property, and #1164 sorts
    /// client-side. The request model carries no <c>Status</c>: Oversigt counts
    /// done and not-done together.
    /// </summary>
    [HttpPost("overview")]
    public async Task<OperationDataResult<ComplianceReportOverviewModel>> Overview(
        [FromBody] ComplianceReportOverviewRequestModel requestModel)
    {
        return await _complianceReportService.Overview(requestModel);
    }

    /// <summary>
    /// The Rapport view's per-template answer columns (#1166): tag groups →
    /// template groups → an ordered column schema plus one keyed cell bag per case.
    ///
    /// Unpaged — Rapport groups the whole filtered set, so the request's paging and
    /// sorting fields are ignored and the service's row cap applies instead.
    /// </summary>
    [HttpPost("eform-columns")]
    public async Task<OperationDataResult<List<ComplianceReportTagGroupModel>>> EformColumns(
        [FromBody] ComplianceReportRequestModel requestModel)
    {
        return await _complianceReportService.EformColumns(requestModel);
    }
}
