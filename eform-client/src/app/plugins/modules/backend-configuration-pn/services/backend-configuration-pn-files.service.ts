import {ApiBaseService} from 'src/app/common/services';
import {Injectable} from '@angular/core';
import {OperationDataResult, OperationResult, Paged} from 'src/app/common/models';
import {Observable} from 'rxjs';
import {FilesModel, FilesRequestModel, FilesUpdateFilename, FilesUpdateFileTags} from '../models';

export let BackendConfigurationPnFilesMethods = {
  Files: 'api/backend-configuration-pn/files',
}

@Injectable({
  providedIn: 'root',
})

export class BackendConfigurationPnFilesService {
  constructor(private apiBaseService: ApiBaseService) {}

  getAllFiles(model: FilesRequestModel): Observable<OperationDataResult<Paged<FilesModel>>> {
    return this.apiBaseService.get(BackendConfigurationPnFilesMethods.Files, {...model});
  }

  getFile(fileId: number): Observable<OperationDataResult<FilesModel>> {
    return this.apiBaseService.get(`${BackendConfigurationPnFilesMethods.Files}/${fileId}`);
  }

  updateFileName(model: FilesUpdateFilename): Observable<OperationResult> {
    return this.apiBaseService.put(BackendConfigurationPnFilesMethods.Files, model);
  }

  updateFileTags(model: FilesUpdateFileTags): Observable<OperationResult> {
    return this.apiBaseService.put(`${BackendConfigurationPnFilesMethods.Files}/tags`, model);
  }

  createFile(model: any): Observable<OperationResult> {
    return this.apiBaseService.postFormData(BackendConfigurationPnFilesMethods.Files, model);
  }

  deleteFile(fileId: number): Observable<OperationResult> {
    return this.apiBaseService.delete(`${BackendConfigurationPnFilesMethods.Files}/${fileId}`);
  }
}
