import {Injectable} from '@angular/core';
import {Observable} from 'rxjs';
import {OperationDataResult, OperationResult} from 'src/app/common/models';
import {ApiBaseService} from 'src/app/common/services';
import {
  AccessChangeModel,
  ArchiveCapabilitiesModel,
  ArchiveFolderAccessModel,
  ArchiveFolderCreateModel,
  ArchiveFolderModel,
  ArchiveFolderUpdateModel,
} from '../models';
import {ArchiveAccountDisabledHandler} from './archive-account-disabled.handler';

export const BackendConfigurationPnArchiveFoldersMethods = {
  ArchiveFolders: 'api/backend-configuration-pn/archive-folders',
  Capabilities: 'api/backend-configuration-pn/archive/capabilities',
};

/**
 * The tenant-wide archive folder tree (max 2 levels). Everything but GET is admin/Leder-only on the server ([ArchiveManager]).
 * A refusal (folder not empty, depth limit) is success=false and is toasted by ApiBaseService.
 * "May complete empty (404)": see ArchiveAccountDisabledHandler.
 */
@Injectable({
  providedIn: 'root',
})
export class BackendConfigurationPnArchiveFoldersService {
  constructor(private apiBaseService: ApiBaseService, private accountDisabled: ArchiveAccountDisabledHandler) {}

  /** `propertyIds` scope the counts only; the tree is the same for every property (D16). */
  getFolders(propertyIds: number[]): Observable<OperationDataResult<ArchiveFolderModel[]>> {
    const base = BackendConfigurationPnArchiveFoldersMethods.ArchiveFolders;
    // ApiBaseService.get cannot repeat a key, and the API binds a repeated `propertyIds`.
    const query = propertyIds.map(id => `propertyIds=${id}`).join('&');
    return this.apiBaseService
      .get<ArchiveFolderModel[]>(query ? `${base}?${query}` : base)
      .pipe(this.accountDisabled.handle());
  }

  /**
   * What the caller may do in Arkiv (admin or Leder). Not cached by the server. Do NOT rely on an empty completion
   * or an error for an older plugin without the endpoint: against the host a 404 (or the SPA's HTML 200) is retried
   * 5 x 15 s and then neither emits nor completes. ArchiveCapabilitiesService bounds the wait (timeout + role fallback).
   * `handle(false)`: like the app, the capabilities call never announces its own 403 on forbidden$ (that stream makes
   * ArchiveCapabilitiesService reload this very call).
   */
  getCapabilities(): Observable<OperationDataResult<ArchiveCapabilitiesModel>> {
    return this.apiBaseService
      .get<ArchiveCapabilitiesModel>(BackendConfigurationPnArchiveFoldersMethods.Capabilities)
      .pipe(this.accountDisabled.handle(false));
  }

  createFolder(model: ArchiveFolderCreateModel): Observable<OperationDataResult<ArchiveFolderModel>> {
    return this.apiBaseService
      .post<ArchiveFolderModel>(BackendConfigurationPnArchiveFoldersMethods.ArchiveFolders, model)
      .pipe(this.accountDisabled.handle());
  }

  /** Rename and/or move. May complete empty (404). */
  updateFolder(id: number, model: ArchiveFolderUpdateModel): Observable<OperationDataResult<AccessChangeModel>> {
    return this.apiBaseService
      .put<AccessChangeModel>(`${BackendConfigurationPnArchiveFoldersMethods.ArchiveFolders}/${id}`, model)
      .pipe(this.accountDisabled.handle());
  }

  /** Only empty folders. May complete empty (404). */
  deleteFolder(id: number): Observable<OperationResult> {
    return this.apiBaseService
      .delete(`${BackendConfigurationPnArchiveFoldersMethods.ArchiveFolders}/${id}`)
      .pipe(this.accountDisabled.handle());
  }

  /** Empty `tagIds` = open. May complete empty (404). */
  setFolderAccess(id: number, model: ArchiveFolderAccessModel): Observable<OperationDataResult<AccessChangeModel>> {
    return this.apiBaseService
      .put<AccessChangeModel>(`${BackendConfigurationPnArchiveFoldersMethods.ArchiveFolders}/${id}/access`, model)
      .pipe(this.accountDisabled.handle());
  }
}
