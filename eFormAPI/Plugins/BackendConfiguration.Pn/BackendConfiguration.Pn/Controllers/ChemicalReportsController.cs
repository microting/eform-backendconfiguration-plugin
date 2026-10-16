/*
The MIT License (MIT)
Copyright (c) 2007 - 2026 Microting A/S
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:
The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.
THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

namespace BackendConfiguration.Pn.Controllers;

using System;
using System.Threading.Tasks;
using Infrastructure.Models.Chemicals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Const;
using Sentry;
using Services.BackendConfigurationComplianceExportService;
using Services.BackendConfigurationLocalizationService;
using Services.ChemicalReporting;

/// <summary>
/// W6 history and the W1/W6 downloads (flutter-chemistry spec section 11). Same prefix and
/// policy as ChemicalsController; callers are eForm web admins (ChemicalCaller.Web).
/// Downloads answer 400 with a readable text on failure and carry a UTF-8 safe
/// Content-Disposition (ComplianceExportFileNaming, as the compliance export does).
/// </summary>
[Authorize(Policy = BackendConfigurationClaims.AccessBackendConfigurationPlugin)]
// TODO(chemistry-GA): remove this gate (and its tests in ChemicalReportsControllerTests) with the one on ChemicalsController.
[FirstUserOnly]
[Route("api/backend-configuration-pn/chemicals")]
public class ChemicalReportsController(
    IChemicalExportService exports,
    IChemicalHistoryService history,
    IUserService userService,
    IBackendConfigurationLocalizationService localizationService) : Controller
{
    [HttpPost("properties/{propertyId:int}/history")]
    public async Task<OperationDataResult<ChemicalHistoryPageModel>> GetHistory(
        int propertyId, [FromBody] ChemicalHistoryRequest request)
    {
        try
        {
            return new OperationDataResult<ChemicalHistoryPageModel>(true,
                await history.GetPageAsync(propertyId, ToQuery(request)).ConfigureAwait(false));
        }
        catch (Exception e)
        {
            if (e is ArgumentException)
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
            }

            return ChemicalsController.FailureResult<ChemicalHistoryPageModel>(
                e, localizationService, ChemicalLocalizationKeys.ErrorWhileReadingChemicalHistory);
        }
    }

    [HttpPost("properties/{propertyId:int}/history/export")]
    public Task<IActionResult> ExportHistory(int propertyId, [FromBody] ChemicalHistoryRequest request) =>
        DownloadAsync(() => exports.HistoryXlsxAsync(propertyId, ToQuery(request)));

    [HttpGet("properties/{propertyId:int}/exports/authority-list")]
    public Task<IActionResult> ExportAuthorityList(int propertyId) =>
        DownloadAsync(() => exports.AuthorityListPdfAsync(userService.UserId, propertyId));

    [HttpPost("properties/{propertyId:int}/exports/inventory")]
    public Task<IActionResult> ExportInventory(int propertyId, [FromBody] ChemicalInventoryExportBody body) =>
        DownloadAsync(() => exports.InventoryXlsxAsync(userService.UserId, propertyId, body?.PlacementIds));

    [HttpGet("properties/{propertyId:int}/exports/removed-placements")]
    public Task<IActionResult> ExportRemovedPlacements(int propertyId) =>
        DownloadAsync(() => exports.RemovedPlacementsXlsxAsync(userService.UserId, propertyId));

    /// <summary>A missing body or a period that is not exactly yyyy-MM-dd is an ArgumentException (a 400).</summary>
    private static ChemicalHistoryQuery ToQuery(ChemicalHistoryRequest request) =>
        (request ?? throw new ArgumentException("A request body is required.")).ToQuery();

    private async Task<IActionResult> DownloadAsync(Func<Task<ChemicalExportFile>> produce)
    {
        try
        {
            var file = await produce().ConfigureAwait(false);
            Response.Headers["Content-Disposition"] = ComplianceExportFileNaming.BuildContentDisposition(file.FileName);
            return File(file.Content, file.ContentType);
        }
        catch (Exception e)
        {
            if (ChemicalsController.ExpectedStatusCode(e) is null)
            {
                SentrySdk.CaptureException(e);
            }

            return BadRequest($"{localizationService.GetString(ChemicalLocalizationKeys.ErrorWhileExportingChemicals)}: {e.Message}");
        }
    }
}
