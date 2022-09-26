import {ApiBaseService} from 'src/app/common/services';
import {Injectable} from '@angular/core';
import {OperationDataResult, Paged} from 'src/app/common/models';
import {Observable} from 'rxjs';
import {DocumentModel} from 'src/app/plugins/modules/backend-configuration-pn/models/documents/document.model';
import {DocumentsRequestModel} from 'src/app/plugins/modules/backend-configuration-pn/models/documents/documents-request.model';
import {DocumentFolderRequestModel} from 'src/app/plugins/modules/backend-configuration-pn/models/documents/document-folder-request.model';
import {DocumentFolderModel} from 'src/app/plugins/modules/backend-configuration-pn/models/documents/document-folder.model';

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

  getAllDocuments(model: DocumentsRequestModel): Observable<OperationDataResult<Paged<DocumentModel>>> {
    return this.apiBaseService.post(BackendConfigurationPnDocumentsMethods.Documents, model);
  }

  getSingleDocument(documentId: number): Observable<OperationDataResult<DocumentModel>> {
    return this.apiBaseService.get(BackendConfigurationPnDocumentsMethods.Documents + '/' + documentId);
  }

  updateDocument(model: DocumentModel): Observable<OperationDataResult<DocumentModel>> {
    return this.apiBaseService.putFormData(BackendConfigurationPnDocumentsMethods.DocumentUpdate + '/' + model.id, model);
  }

  createDocument(model: DocumentModel): Observable<OperationDataResult<DocumentModel>> {
    return this.apiBaseService.postFormData(BackendConfigurationPnDocumentsMethods.DocumentCreate, model);
  }

  deleteDocument(documentId: number): Observable<OperationDataResult<boolean>> {
    return this.apiBaseService.delete(BackendConfigurationPnDocumentsMethods.DocumentDelete + '/' + documentId);
  }

  getAllFolders(model: DocumentFolderRequestModel): Observable<OperationDataResult<Paged<DocumentFolderModel>>> {
    return this.apiBaseService.post(BackendConfigurationPnDocumentsMethods.Folders, model);
  }

  getSingleFolder(folderId: number): Observable<OperationDataResult<DocumentFolderModel>> {
    return this.apiBaseService.get(BackendConfigurationPnDocumentsMethods.Folders + '/' + folderId);
  }

  updateFolder(model: DocumentFolderModel): Observable<OperationDataResult<DocumentFolderModel>> {
    return this.apiBaseService.put(BackendConfigurationPnDocumentsMethods.FolderUpdate + '/' + model.id, model);
  }

  createFolder(model: DocumentFolderModel): Observable<OperationDataResult<DocumentFolderModel>> {
    return this.apiBaseService.post(BackendConfigurationPnDocumentsMethods.FolderCreate, model);
  }

  deleteFolder(folderId: number): Observable<OperationDataResult<boolean>> {
    return this.apiBaseService.delete(BackendConfigurationPnDocumentsMethods.FolderDelete + '/' + folderId);
  }

}
