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

namespace BackendConfiguration.Pn.Services.ChemicalInventoryService;

using System.Collections.Generic;
using System.Threading.Tasks;
using Infrastructure.Models.Chemicals;

/// <summary>
/// Every chemical inventory rule (flutter-chemistry spec §4). ChemicalsGrpcService
/// (app) and ChemicalsController (web) are thin adapters over it. Every method
/// checks the caller's permission for the property it touches.
/// </summary>
public interface IChemicalInventoryService
{
    // ---- locations (ManageLocations; reading a photo needs View) ----
    Task<ChemicalLocationModel> CreateLocationAsync(ChemicalCaller caller, ChemicalCreateLocationCommand command);
    Task<ChemicalLocationModel> UpdateLocationAsync(ChemicalCaller caller, ChemicalUpdateLocationCommand command);
    Task<ChemicalLocationModel> ArchiveLocationAsync(ChemicalCaller caller, int locationId);
    Task<IReadOnlyList<ChemicalLocationModel>> ReorderLocationsAsync(ChemicalCaller caller, int propertyId, IReadOnlyList<int> orderedLocationIds);
    /// <summary>
    /// The location is active and the caller may manage it. A cheap pre-check so an
    /// upload is refused before its bytes are read; SaveLocationPhotoAsync checks again.
    /// </summary>
    Task RequireCanManageLocationAsync(ChemicalCaller caller, int locationId);
    Task<ChemicalLocationModel> SaveLocationPhotoAsync(ChemicalCaller caller, int locationId, byte[] content, string contentType);
    Task<(byte[] Content, string ContentType)> GetLocationPhotoAsync(ChemicalCaller caller, int locationId);

    // ---- property administration (Admin) ----
    Task<ChemicalSettingsModel> GetSettingsAsync(ChemicalCaller caller, int propertyId);
    Task<ChemicalSettingsModel> SetSettingsAsync(ChemicalCaller caller, ChemicalSetSettingsCommand command);
    Task<IReadOnlyList<ChemicalWorkerPermissionModel>> ListWorkerPermissionsAsync(ChemicalCaller caller, int propertyId);
    Task<IReadOnlyList<ChemicalWorkerPermissionModel>> SetWorkerPermissionsAsync(ChemicalCaller caller, int propertyId, IReadOnlyList<ChemicalSetWorkerPermissionCommand> changes);

    // ---- placements and stock ----
    Task<ChemicalPlacementChangeModel> RegisterPlacementAsync(ChemicalCaller caller, ChemicalRegisterPlacementCommand command);
    Task<ChemicalPlacementChangeModel> MovePlacementAsync(ChemicalCaller caller, ChemicalMovePlacementCommand command);
    Task<ChemicalPlacementChangeModel> RemovePlacementAsync(ChemicalCaller caller, ChemicalRemovePlacementCommand command);
    Task<ChemicalPlacementChangeModel> UpdatePlacementNoteAsync(ChemicalCaller caller, int placementId, string placementNote);
    Task<ChemicalPlacementChangeModel> AddStockEntryAsync(ChemicalCaller caller, ChemicalAddStockEntryCommand command);

    // ---- sync and read ----
    /// <summary>
    /// The app's delta sync (spec §6). <paramref name="since"/> is the previous
    /// SyncToken; empty, garbage or future tokens give a full load.
    /// </summary>
    Task<ChemicalInventoryModel> GetInventoryAsync(ChemicalCaller caller, string since);

    /// <summary>One property in full for the web (View); SyncToken is empty.</summary>
    Task<ChemicalInventoryModel> GetPropertyInventoryAsync(ChemicalCaller caller, int propertyId);

    // ---- register-wide (View on any property) ----
    Task<IReadOnlyList<ChemicalRegisterEntryModel>> LookupBarcodeAsync(ChemicalCaller caller, string barcode);
    Task<ChemicalRegisterPageModel> SearchRegisterAsync(ChemicalCaller caller, string query, int page, int pageSize);

    /// <summary>
    /// The SDS PDF of a file name the register knows, proxied from chemicalbase.
    /// ArgumentException for a malformed name; NotFound for no SDS.
    /// </summary>
    Task<byte[]> GetSdsPdfAsync(ChemicalCaller caller, string fileName);
}
