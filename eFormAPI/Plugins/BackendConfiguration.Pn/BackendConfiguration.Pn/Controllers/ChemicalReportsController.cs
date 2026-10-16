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
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Const;
using Services.BackendConfigurationLocalizationService;
using Services.ChemicalInventoryService;
using Services.ChemicalReporting;

/// <summary>
/// W6 history and the W1/W6 downloads (flutter-chemistry spec section 11). Same prefix and
/// policy as ChemicalsController; callers are eForm web admins (ChemicalCaller.Web). Failures
/// follow the sibling controllers: the history answers 200 with Success=false and a localized
/// message (the host shows it), downloads answer through ChemicalsController.FileResultAsync.
/// </summary>
[Authorize(Policy = BackendConfigurationClaims.AccessBackendConfigurationPlugin)]
// TODO(chemistry-GA): remove this gate (and its tests in ChemicalReportsControllerTests) with the one on ChemicalsController.
[FirstUserOnly]
[Route("api/backend-configuration-pn/chemicals")]
public class ChemicalReportsController(
    IChemicalExportService exports,
    IChemicalHistoryService history,
    IChemicalPermissionService permissions,
    IUserService userService,
    IBackendConfigurationLocalizationService localizationService) : Controller
{
    private ChemicalCaller Caller => ChemicalCaller.Web(userService.UserId);

    [HttpPost("properties/{propertyId:int}/history")]
    public async Task<OperationDataResult<ChemicalHistoryPageModel>> GetHistory(
        int propertyId, [FromBody] ChemicalHistoryRequest request)
    {
        try
        {
            var query = ChemicalsController.Required(request).ToQuery();
            await permissions.RequireAsync(Caller, propertyId, ChemicalPermission.View).ConfigureAwait(false);
            return new OperationDataResult<ChemicalHistoryPageModel>(true,
                await history.GetPageAsync(propertyId, query).ConfigureAwait(false));
        }
        catch (Exception e)
        {
            return ChemicalsController.FailureResult<ChemicalHistoryPageModel>(
                e, localizationService, ChemicalLocalizationKeys.ErrorWhileReadingChemicalHistory);
        }
    }

    [HttpPost("properties/{propertyId:int}/history/export")]
    public Task<IActionResult> ExportHistory(int propertyId, [FromBody] ChemicalHistoryRequest request) =>
        ChemicalsController.FileResultAsync(this, async () =>
        {
            var query = ChemicalsController.Required(request).ToQuery();
            await permissions.RequireAsync(Caller, propertyId, ChemicalPermission.View).ConfigureAwait(false);
            return await exports.HistoryXlsxAsync(propertyId, query).ConfigureAwait(false);
        });

    [HttpGet("properties/{propertyId:int}/exports/authority-list")]
    public Task<IActionResult> ExportAuthorityList(int propertyId) =>
        ChemicalsController.FileResultAsync(this, () => exports.AuthorityListPdfAsync(userService.UserId, propertyId));

    // A missing body exports every open placement (Plan E decision 4), so the body is optional here.
    [HttpPost("properties/{propertyId:int}/exports/inventory")]
    public Task<IActionResult> ExportInventory(int propertyId, [FromBody] ChemicalInventoryExportBody body) =>
        ChemicalsController.FileResultAsync(this, () => exports.InventoryXlsxAsync(userService.UserId, propertyId, body?.PlacementIds));

    [HttpGet("properties/{propertyId:int}/exports/removed-placements")]
    public Task<IActionResult> ExportRemovedPlacements(int propertyId) =>
        ChemicalsController.FileResultAsync(this, () => exports.RemovedPlacementsXlsxAsync(userService.UserId, propertyId));
}
