import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {
  OperationDataResult,
  OperationResult,
  SharedTagCreateModel,
  SharedTagModel, SharedTagMultipleCreateModel,
} from 'src/app/common/models';
import { ApiBaseService } from 'src/app/common/services';

export let BackendConfigurationFileTagsMethods = {
  Tags: 'api/backend-configuration-pn/file-tags',
  CreateBulkTags: 'api/backend-configuration-pn/file-tags/bulk',
};

@Injectable({
  providedIn: 'root',
})
export class BackendConfigurationPnFileTagsService {
  constructor(private apiBaseService: ApiBaseService) {}

  getTags(): Observable<OperationDataResult<SharedTagModel[]>> {
    return this.apiBaseService.get<SharedTagModel[]>(BackendConfigurationFileTagsMethods.Tags);
  }

  updateTag(model: SharedTagModel): Observable<OperationResult> {
    return this.apiBaseService.put(
      BackendConfigurationFileTagsMethods.Tags,
      model
    );
  }

  deleteTag(id: number): Observable<OperationResult> {
    return this.apiBaseService.delete(`${BackendConfigurationFileTagsMethods.Tags}/${id}`);
  }

  createTag(model: SharedTagCreateModel): Observable<OperationResult> {
    return this.apiBaseService.post(
      BackendConfigurationFileTagsMethods.Tags,
      model
    );
  }

  getTagById(id: number): Observable<OperationDataResult<SharedTagModel>> {
    return this.apiBaseService.get(`${BackendConfigurationFileTagsMethods.Tags}/${id}`);
  }


  createTags(model: SharedTagMultipleCreateModel): Observable<OperationResult> {
    return this.apiBaseService.post(
      BackendConfigurationFileTagsMethods.CreateBulkTags,
      model
    );
  }
}
