import {Injectable} from '@angular/core';
import {Observable} from 'rxjs';
import {CommonDictionaryModel, OperationDataResult} from 'src/app/common/models';
import {ApiBaseService} from 'src/app/common/services';

/**
 * A team entry. `memberSiteIds` is only present on the property-scoped list
 * (`getWorkerTags(propertyId)`, #1295): the team's live members linked to that
 * property — exactly the sites the team deploys to there.
 */
export interface WorkerTagModel extends CommonDictionaryModel {
  memberSiteIds?: number[] | null;
}

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

  /**
   * Without `propertyId`: the installation-wide teams list (header filter, tile name
   * maps). With `propertyId` (#1295): only teams with at least one live member linked
   * to that property, each carrying those members in `memberSiteIds` — the task
   * modal's grouped assignee picker uses this.
   */
  getWorkerTags(propertyId?: number | null): Observable<OperationDataResult<WorkerTagModel[]>> {
    if (propertyId == null) {
      return this.apiBaseService.get<WorkerTagModel[]>(
        BackendConfigurationWorkerTagsMethods.WorkerTags
      );
    }
    return this.apiBaseService.get<WorkerTagModel[]>(
      BackendConfigurationWorkerTagsMethods.WorkerTags,
      {propertyId}
    );
  }
}
