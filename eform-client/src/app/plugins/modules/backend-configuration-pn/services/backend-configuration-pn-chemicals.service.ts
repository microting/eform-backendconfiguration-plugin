import {HttpBackend, HttpClient, HttpErrorResponse, HttpResponse} from '@angular/common/http';
import {Injectable, inject} from '@angular/core';
import {Store} from '@ngrx/store';
import {Observable, from, of, throwError} from 'rxjs';
import {catchError, map, switchMap, take} from 'rxjs/operators';
import {OperationDataResult} from 'src/app/common/models';
import {ApiBaseService} from 'src/app/common/services';
import {selectBearerToken} from 'src/app/state/auth/auth.selector';
import {parseContentDispositionFileName} from './backend-configuration-pn-compliance-report.service';
import {
  ChemicalCreateLocationCommand,
  ChemicalDigestModel,
  ChemicalDownload,
  ChemicalDownloadError,
  ChemicalHistoryPageModel,
  ChemicalHistoryQueryModel,
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

/** Statuses whose blob body is the server's reason for refusing the request (ChemicalsController.FileResultAsync). */
const REASON_STATUSES = [400, 409, 422];

/** The reason text of a refused download, or null. The body is a blob (responseType), plain text or a JSON string. */
function toDownloadError(err: unknown): Observable<ChemicalDownloadError> {
  const status = err instanceof HttpErrorResponse ? err.status : 0;
  const body = err instanceof HttpErrorResponse ? err.error : null;
  if (!REASON_STATUSES.includes(status) || !(body instanceof Blob)) {
    return of({status, message: null});
  }
  return from(readText(body)).pipe(
    map((text) => ({status, message: unquote(text.trim()) || null})),
    catchError(() => of({status, message: null})),
  );
}

function readText(blob: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result ?? ''));
    reader.onerror = () => reject(reader.error);
    reader.readAsText(blob);
  });
}

function unquote(text: string): string {
  if (!text.startsWith('"')) {
    return text;
  }
  try {
    const parsed = JSON.parse(text);
    return typeof parsed === 'string' ? parsed.trim() : text;
  } catch {
    return text;
  }
}

/** Plan A Task 15 REST prefix. Every Kemi call lives under it. */
export const BackendConfigurationPnChemicalsMethods = {
  Base: 'api/backend-configuration-pn/chemicals',
};

/**
 * REST client for the Kemi web screens (flutter-chemistry spec §11, W1-W6).
 * Routes mirror ChemicalsController (Plan A Task 15).
 */
@Injectable({providedIn: 'root'})
export class BackendConfigurationPnChemicalsService {
  private apiBaseService = inject(ApiBaseService);
  /**
   * Raw client without interceptors for the file downloads, as
   * BackendConfigurationPnComplianceReportService.export(): the host's error interceptor
   * shows nothing for a failed blob request and retries a 403 as an expired token, and
   * ApiBaseService hides the Content-Disposition header that carries the server's file name.
   * A failure reaches the caller as a ChemicalDownloadError; each export button toasts it (m-2, m-9).
   */
  private rawHttp = new HttpClient(inject(HttpBackend));
  private store = inject(Store);

  private url(path: string): string {
    return `${BackendConfigurationPnChemicalsMethods.Base}/${path}`;
  }

  getPropertyInventory(propertyId: number): Observable<OperationDataResult<ChemicalInventoryModel>> {
    return this.apiBaseService.get(this.url(`properties/${propertyId}/inventory`));
  }

  searchRegister(query: string, page = 0, pageSize = 25): Observable<OperationDataResult<ChemicalRegisterPageModel>> {
    return this.apiBaseService.get(this.url('register/search'), {query, page, pageSize});
  }

  lookupBarcode(barcode: string): Observable<OperationDataResult<ChemicalRegisterEntryModel[]>> {
    return this.apiBaseService.get(this.url(`register/barcode/${encodeURIComponent(barcode)}`));
  }

  getSdsBlob(fileName: string): Observable<Blob> {
    return this.apiBaseService.getBlobData(this.url(`sds/${encodeURIComponent(fileName)}`));
  }

  registerPlacement(command: ChemicalRegisterPlacementCommand): Observable<OperationDataResult<ChemicalPlacementChangeModel>> {
    return this.apiBaseService.post(this.url('placements'), command);
  }

  movePlacement(placementId: number, body: ChemicalMovePlacementBody): Observable<OperationDataResult<ChemicalPlacementChangeModel>> {
    return this.apiBaseService.post(this.url(`placements/${placementId}/move`), body);
  }

  removePlacement(placementId: number, body: ChemicalRemovePlacementBody): Observable<OperationDataResult<ChemicalPlacementChangeModel>> {
    return this.apiBaseService.post(this.url(`placements/${placementId}/remove`), body);
  }

  updatePlacementNote(placementId: number, body: ChemicalPlacementNoteBody): Observable<OperationDataResult<ChemicalPlacementChangeModel>> {
    return this.apiBaseService.put(this.url(`placements/${placementId}/note`), body);
  }

