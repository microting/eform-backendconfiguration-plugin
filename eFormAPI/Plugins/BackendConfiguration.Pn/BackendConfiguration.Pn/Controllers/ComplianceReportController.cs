using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;
using BackendConfiguration.Pn.Services.BackendConfigurationComplianceExportService;
using BackendConfiguration.Pn.Services.BackendConfigurationComplianceReportService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
    private readonly IBackendConfigurationComplianceExportService _complianceExportService;

    public ComplianceReportController(
        IBackendConfigurationComplianceReportService complianceReportService,
        IBackendConfigurationComplianceExportService complianceExportService)
    {
        _complianceReportService = complianceReportService;
        _complianceExportService = complianceExportService;
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

    /// <summary>
    /// Server-side CSV / Excel / PDF export for all three view modes (#1169).
    ///
    /// <para>
    /// ONE endpoint, nine combinations: <c>viewMode</c> ∈ {overview, details,
    /// report} × <c>format</c> ∈ {csv, xlsx, pdf}. The body carries the CURRENT
    /// FILTER SET, so the file always matches the filter bar — including while the
    /// screen still shows the pre-fetch placeholder, which is why no download
    /// control has to be disabled (#1169 §3).
    /// </para>
    ///
    /// <para>
    /// POST rather than GET because the filters are a structured object with four
    /// multi-select lists — the same body the three read endpoints above take. The
    /// client saves the response from a blob.
    /// </para>
    ///
    /// <para>
    /// Returns <c>400</c> with a plain-text message on an unknown view mode or
    /// format, on a failure inside the report service, and — for <c>pdf</c> — when
    /// <c>soffice</c> is unavailable.
    /// </para>
    ///
    /// <para>
    /// The download carries <c>Content-Disposition: attachment</c> with BOTH an
    /// ASCII fallback and an RFC 5987 <c>filename*=UTF-8''…</c>, so a property named
    /// <c>Miljøtilsyn</c> keeps its <c>ø</c>. That pairing is
    /// <c>CalendarController.DownloadFile</c>'s, the only correct download header in
    /// this plugin; <c>ReportController.GenerateReportFile</c> sets none at all.
    /// </para>
    /// </summary>
    [HttpPost("export")]
    [ProducesResponseType(typeof(string), 400)]
    public async Task<IActionResult> Export(
        [FromBody] ComplianceReportExportRequestModel requestModel, CancellationToken cancellationToken)
    {
        var result = await _complianceExportService.Export(requestModel, cancellationToken);
        if (!result.Success)
        {
            return BadRequest(result.Message);
        }

        // Assignment, not Append: Append would ADD a second Content-Disposition if
        // one were ever already on the response, and two of them is undefined.
        Response.Headers["Content-Disposition"] =
            ComplianceExportFileNaming.BuildContentDisposition(result.Model.FileName);

        return File(result.Model.Content, result.Model.MimeType);
    }
}
