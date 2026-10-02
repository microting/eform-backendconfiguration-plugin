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
/// Typed service outcomes (<see cref="ExpectedStatusCode"/>) are answers, not
/// errors: they reach the client with their message and never go to Sentry.
/// </summary>
[Authorize(Policy = BackendConfigurationClaims.AccessBackendConfigurationPlugin)]
[ChemistryFirstUserOnly] // TODO(chemistry-GA): remove with ChemistryFirstUserOnlyAttribute.
[Route("api/backend-configuration-pn/chemicals")]
public class ChemicalsController(
    IChemicalInventoryService inventory,
    IUserService userService,
    IBackendConfigurationLocalizationService localizationService) : Controller
{
    /// <summary>
    /// The photo limit plus 1 MB for the multipart envelope. The host lifts Kestrel's
    /// limit to 100 MB and the multipart limit to long.MaxValue, so the route sets its own.
    /// </summary>
    private const long MaxPhotoRequestBytes = ChemicalInventoryService.MaxPhotoBytes + 1024 * 1024;

    private const string BodyRequired = "A request body is required.";

    private const string ReadingChemicals = "ErrorWhileReadingChemicals";
    private const string SearchingRegister = "ErrorWhileSearchingChemicalRegister";
    private const string SavingPlacement = "ErrorWhileSavingChemicalPlacement";
    private const string SavingStock = "ErrorWhileSavingChemicalStock";
    private const string SavingLocation = "ErrorWhileSavingChemicalLocation";
    private const string ReadingPermissions = "ErrorWhileReadingChemicalPermissions";
    private const string SavingPermissions = "ErrorWhileSavingChemicalPermissions";
    private const string ReadingSettings = "ErrorWhileReadingChemicalSettings";
    private const string SavingSettings = "ErrorWhileSavingChemicalSettings";

    private ChemicalCaller Caller => ChemicalCaller.Web(userService.UserId);

    [HttpGet("properties/{propertyId:int}/inventory")]
    public Task<OperationDataResult<ChemicalInventoryModel>> GetPropertyInventory(int propertyId) =>
        ExecuteAsync(() => inventory.GetPropertyInventoryAsync(Caller, propertyId), ReadingChemicals);

    [HttpGet("register/search")]
    public Task<OperationDataResult<ChemicalRegisterPageModel>> SearchRegister(
        [FromQuery] string query, [FromQuery] int page = 0, [FromQuery] int pageSize = 25) =>
        ExecuteAsync(() => inventory.SearchRegisterAsync(Caller, query, page, pageSize), SearchingRegister);

    [HttpGet("register/barcode/{barcode}")]
    public Task<OperationDataResult<List<ChemicalRegisterEntryModel>>> LookupBarcode(string barcode) =>
        ExecuteListAsync(() => inventory.LookupBarcodeAsync(Caller, barcode), SearchingRegister);

    [HttpGet("sds/{fileName}")]
    public Task<IActionResult> GetSds(string fileName) =>
        FileAsync(async () => (await inventory.GetSdsPdfAsync(Caller, fileName).ConfigureAwait(false), "application/pdf"));

    [HttpPost("placements")]
    public Task<OperationDataResult<ChemicalPlacementChangeModel>> RegisterPlacement([FromBody] ChemicalRegisterPlacementCommand command) =>
        ExecuteAsync(() => inventory.RegisterPlacementAsync(Caller, Required(command)), SavingPlacement);

    [HttpPost("placements/{placementId:int}/move")]
    public Task<OperationDataResult<ChemicalPlacementChangeModel>> MovePlacement(int placementId, [FromBody] ChemicalMovePlacementBody body) =>
        ExecuteAsync(() => inventory.MovePlacementAsync(Caller, Required(body).ToCommand(placementId)), SavingPlacement);

    [HttpPost("placements/{placementId:int}/remove")]
    public Task<OperationDataResult<ChemicalPlacementChangeModel>> RemovePlacement(int placementId, [FromBody] ChemicalRemovePlacementBody body) =>
        ExecuteAsync(() => inventory.RemovePlacementAsync(Caller, Required(body).ToCommand(placementId)), SavingPlacement);

    [HttpPut("placements/{placementId:int}/note")]
    public Task<OperationDataResult<ChemicalPlacementChangeModel>> UpdatePlacementNote(int placementId, [FromBody] ChemicalPlacementNoteBody body) =>
        ExecuteAsync(() => inventory.UpdatePlacementNoteAsync(Caller, placementId, Required(body).PlacementNote), SavingPlacement);

    [HttpPost("placements/{placementId:int}/stock-entries")]
    public Task<OperationDataResult<ChemicalPlacementChangeModel>> AddStockEntry(int placementId, [FromBody] ChemicalStockEntryBody body) =>
        ExecuteAsync(() => inventory.AddStockEntryAsync(Caller, Required(body).ToCommand(placementId)), SavingStock);

    [HttpPost("locations")]
    public Task<OperationDataResult<ChemicalLocationModel>> CreateLocation([FromBody] ChemicalCreateLocationCommand command) =>
        ExecuteAsync(() => inventory.CreateLocationAsync(Caller, Required(command)), SavingLocation);

    [HttpPut("locations/{locationId:int}")]
    public Task<OperationDataResult<ChemicalLocationModel>> UpdateLocation(int locationId, [FromBody] ChemicalUpdateLocationBody body) =>
        ExecuteAsync(() => inventory.UpdateLocationAsync(Caller, Required(body).ToCommand(locationId)), SavingLocation);

    [HttpPost("locations/{locationId:int}/archive")]
    public Task<OperationDataResult<ChemicalLocationModel>> ArchiveLocation(int locationId) =>
        ExecuteAsync(() => inventory.ArchiveLocationAsync(Caller, locationId), SavingLocation);

    [HttpPut("properties/{propertyId:int}/locations/order")]
    public Task<OperationDataResult<List<ChemicalLocationModel>>> ReorderLocations(int propertyId, [FromBody] List<int> orderedLocationIds) =>
        ExecuteListAsync(() => inventory.ReorderLocationsAsync(Caller, propertyId, Required(orderedLocationIds)), SavingLocation);

    /// <summary>
    /// The request is capped at the photo limit plus the multipart envelope. An empty or
    /// oversize file, or a location the caller may not manage, is refused before the file
    /// is copied (as ChemicalsGrpcService.UploadLocationPhoto does); SaveLocationPhotoAsync
    /// checks again.
    /// </summary>
    [HttpPost("locations/{locationId:int}/photo")]
    [RequestSizeLimit(MaxPhotoRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxPhotoRequestBytes)]
    public Task<OperationDataResult<ChemicalLocationModel>> UploadLocationPhoto(int locationId, IFormFile file) =>
        ExecuteAsync(async () =>
        {
            ChemicalInventoryService.EnsurePhotoSize(file?.Length ?? 0);
            await inventory.RequireCanManageLocationAsync(Caller, locationId).ConfigureAwait(false);

            await using var stream = new MemoryStream();
            await file!.CopyToAsync(stream).ConfigureAwait(false);
            return await inventory.SaveLocationPhotoAsync(Caller, locationId, stream.ToArray(), file.ContentType).ConfigureAwait(false);
        }, SavingLocation);

    [HttpGet("locations/{locationId:int}/photo")]
    public Task<IActionResult> GetLocationPhoto(int locationId) =>
        FileAsync(() => inventory.GetLocationPhotoAsync(Caller, locationId));

    [HttpGet("properties/{propertyId:int}/permissions")]
    public Task<OperationDataResult<List<ChemicalWorkerPermissionModel>>> GetPermissions(int propertyId) =>
        ExecuteListAsync(() => inventory.ListWorkerPermissionsAsync(Caller, propertyId), ReadingPermissions);

    [HttpPut("properties/{propertyId:int}/permissions")]
    public Task<OperationDataResult<List<ChemicalWorkerPermissionModel>>> SavePermissions(int propertyId,
        [FromBody] List<ChemicalSetWorkerPermissionCommand> changes) =>
        ExecuteListAsync(() => inventory.SetWorkerPermissionsAsync(Caller, propertyId, Required(changes)), SavingPermissions);

    [HttpGet("properties/{propertyId:int}/settings")]
    public Task<OperationDataResult<ChemicalSettingsModel>> GetSettings(int propertyId) =>
        ExecuteAsync(() => inventory.GetSettingsAsync(Caller, propertyId), ReadingSettings);

    [HttpPut("properties/{propertyId:int}/settings")]
    public Task<OperationDataResult<ChemicalSettingsModel>> SaveSettings(int propertyId, [FromBody] ChemicalSettingsBody body) =>
        ExecuteAsync(() => inventory.SetSettingsAsync(Caller, Required(body).ToCommand(propertyId)), SavingSettings);

    /// <summary>
    /// The HTTP status of a typed service outcome, the same set ChemicalsGrpcService.RunAsync
    /// maps to gRPC codes; null for anything unexpected (a bug, reported to Sentry).
    /// </summary>
    internal static int? ExpectedStatusCode(Exception e) => e switch
    {
        ChemicalNotFoundException => StatusCodes.Status404NotFound,
        ChemicalPermissionDeniedException => StatusCodes.Status403Forbidden,
        ChemicalPreconditionException or ChemicalConflictException => StatusCodes.Status409Conflict,
        ChemicalUnavailableException => StatusCodes.Status503ServiceUnavailable,
        ArgumentException => StatusCodes.Status400BadRequest,
        _ => null,
    };

    /// <summary>A JSON body that is missing or does not parse binds to null (no [ApiController]).</summary>
    private static TBody Required<TBody>(TBody body) where TBody : class =>
        body ?? throw new ArgumentException(BodyRequired);

    private async Task<OperationDataResult<T>> ExecuteAsync<T>(Func<Task<T>> action, string errorKey)
    {
        try
        {
            return new OperationDataResult<T>(true, await action().ConfigureAwait(false));
        }
        catch (Exception e)
        {
            if (ExpectedStatusCode(e) is null)
            {
                SentrySdk.CaptureException(e);
            }

            return new OperationDataResult<T>(false, $"{localizationService.GetString(errorKey)}: {e.Message}");
        }
    }

    private Task<OperationDataResult<List<T>>> ExecuteListAsync<T>(Func<Task<IReadOnlyList<T>>> action, string errorKey) =>
        ExecuteAsync(async () => (await action().ConfigureAwait(false)).ToList(), errorKey);

    private async Task<IActionResult> FileAsync(Func<Task<(byte[] Content, string ContentType)>> read)
    {
        try
        {
            var (content, contentType) = await read().ConfigureAwait(false);
            return File(content, contentType);
        }
        catch (Exception e)
        {
            switch (ExpectedStatusCode(e))
            {
                case StatusCodes.Status404NotFound:
                    return NotFound();
                case { } status:
                    return StatusCode(status, e.Message);
                default:
                    SentrySdk.CaptureException(e);
                    return StatusCode(StatusCodes.Status500InternalServerError, e.Message);
            }
        }
    }
}
