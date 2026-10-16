import {ApiBaseService} from 'src/app/common/services';
import {Injectable} from '@angular/core';
import {OperationDataResult, OperationResult, Paged} from 'src/app/common/models';
import {Observable} from 'rxjs';
import {
  AccessChangeModel,
  FileDateModel,
  FileModel,
  FilesAccessModel,
  FilesArchiveModel,
  FilesCreateListModel,
  FilesModel,
  FilesMoveModel,
  FilesMoveResultModel,
  FilesRequestModel,
  FilesUpdateFilename,
  FilesUpdateFileTags,
  FilesUpdatePropertiesModel,
} from '../models';
import {ArchiveAccountDisabledHandler} from './archive-account-disabled.handler';

export let BackendConfigurationPnFilesMethods = {
  Files: 'api/backend-configuration-pn/files',
  CreateFiles: 'api/backend-configuration-pn/files/create',
  GetFile: 'api/backend-configuration-pn/files/get-file',
  GetFiles: 'api/backend-configuration-pn/files/get-files',
  Folder: 'api/backend-configuration-pn/files/folder',
  Access: 'api/backend-configuration-pn/files/access',
  Properties: 'api/backend-configuration-pn/files/properties',
};

/** The archive documents. Every call goes through the account_disabled handler; "May complete empty (404)": see ArchiveAccountDisabledHandler. */
@Injectable({
  providedIn: 'root',
})
export class BackendConfigurationPnFilesService {
  constructor(private apiBaseService: ApiBaseService, private accountDisabled: ArchiveAccountDisabledHandler) {
  }

  getAllFiles(model: FilesRequestModel): Observable<OperationDataResult<Paged<FilesModel>>> {
    return this.apiBaseService.post(BackendConfigurationPnFilesMethods.Files, model).pipe(this.accountDisabled.handle());
  }

  /** May complete empty (404: the document is gone or not readable). */
  getFile(fileId: number): Observable<OperationDataResult<FileModel>> {
    return this.apiBaseService.get(`${BackendConfigurationPnFilesMethods.Files}/${fileId}`).pipe(this.accountDisabled.handle());
  }

  /** May complete empty (404). */
  updateFileName(model: FilesUpdateFilename): Observable<OperationResult> {
    return this.apiBaseService.put(BackendConfigurationPnFilesMethods.Files, model).pipe(this.accountDisabled.handle());
  }

  /** May complete empty (404). */
  updateFileTags(model: FilesUpdateFileTags): Observable<OperationResult> {
    return this.apiBaseService.put(`${BackendConfigurationPnFilesMethods.Files}/tags`, model).pipe(this.accountDisabled.handle());
  }

  /** Replaces the document's properties. May complete empty (404). */
  updateFileProperties(model: FilesUpdatePropertiesModel): Observable<OperationResult> {
    return this.apiBaseService.put(BackendConfigurationPnFilesMethods.Properties, model).pipe(this.accountDisabled.handle());
  }

  /** An unknown or unreadable folder is success=false ("Mappen findes ikke"), not 404. */
  createFiles(model: FilesCreateListModel): Observable<OperationResult> {
    return this.apiBaseService.postFormData(BackendConfigurationPnFilesMethods.CreateFiles, model).pipe(this.accountDisabled.handle());
  }

  /** May complete empty (404). */
  deleteFile(fileId: number): Observable<OperationResult> {
    return this.apiBaseService.delete(`${BackendConfigurationPnFilesMethods.Files}/${fileId}`).pipe(this.accountDisabled.handle());
  }

  /**
   * Bulk move; the result's `accessChanged` drives the D11 message. A move that would change access unconfirmed
   * (D17) is success=false. May complete empty (404: unknown, deleted or unreadable target folder or document).
   */
  moveFiles(model: FilesMoveModel): Observable<OperationDataResult<FilesMoveResultModel>> {
    return this.apiBaseService.put(BackendConfigurationPnFilesMethods.Folder, model).pipe(this.accountDisabled.handle());
  }

  /**
   * A date approves it (DocumentDateState.Approved); null clears it (None). `date` is `yyyy-MM-dd`: for a server
   * date (a Date after the host's DateInterceptor) send `serverDay(value)`. May complete empty (404).
   */
  setDocumentDate(fileId: number, model: FileDateModel): Observable<OperationResult> {
    return this.apiBaseService.put(`${BackendConfigurationPnFilesMethods.Files}/${fileId}/date`, model).pipe(this.accountDisabled.handle());
  }

  /** Admin only. An unknown group is success=false. May complete empty (404). */
  setFilesAccess(model: FilesAccessModel): Observable<OperationDataResult<AccessChangeModel>> {
    return this.apiBaseService.put(BackendConfigurationPnFilesMethods.Access, model).pipe(this.accountDisabled.handle());
  }

  /** Blob response: a 403 body arrives as a Blob, which the account_disabled handler reads. */
  downloadFiles(archiveModel: FilesArchiveModel): Observable<any> {
    return this.apiBaseService.postBlobData(BackendConfigurationPnFilesMethods.GetFiles, archiveModel).pipe(this.accountDisabled.handle());
  }

  /** Blob response (see downloadFiles). May complete empty (404). */
  getPdfFile(id: number) {
    return this.apiBaseService.getBlobData(`${BackendConfigurationPnFilesMethods.GetFile}/${id}`).pipe(this.accountDisabled.handle());
  }
}
