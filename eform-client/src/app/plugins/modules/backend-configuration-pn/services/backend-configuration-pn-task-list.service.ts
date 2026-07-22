import {Injectable} from '@angular/core';
import {Observable} from 'rxjs';
import {OperationResult} from 'src/app/common/models';
import {ApiBaseService} from 'src/app/common/services';

export class TaskListBatchRequest { taskIds: number[] = []; }
export class TaskListBatchAssignRequest extends TaskListBatchRequest { siteId: number; }
export class TaskListBatchReassignRequest extends TaskListBatchRequest { fromSiteId: number; toSiteId: number; }
export class TaskListBatchChangeEformRequest extends TaskListBatchRequest { eformId: number; }
export class TaskListBatchTagsRequest extends TaskListBatchRequest { tagIds: number[] = []; }
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
  copy(model: TaskListBatchCopyRequest): Observable<OperationResult> {
    return this.apiBaseService.post(`${TaskListMethods.Base}/copy`, model);
  }
  delete(model: TaskListBatchRequest): Observable<OperationResult> {
    return this.apiBaseService.post(`${TaskListMethods.Base}/delete`, model);
  }
}
