import {ApiBaseService} from 'src/app/common/services';
import {Injectable} from '@angular/core';
import {OperationDataResult, OperationResult, Paged} from 'src/app/common/models';
import {Observable} from 'rxjs';
import {DocumentModel, DocumentFolderModel, DocumentSimpleFolderModel, DocumentSimpleModel} from '../models';

export let BackendConfigurationPnDocumentsMethods = {
  Documents: 'api/backend-configuration-pn/documents',
  DocumentCreate: 'api/backend-configuration-pn/documents/create',
  DocumentUpdate: 'api/backend-configuration-pn/documents/update',
  DocumentDelete: 'api/backend-configuration-pn/documents/delete',
  Folders: 'api/backend-configuration-pn/documents/folders',
  FolderCreate: 'api/backend-configuration-pn/documents/folders/create',
  FolderUpdate: 'api/backend-configuration-pn/documents/folders/update',
  FolderDelete: 'api/backend-configuration-pn/documents/folders/delete',
}

@Injectable({
  providedIn: 'root',
})

export class BackendConfigurationPnDocumentsService {
  constructor(private apiBaseService: ApiBaseService) {}

  getAllDocuments(model: { documentId?: string; expiration?: string; propertyId: number; folderId?: string }):
    Observable<OperationDataResult<Paged<DocumentModel>>> {
    return this.apiBaseService.post(BackendConfigurationPnDocumentsMethods.Documents, model);
  }

  getSingleDocument(documentId: number): Observable<OperationDataResult<DocumentModel>> {
    return this.apiBaseService.get(BackendConfigurationPnDocumentsMethods.Documents + '/' + documentId);
  }

  getSimpleDocuments(languageId: number, propertyId: number): Observable<OperationDataResult<DocumentSimpleModel[]>> {
    return this.apiBaseService.get(BackendConfigurationPnDocumentsMethods.Documents + '?languageId=' + languageId + '&propertyId=' + propertyId, {});
  }

  updateDocument(model: DocumentModel): Observable<OperationResult> {
    return this.apiBaseService.putFormData(BackendConfigurationPnDocumentsMethods.DocumentUpdate + '/' + model.id, model);
  }

  createDocument(model: DocumentModel): Observable<OperationResult> {
    return this.apiBaseService.postFormData(BackendConfigurationPnDocumentsMethods.DocumentCreate, model);
  }

  deleteDocument(documentId: number): Observable<OperationResult> {
    return this.apiBaseService.delete(BackendConfigurationPnDocumentsMethods.DocumentDelete + '/' + documentId);
  }

  getAllFolders(model: { documentId?: string; expiration?: string; propertyId: number; folderId?: string }):
    Observable<OperationDataResult<Paged<DocumentFolderModel>>> {
    return this.apiBaseService.post(BackendConfigurationPnDocumentsMethods.Folders, model);
  }

  getSimpleFolders(languageId: number): Observable<OperationDataResult<DocumentSimpleFolderModel[]>> {
    return this.apiBaseService.get(BackendConfigurationPnDocumentsMethods.Folders + '?languageId=' + languageId);
  }

  getSingleFolder(folderId: number): Observable<OperationDataResult<DocumentFolderModel>> {
    return this.apiBaseService.get(BackendConfigurationPnDocumentsMethods.Folders + '/' + folderId);
  }

  updateFolder(model: DocumentFolderModel): Observable<OperationResult> {
    return this.apiBaseService.put(BackendConfigurationPnDocumentsMethods.FolderUpdate + '/' + model.id, model);
  }

  createFolder(model: DocumentFolderModel): Observable<OperationResult> {
    return this.apiBaseService.post(BackendConfigurationPnDocumentsMethods.FolderCreate, model);
  }

  deleteFolder(folderId: number): Observable<OperationResult> {
    return this.apiBaseService.delete(BackendConfigurationPnDocumentsMethods.FolderDelete + '/' + folderId);
  }

}
