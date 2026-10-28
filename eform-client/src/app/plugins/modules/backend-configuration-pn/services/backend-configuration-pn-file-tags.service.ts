import {Injectable} from '@angular/core';
import {Observable} from 'rxjs';
import {
  OperationDataResult,
  OperationResult,
  SharedTagCreateModel,
  SharedTagModel,
  SharedTagMultipleCreateModel,
} from 'src/app/common/models';
import {ApiBaseService} from 'src/app/common/services';
import {ArchiveAccountDisabledHandler} from './archive-account-disabled.handler';

export let BackendConfigurationFileTagsMethods = {
  Tags: 'api/backend-configuration-pn/file-tags',
  CreateBulkTags: 'api/backend-configuration-pn/file-tags/bulk',
};

/**
 * Etiketter (document tags). Every call goes through the account-disabled handler (the API has the D7 filter, S3).
 * Rename and delete are admin/Leder-only (D-L11): a non-manager's 403 is toasted by the handler.
 */
@Injectable({
  providedIn: 'root',
})
export class BackendConfigurationPnFileTagsService {
  constructor(private apiBaseService: ApiBaseService, private accountDisabled: ArchiveAccountDisabledHandler) {}

  getTags(): Observable<OperationDataResult<SharedTagModel[]>> {
    return this.apiBaseService.get<SharedTagModel[]>(BackendConfigurationFileTagsMethods.Tags)
      .pipe(this.accountDisabled.handle());
  }

  updateTag(model: SharedTagModel): Observable<OperationResult> {
    return this.apiBaseService.put(BackendConfigurationFileTagsMethods.Tags, model)
      .pipe(this.accountDisabled.handle());
  }

  deleteTag(id: number): Observable<OperationResult> {
    return this.apiBaseService.delete(`${BackendConfigurationFileTagsMethods.Tags}/${id}`)
      .pipe(this.accountDisabled.handle());
  }

  createTag(model: SharedTagCreateModel): Observable<OperationResult> {
    return this.apiBaseService.post(BackendConfigurationFileTagsMethods.Tags, model)
      .pipe(this.accountDisabled.handle());
  }

  getTagById(id: number): Observable<OperationDataResult<SharedTagModel>> {
    return this.apiBaseService.get<SharedTagModel>(`${BackendConfigurationFileTagsMethods.Tags}/${id}`)
      .pipe(this.accountDisabled.handle());
  }

  createTags(model: SharedTagMultipleCreateModel): Observable<OperationResult> {
    return this.apiBaseService.post(BackendConfigurationFileTagsMethods.CreateBulkTags, model)
      .pipe(this.accountDisabled.handle());
  }
}
