import {Injectable} from '@angular/core';
import {Observable} from 'rxjs';
import {OperationDataResult, OperationResult} from 'src/app/common/models';
import {ApiBaseService} from 'src/app/common/services';

export class TaskListBatchRequest { taskIds: number[] = []; }
export class TaskListBatchAssignRequest extends TaskListBatchRequest { siteId: number; }
export class TaskListBatchReassignRequest extends TaskListBatchRequest { fromSiteId: number; toSiteId: number; }
export class TaskListBatchChangeEformRequest extends TaskListBatchRequest { eformId: number; }
export class TaskListBatchTagsRequest extends TaskListBatchRequest { tagIds: number[] = []; }
export class TaskListBatchComplianceRequest extends TaskListBatchRequest { complianceEnabled: boolean; }
export class TaskListBatchCopyRequest extends TaskListBatchRequest {
  targetPropertyId: number; targetBoardId: number; startDate: string; siteId: number;
}

export const TaskListMethods = {
  Base: 'api/backend-configuration-pn/task-list',
};

@Injectable({providedIn: 'root'})
export class BackendConfigurationPnTaskListService {
  constructor(private apiBaseService: ApiBaseService) {}

  assign(model: TaskListBatchAssignRequest): Observable<OperationResult> {
    return this.apiBaseService.post(`${TaskListMethods.Base}/assign`, model);
  }
  reassign(model: TaskListBatchReassignRequest): Observable<OperationResult> {
    return this.apiBaseService.post(`${TaskListMethods.Base}/reassign`, model);
  }
  addWorker(model: TaskListBatchAssignRequest): Observable<OperationResult> {
    return this.apiBaseService.post(`${TaskListMethods.Base}/add-worker`, model);
  }
  changeEform(model: TaskListBatchChangeEformRequest): Observable<OperationResult> {
    return this.apiBaseService.post(`${TaskListMethods.Base}/change-eform`, model);
  }
  addTags(model: TaskListBatchTagsRequest): Observable<OperationResult> {
    return this.apiBaseService.post(`${TaskListMethods.Base}/add-tags`, model);
  }
  removeTags(model: TaskListBatchTagsRequest): Observable<OperationResult> {
    return this.apiBaseService.post(`${TaskListMethods.Base}/remove-tags`, model);
  }
  setCompliance(model: TaskListBatchComplianceRequest): Observable<OperationResult> {
    return this.apiBaseService.post(`${TaskListMethods.Base}/set-compliance`, model);
  }
  copy(model: TaskListBatchCopyRequest): Observable<OperationResult> {
    return this.apiBaseService.post(`${TaskListMethods.Base}/copy`, model);
  }
  delete(model: TaskListBatchRequest): Observable<OperationResult> {
    return this.apiBaseService.post(`${TaskListMethods.Base}/delete`, model);
  }

  /**
   * Soft-deletes AreaRulePlanningTag rows left pointing at a tag that has been
   * deleted. `AreaRulePlanningTag.ItemPlanningTagId` is a bare int naming a row in
   * the items-planning DATABASE, so the tag-delete endpoint cannot clean the join
   * up itself; this is what makes a delete done in the Manage-tags dialog take
   * effect on the task list right away.
   *
   * `postNoToast` on purpose: this fires after every tag create/rename/delete and
   * is a no-op in most of those cases, so it must stay silent. Returns the number
   * of rows purged.
   */
  purgeOrphanTags(): Observable<OperationDataResult<number>> {
    return this.apiBaseService.postNoToast(`${TaskListMethods.Base}/purge-orphan-tags`, {});
  }
}
