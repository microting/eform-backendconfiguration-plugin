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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Infrastructure.Models.Chemicals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Const;
using Sentry;
using Services.BackendConfigurationLocalizationService;
using Services.ChemicalInventoryService;

/// <summary>
/// REST façade for the Kemi web screens (flutter-chemistry spec §11, W1–W5)
/// over IChemicalInventoryService, the same service ChemicalsGrpcService uses.
/// Web users are eForm web admins: the class-level policy bounds reach and
/// every call runs as ChemicalCaller.Web (spec §8: worker flags do not apply).
/// </summary>
[Authorize(Policy = BackendConfigurationClaims.AccessBackendConfigurationPlugin)]
[Route("api/backend-configuration-pn/chemicals")]
public class ChemicalsController(
    IChemicalInventoryService inventory,
    IUserService userService,
    IBackendConfigurationLocalizationService localizationService) : Controller
{
    private ChemicalCaller Caller => ChemicalCaller.Web(userService.UserId);

    [HttpGet("properties/{propertyId:int}/inventory")]
    public Task<OperationDataResult<ChemicalInventoryModel>> GetPropertyInventory(int propertyId) =>
        ExecuteAsync(() => inventory.GetPropertyInventoryAsync(Caller, propertyId), "ErrorWhileReadingChemicals");

    [HttpGet("register/search")]
    public Task<OperationDataResult<ChemicalRegisterPageModel>> SearchRegister(
        [FromQuery] string query, [FromQuery] int page = 0, [FromQuery] int pageSize = 25) =>
        ExecuteAsync(() => inventory.SearchRegisterAsync(Caller, query, page, pageSize), "ErrorWhileSearchingChemicalRegister");

    [HttpGet("register/barcode/{barcode}")]
    public Task<OperationDataResult<List<ChemicalRegisterEntryModel>>> LookupBarcode(string barcode) =>
        ExecuteAsync(async () => (await inventory.LookupBarcodeAsync(Caller, barcode).ConfigureAwait(false)).ToList(),
            "ErrorWhileSearchingChemicalRegister");

    [HttpGet("sds/{fileName}")]
    public Task<IActionResult> GetSds(string fileName) =>
        FileAsync(async () => (await inventory.GetSdsPdfAsync(Caller, fileName).ConfigureAwait(false), "application/pdf"));

    [HttpPost("placements")]
    public Task<OperationDataResult<ChemicalPlacementChangeModel>> RegisterPlacement([FromBody] ChemicalRegisterPlacementCommand command) =>
        ExecuteAsync(() => inventory.RegisterPlacementAsync(Caller, command), "ErrorWhileSavingChemicalPlacement");

    [HttpPost("placements/{placementId:int}/move")]
    public Task<OperationDataResult<ChemicalPlacementChangeModel>> MovePlacement(int placementId, [FromBody] ChemicalMovePlacementBody body) =>
        ExecuteAsync(() => inventory.MovePlacementAsync(Caller,
            new ChemicalMovePlacementCommand(placementId, body.TargetLocationId, body.TargetPlacementNote, body.Amount)),
            "ErrorWhileSavingChemicalPlacement");

    [HttpPost("placements/{placementId:int}/remove")]
    public Task<OperationDataResult<ChemicalPlacementChangeModel>> RemovePlacement(int placementId, [FromBody] ChemicalRemovePlacementBody body) =>
        ExecuteAsync(() => inventory.RemovePlacementAsync(Caller,
            new ChemicalRemovePlacementCommand(placementId, body.Reason, body.RemovedAt, body.Note)),
            "ErrorWhileSavingChemicalPlacement");

    [HttpPut("placements/{placementId:int}/note")]
    public Task<OperationDataResult<ChemicalPlacementChangeModel>> UpdatePlacementNote(int placementId, [FromBody] ChemicalPlacementNoteBody body) =>
        ExecuteAsync(() => inventory.UpdatePlacementNoteAsync(Caller, placementId, body.PlacementNote), "ErrorWhileSavingChemicalPlacement");

    [HttpPost("placements/{placementId:int}/stock-entries")]
    public Task<OperationDataResult<ChemicalPlacementChangeModel>> AddStockEntry(int placementId, [FromBody] ChemicalStockEntryBody body) =>
        ExecuteAsync(() => inventory.AddStockEntryAsync(Caller, new ChemicalAddStockEntryCommand(placementId, body.Kind, body.Amount)),
            "ErrorWhileSavingChemicalStock");

    [HttpPost("locations")]
    public Task<OperationDataResult<ChemicalLocationModel>> CreateLocation([FromBody] ChemicalCreateLocationCommand command) =>
        ExecuteAsync(() => inventory.CreateLocationAsync(Caller, command), "ErrorWhileSavingChemicalLocation");

    [HttpPut("locations/{locationId:int}")]
    public Task<OperationDataResult<ChemicalLocationModel>> UpdateLocation(int locationId, [FromBody] ChemicalUpdateLocationBody body) =>
        ExecuteAsync(() => inventory.UpdateLocationAsync(Caller,
            new ChemicalUpdateLocationCommand(locationId, body.Name, body.Description, body.SortOrder)),
            "ErrorWhileSavingChemicalLocation");

    [HttpPost("locations/{locationId:int}/archive")]
    public Task<OperationDataResult<ChemicalLocationModel>> ArchiveLocation(int locationId) =>
        ExecuteAsync(() => inventory.ArchiveLocationAsync(Caller, locationId), "ErrorWhileSavingChemicalLocation");

    [HttpPut("properties/{propertyId:int}/locations/order")]
    public Task<OperationDataResult<List<ChemicalLocationModel>>> ReorderLocations(int propertyId, [FromBody] List<int> orderedLocationIds) =>
        ExecuteAsync(async () => (await inventory.ReorderLocationsAsync(Caller, propertyId, orderedLocationIds).ConfigureAwait(false)).ToList(),
            "ErrorWhileSavingChemicalLocation");

    /// <summary>
    /// Refuses an empty or oversize file, or a location the caller may not manage,
    /// before the bytes are read (as ChemicalsGrpcService.UploadLocationPhoto does);
    /// SaveLocationPhotoAsync checks again.
    /// </summary>
    [HttpPost("locations/{locationId:int}/photo")]
    public Task<OperationDataResult<ChemicalLocationModel>> UploadLocationPhoto(int locationId, IFormFile file) =>
        ExecuteAsync(async () =>
        {
            if (file == null || file.Length == 0)
            {
                throw new ArgumentException("A non-empty file must be uploaded.", nameof(file));
            }

            if (file.Length > ChemicalInventoryService.MaxPhotoBytes)
            {
                throw new ArgumentException($"The photo exceeds {ChemicalInventoryService.MaxPhotoBytes / (1024 * 1024)} MB.", nameof(file));
            }

            await inventory.RequireCanManageLocationAsync(Caller, locationId).ConfigureAwait(false);

            await using var stream = new MemoryStream();
            await file.CopyToAsync(stream).ConfigureAwait(false);
            return await inventory.SaveLocationPhotoAsync(Caller, locationId, stream.ToArray(), file.ContentType).ConfigureAwait(false);
        }, "ErrorWhileSavingChemicalLocation");

    [HttpGet("locations/{locationId:int}/photo")]
    public Task<IActionResult> GetLocationPhoto(int locationId) =>
        FileAsync(() => inventory.GetLocationPhotoAsync(Caller, locationId));

    [HttpGet("properties/{propertyId:int}/permissions")]
    public Task<OperationDataResult<List<ChemicalWorkerPermissionModel>>> GetPermissions(int propertyId) =>
        ExecuteAsync(async () => (await inventory.ListWorkerPermissionsAsync(Caller, propertyId).ConfigureAwait(false)).ToList(),
            "ErrorWhileReadingChemicalPermissions");

    [HttpPut("properties/{propertyId:int}/permissions")]
    public Task<OperationDataResult<List<ChemicalWorkerPermissionModel>>> SavePermissions(int propertyId,
        [FromBody] List<ChemicalSetWorkerPermissionCommand> changes) =>
        ExecuteAsync(async () => (await inventory.SetWorkerPermissionsAsync(Caller, propertyId, changes).ConfigureAwait(false)).ToList(),
            "ErrorWhileSavingChemicalPermissions");

    [HttpGet("properties/{propertyId:int}/settings")]
    public Task<OperationDataResult<ChemicalSettingsModel>> GetSettings(int propertyId) =>
        ExecuteAsync(() => inventory.GetSettingsAsync(Caller, propertyId), "ErrorWhileReadingChemicalSettings");

    [HttpPut("properties/{propertyId:int}/settings")]
    public Task<OperationDataResult<ChemicalSettingsModel>> SaveSettings(int propertyId, [FromBody] ChemicalSettingsBody body) =>
        ExecuteAsync(() => inventory.SetSettingsAsync(Caller,
            new ChemicalSetSettingsCommand(propertyId, body.StockEnabled, body.DigestRecipients ?? [])),
            "ErrorWhileSavingChemicalSettings");

    private async Task<OperationDataResult<T>> ExecuteAsync<T>(Func<Task<T>> action, string errorKey)
    {
        try
        {
            return new OperationDataResult<T>(true, await action().ConfigureAwait(false));
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            return new OperationDataResult<T>(false, $"{localizationService.GetString(errorKey)}: {e.Message}");
        }
    }

    private async Task<IActionResult> FileAsync(Func<Task<(byte[] Content, string ContentType)>> read)
    {
        try
        {
            var (content, contentType) = await read().ConfigureAwait(false);
            return File(content, contentType);
        }
        catch (ChemicalNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException)
        {
            return BadRequest();
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            return StatusCode(500, e.Message);
        }
    }
}