  addStockEntry(placementId: number, body: ChemicalStockEntryBody): Observable<OperationDataResult<ChemicalPlacementChangeModel>> {
    return this.apiBaseService.post(this.url(`placements/${placementId}/stock-entries`), body);
  }

  createLocation(command: ChemicalCreateLocationCommand): Observable<OperationDataResult<ChemicalLocationModel>> {
    return this.apiBaseService.post(this.url('locations'), command);
  }

  updateLocation(locationId: number, body: ChemicalUpdateLocationBody): Observable<OperationDataResult<ChemicalLocationModel>> {
    return this.apiBaseService.put(this.url(`locations/${locationId}`), body);
  }

  archiveLocation(locationId: number): Observable<OperationDataResult<ChemicalLocationModel>> {
    return this.apiBaseService.post(this.url(`locations/${locationId}/archive`), {});
  }

  reorderLocations(propertyId: number, orderedLocationIds: number[]): Observable<OperationDataResult<ChemicalLocationModel[]>> {
    return this.apiBaseService.put(this.url(`properties/${propertyId}/locations/order`), orderedLocationIds);
  }

  uploadLocationPhoto(locationId: number, file: File): Observable<OperationDataResult<ChemicalLocationModel>> {
    return this.apiBaseService.postFormData(this.url(`locations/${locationId}/photo`), {file});
  }

  /** URL for `<img [src]="url | authImage | async">` (the pipe adds the bearer token). */
  locationPhotoUrl(locationId: number): string {
    return `/${this.url(`locations/${locationId}/photo`)}`;
  }

  getPermissions(propertyId: number): Observable<OperationDataResult<ChemicalWorkerPermissionModel[]>> {
    return this.apiBaseService.get(this.url(`properties/${propertyId}/permissions`));
  }

  /** Only the changed workers (W4 save bar); the answer is the full list after save. */
  savePermissions(propertyId: number, changes: ChemicalSetWorkerPermissionCommand[]): Observable<OperationDataResult<ChemicalWorkerPermissionModel[]>> {
    return this.apiBaseService.put(this.url(`properties/${propertyId}/permissions`), changes);
  }

  getSettings(propertyId: number): Observable<OperationDataResult<ChemicalSettingsModel>> {
    return this.apiBaseService.get(this.url(`properties/${propertyId}/settings`));
  }

  saveSettings(propertyId: number, body: ChemicalSettingsBody): Observable<OperationDataResult<ChemicalSettingsModel>> {
    return this.apiBaseService.put(this.url(`properties/${propertyId}/settings`), body);
  }

  /** The next weekly e-mail for one property, built by the server from the digest job's rule. */
  getDigestPreview(propertyId: number): Observable<OperationDataResult<ChemicalDigestModel>> {
    return this.apiBaseService.get(this.url(`properties/${propertyId}/digest-preview`));
  }

  /** W6: one server page of the property's history. A refused query (bad period) is 200 + success=false. */
  getHistory(propertyId: number, query: ChemicalHistoryQueryModel): Observable<OperationDataResult<ChemicalHistoryPageModel>> {
    return this.apiBaseService.post(this.url(`properties/${propertyId}/history`), query);
  }

  exportAuthorityList(propertyId: number): Observable<ChemicalDownload> {
    return this.download('GET', `properties/${propertyId}/exports/authority-list`, null, 'chemicals.pdf');
  }

  /** placementIds = the rows the page shows (Plan E decision 4); null = every open placement. */
  exportInventory(propertyId: number, placementIds: number[] | null): Observable<ChemicalDownload> {
    return this.download('POST', `properties/${propertyId}/exports/inventory`, {placementIds}, 'chemicals.xlsx');
  }

  exportRemovedPlacements(propertyId: number): Observable<ChemicalDownload> {
    return this.download('GET', `properties/${propertyId}/exports/removed-placements`, null, 'removed-placements.xlsx');
  }

  exportHistory(propertyId: number, query: ChemicalHistoryQueryModel): Observable<ChemicalDownload> {
    return this.download('POST', `properties/${propertyId}/history/export`, query, 'chemical-history.xlsx');
  }

  private download(method: 'GET' | 'POST', path: string, body: unknown, fallbackFileName: string): Observable<ChemicalDownload> {
    return this.store.select(selectBearerToken).pipe(
      take(1),
      switchMap((token) => this.rawHttp.request(method, this.url(path), {
        body,
        observe: 'response',
        responseType: 'blob',
        headers: token ? {Authorization: `Bearer ${token}`} : {},
      })),
      map((res: HttpResponse<Blob>) => ({
        blob: res.body ?? new Blob(),
        fileName: parseContentDispositionFileName(res.headers.get('Content-Disposition'), fallbackFileName),
      })),
      catchError((err: unknown) => toDownloadError(err).pipe(switchMap((error) => throwError(() => error)))),
    );
  }
}
