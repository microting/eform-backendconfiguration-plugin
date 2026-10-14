import {Injectable, inject} from '@angular/core';
import {Observable} from 'rxjs';
import {OperationDataResult} from 'src/app/common/models';
import {ApiBaseService} from 'src/app/common/services';
import {
  ChemicalCreateLocationCommand,
  ChemicalInventoryModel,
  ChemicalLocationModel,
  ChemicalMovePlacementBody,
  ChemicalPlacementChangeModel,
  ChemicalPlacementNoteBody,
  ChemicalRegisterEntryModel,
  ChemicalRegisterPageModel,
  ChemicalRegisterPlacementCommand,
  ChemicalRemovePlacementBody,
  ChemicalSetWorkerPermissionCommand,
  ChemicalSettingsBody,
  ChemicalSettingsModel,
  ChemicalStockEntryBody,
  ChemicalUpdateLocationBody,
  ChemicalWorkerPermissionModel,
} from '../models';

/** Plan A Task 15 REST prefix. Every Kemi call lives under it. */
export const BACKEND_CONFIGURATION_PN_CHEMICALS_BASE = 'api/backend-configuration-pn/chemicals';
const BASE = BACKEND_CONFIGURATION_PN_CHEMICALS_BASE;

/**
 * REST client for the Kemi web screens (flutter-chemistry spec §11, W1-W6).
 * Routes mirror ChemicalsController (Plan A Task 15) and, from PR E2/E3 on,
 * ChemicalDigestController / ChemicalReportsController (Plan E).
 */
@Injectable({providedIn: 'root'})
export class BackendConfigurationPnChemicalsService {
  private apiBaseService = inject(ApiBaseService);

  getPropertyInventory(propertyId: number): Observable<OperationDataResult<ChemicalInventoryModel>> {
    return this.apiBaseService.get(`${BASE}/properties/${propertyId}/inventory`);
  }

  searchRegister(query: string, page = 0, pageSize = 25): Observable<OperationDataResult<ChemicalRegisterPageModel>> {
    return this.apiBaseService.get(`${BASE}/register/search`, {query, page, pageSize});
  }

  lookupBarcode(barcode: string): Observable<OperationDataResult<ChemicalRegisterEntryModel[]>> {
    return this.apiBaseService.get(`${BASE}/register/barcode/${encodeURIComponent(barcode)}`);
  }

  getSdsBlob(fileName: string): Observable<Blob> {
    return this.apiBaseService.getBlobData(`${BASE}/sds/${encodeURIComponent(fileName)}`);
  }

  registerPlacement(command: ChemicalRegisterPlacementCommand): Observable<OperationDataResult<ChemicalPlacementChangeModel>> {
    return this.apiBaseService.post(`${BASE}/placements`, command);
  }

  movePlacement(placementId: number, body: ChemicalMovePlacementBody): Observable<OperationDataResult<ChemicalPlacementChangeModel>> {
    return this.apiBaseService.post(`${BASE}/placements/${placementId}/move`, body);
  }

  removePlacement(placementId: number, body: ChemicalRemovePlacementBody): Observable<OperationDataResult<ChemicalPlacementChangeModel>> {
    return this.apiBaseService.post(`${BASE}/placements/${placementId}/remove`, body);
  }

  updatePlacementNote(placementId: number, body: ChemicalPlacementNoteBody): Observable<OperationDataResult<ChemicalPlacementChangeModel>> {
    return this.apiBaseService.put(`${BASE}/placements/${placementId}/note`, body);
  }

  addStockEntry(placementId: number, body: ChemicalStockEntryBody): Observable<OperationDataResult<ChemicalPlacementChangeModel>> {
    return this.apiBaseService.post(`${BASE}/placements/${placementId}/stock-entries`, body);
  }

  createLocation(command: ChemicalCreateLocationCommand): Observable<OperationDataResult<ChemicalLocationModel>> {
    return this.apiBaseService.post(`${BASE}/locations`, command);
  }

  updateLocation(locationId: number, body: ChemicalUpdateLocationBody): Observable<OperationDataResult<ChemicalLocationModel>> {
    return this.apiBaseService.put(`${BASE}/locations/${locationId}`, body);
  }

  archiveLocation(locationId: number): Observable<OperationDataResult<ChemicalLocationModel>> {
    return this.apiBaseService.post(`${BASE}/locations/${locationId}/archive`, {});
  }

  reorderLocations(propertyId: number, orderedLocationIds: number[]): Observable<OperationDataResult<ChemicalLocationModel[]>> {
    return this.apiBaseService.put(`${BASE}/properties/${propertyId}/locations/order`, orderedLocationIds);
  }

  uploadLocationPhoto(locationId: number, file: File): Observable<OperationDataResult<ChemicalLocationModel>> {
    return this.apiBaseService.postFormData(`${BASE}/locations/${locationId}/photo`, {file});
  }

  /** URL for `<img [src]="url | authImage | async">` (the pipe adds the bearer token). */
  locationPhotoUrl(locationId: number): string {
    return `/${BASE}/locations/${locationId}/photo`;
  }

  getPermissions(propertyId: number): Observable<OperationDataResult<ChemicalWorkerPermissionModel[]>> {
    return this.apiBaseService.get(`${BASE}/properties/${propertyId}/permissions`);
  }

  /** Only the changed workers (W4 save bar); the answer is the full list after save. */
  savePermissions(propertyId: number, changes: ChemicalSetWorkerPermissionCommand[]): Observable<OperationDataResult<ChemicalWorkerPermissionModel[]>> {
    return this.apiBaseService.put(`${BASE}/properties/${propertyId}/permissions`, changes);
  }

  getSettings(propertyId: number): Observable<OperationDataResult<ChemicalSettingsModel>> {
    return this.apiBaseService.get(`${BASE}/properties/${propertyId}/settings`);
  }

  saveSettings(propertyId: number, body: ChemicalSettingsBody): Observable<OperationDataResult<ChemicalSettingsModel>> {
    return this.apiBaseService.put(`${BASE}/properties/${propertyId}/settings`, body);
  }
}
