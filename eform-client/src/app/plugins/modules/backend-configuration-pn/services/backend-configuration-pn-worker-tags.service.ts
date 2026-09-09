import {Injectable} from '@angular/core';
import {Observable} from 'rxjs';
import {CommonDictionaryModel, OperationDataResult} from 'src/app/common/models';
import {ApiBaseService} from 'src/app/common/services';

export let BackendConfigurationWorkerTagsMethods = {
  WorkerTags: 'api/backend-configuration-pn/worker-tags',
};

/**
 * Worker groups ("teams").
 *
 * The SDK has a single `Tags` table holding BOTH worker groups and eForm/template
 * tags, and the core `EformTagService.getAvailableTags()` returns all of them — so
 * the calendar used to offer template tags as teams (#1213). This plugin endpoint
 * returns only the tags that have at least one live worker member; the filtering is
 * done by the query on the server, not by discarding rows here.
 */
@Injectable({
  providedIn: 'root',
})
export class BackendConfigurationPnWorkerTagsService {
  constructor(private apiBaseService: ApiBaseService) {}

  getWorkerTags(): Observable<OperationDataResult<CommonDictionaryModel[]>> {
    return this.apiBaseService.get<CommonDictionaryModel[]>(
      BackendConfigurationWorkerTagsMethods.WorkerTags
    );
  }
}
