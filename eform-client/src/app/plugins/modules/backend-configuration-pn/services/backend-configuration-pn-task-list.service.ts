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
/**
 * #1123 — `active` is the whole payload: true activates every selected task,
 * false deactivates them (retracting the OPEN occurrences server-side and
 * preserving the completed ones).
 */
export class TaskListBatchStatusRequest extends TaskListBatchRequest { active: boolean; }
export class TaskListBatchCopyRequest extends TaskListBatchRequest {
  targetPropertyId: number; targetBoardId: number; startDate: string; siteId: number;
}
// `startDate` is a date-only "yyyy-MM-dd" string, NOT an ISO instant: the
// backend binds it to a `DateTime`, and sending `toISOString()` would shift the
// picked calendar day across a UTC offset. Same convention as
// TaskListBatchCopyRequest above.
export class TaskListBatchStartDateRequest extends TaskListBatchRequest { startDate: string; }

/**
 * #1126 — inline rename from the task-list grid row. `taskIds` always carries
 * exactly ONE id: the action is single-row, but it rides the batch rail so the
 * server reuses `BuildUpdateModel` -> `UpdateTask` and the name lands in BOTH
 * `AreaRuleTranslation` (what this grid reads) and `PlanningNameTranslation`
 * (what the items-planning Plannings list reads).
 */
export class TaskListRenameRequest extends TaskListBatchRequest { title: string; }

/**
 * Read-only projection of what `change-start-date` WOULD do, returned by the
 * `/preview` endpoint. Counted by enumerating exactly as the write path does,
 * but writing nothing.
 */
export class TaskListBatchStartDatePreviewModel {
  taskCount: number;
  occurrencesToRetract: number;
  completedPreserved: number;
  overdueToCreate: number;
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
  /**
   * Activates or deactivates every selected task. Deactivation goes through the
   * calendar/task-wizard path, which retracts only the NOT-yet-completed
   * occurrences — completed ones and their collected data survive (invariant
   * R2), which is what the modal's warning promises.
   */
  setStatus(model: TaskListBatchStatusRequest): Observable<OperationResult> {
    return this.apiBaseService.post(`${TaskListMethods.Base}/set-status`, model);
  }
  copy(model: TaskListBatchCopyRequest): Observable<OperationResult> {
    return this.apiBaseService.post(`${TaskListMethods.Base}/copy`, model);
  }
  /**
   * Re-anchors every selected task's series to `model.startDate`. The date may
   * be in the PAST — that is the whole point of the action (#1122) — so the
   * modal's picker deliberately carries no `minDate`.
   */
  changeStartDate(model: TaskListBatchStartDateRequest): Observable<OperationResult> {
    return this.apiBaseService.post(`${TaskListMethods.Base}/change-start-date`, model);
  }

  /**
   * Preview counts for `changeStartDate`, with no writes.
   *
   * `postNoToast` on purpose, for the same reason as `purgeOrphanTags` below:
   * this fires on EVERY date change in the modal (debounced), so a toast per
   * keystroke/day-click would bury the screen — and a failed preview is
   * surfaced inline in the modal's preview panel instead, where it also has to
   * keep Save disabled.
   */
  changeStartDatePreview(
    model: TaskListBatchStartDateRequest,
  ): Observable<OperationDataResult<TaskListBatchStartDatePreviewModel>> {
    return this.apiBaseService.postNoToast(`${TaskListMethods.Base}/change-start-date/preview`, model);
  }

  /**
   * Renames the single task in `model.taskIds`. Uses the toasting `post` like
   * every other action on this page — the server's message carries the reason a
   * rename was refused, which the grid's own inline error deliberately does not
   * try to reproduce.
   */
  rename(model: TaskListRenameRequest): Observable<OperationResult> {
    return this.apiBaseService.post(`${TaskListMethods.Base}/rename`, model);
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
