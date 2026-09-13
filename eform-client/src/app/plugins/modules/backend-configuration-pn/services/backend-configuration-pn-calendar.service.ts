import {Injectable} from '@angular/core';
import {Observable} from 'rxjs';
import {tap} from 'rxjs/operators';
import {ToastrService} from 'ngx-toastr';
import {TranslateService} from '@ngx-translate/core';
import {ApiBaseService} from 'src/app/common/services';
import {OperationDataResult, OperationResult} from 'src/app/common/models';
import {
  CalendarBoardModel,
  CalendarPrepareCompleteResult,
  CalendarTaskCreateModel,
  CalendarTaskIndexRequestModel,
  CalendarTaskModel,
  CalendarTaskUpdateModel,
  CalendarTaskWeekRequestModel,
  RepeatDeleteScope,
  RepeatEditScope,
} from '../models';

export let BackendConfigurationPnCalendarMethods = {
  TasksWeek: 'api/backend-configuration-pn/calendar/tasks/week',
  Index: 'api/backend-configuration-pn/calendar/tasks/index',
  Tasks: 'api/backend-configuration-pn/calendar/tasks',
  MoveTask: 'api/backend-configuration-pn/calendar/tasks/move',
  ResizeTask: 'api/backend-configuration-pn/calendar/tasks/resize',
  Boards: 'api/backend-configuration-pn/calendar/boards',
};

@Injectable({providedIn: 'root'})
export class BackendConfigurationPnCalendarService {
  constructor(
    private apiBaseService: ApiBaseService,
    private toastr: ToastrService,
    private translate: TranslateService,
  ) {}

  private notify(res: OperationResult): void {
    if (res && res.success) {
      this.toastr.success(this.translate.instant('Updated.'));
    } else {
      this.notifyError(res);
    }
  }

  private notifyError(res: OperationResult): void {
    if (!res || !res.success) {
      this.toastr.error(`${this.translate.instant('Error')} [${(res && res.message) || 'unknown'}]`);
    }
  }

  /**
   * The grid's read path. Takes the whole request as one model rather than a
   * positional argument list — see `CalendarTaskWeekRequestModel`, which also
   * documents why `siteIds` and `workerTagIds` are ORed server-side.
   *
   * The model is posted as-is: no field is defaulted or dropped here, so a
   * filter the caller forgot shows up as a compile error rather than as a
   * silently unfiltered grid.
   */
  getTasksForWeek(
    model: CalendarTaskWeekRequestModel
  ): Observable<OperationDataResult<CalendarTaskModel[]>> {
    return this.apiBaseService.postNoToast(BackendConfigurationPnCalendarMethods.TasksWeek, model)
      .pipe(tap((res) => this.notifyError(res)));
  }

  getTasksIndex(model: CalendarTaskIndexRequestModel): Observable<OperationDataResult<CalendarTaskModel[]>> {
    return this.apiBaseService.postNoToast(BackendConfigurationPnCalendarMethods.Index, model)
      .pipe(tap((res) => this.notifyError(res)));
  }

  createTask(model: CalendarTaskCreateModel): Observable<OperationDataResult<number>> {
    return this.apiBaseService.postNoToast(BackendConfigurationPnCalendarMethods.Tasks, model)
      .pipe(tap((res) => this.notify(res)));
  }

  updateTask(model: CalendarTaskUpdateModel, scope: RepeatEditScope): Observable<OperationResult> {
    return this.apiBaseService.putNoToast(BackendConfigurationPnCalendarMethods.Tasks, {...model, scope})
      .pipe(tap((res) => this.notify(res)));
  }

  deleteTask(id: number, scope: RepeatDeleteScope, originalDate: string): Observable<OperationResult> {
    return this.apiBaseService.putNoToast(`${BackendConfigurationPnCalendarMethods.Tasks}/delete`, {id, scope, originalDate})
      .pipe(tap((res) => this.notify(res)));
  }

  moveTask(id: number, newDate: string, newStartHour: number): Observable<OperationResult> {
    return this.apiBaseService.putNoToast(BackendConfigurationPnCalendarMethods.MoveTask, {id, newDate, newStartHour})
      .pipe(tap((res) => this.notify(res)));
  }

  moveTaskWithScope(
    id: number,
    newDate: string,
    newStartHour: number,
    scope: 'this' | 'thisAndFollowing' | 'all',
    originalDate: string
  ): Observable<OperationResult> {
    return this.apiBaseService.putNoToast(BackendConfigurationPnCalendarMethods.MoveTask, {id, newDate, newStartHour, scope, originalDate})
      .pipe(tap((res) => this.notify(res)));
  }

  resizeTask(
    id: number,
    newStartHour: number,
    newDuration: number,
    scope: 'this' | 'thisAndFollowing' | 'all',
    originalDate: string,
  ): Observable<OperationResult> {
    return this.apiBaseService.putNoToast(BackendConfigurationPnCalendarMethods.ResizeTask, {id, newStartHour, newDuration, scope, originalDate})
      .pipe(tap((res) => this.notify(res)));
  }

  prepareComplete(
    taskId: number,
    complianceId: number | null | undefined,
    occurrenceDate: string | null | undefined,
  ): Observable<OperationDataResult<CalendarPrepareCompleteResult>> {
    return this.apiBaseService.postNoToast(
      `${BackendConfigurationPnCalendarMethods.Tasks}/${taskId}/prepare-complete`,
      {complianceId: complianceId ?? null, occurrenceDate: occurrenceDate ?? null}
    ).pipe(tap((res) => this.notifyError(res)));
  }

  getBoards(propertyId: number): Observable<OperationDataResult<CalendarBoardModel[]>> {
    return this.apiBaseService.getNoToast(`${BackendConfigurationPnCalendarMethods.Boards}/${propertyId}`)
      .pipe(tap((res) => this.notifyError(res)));
  }

  createBoard(model: {name: string; color: string; propertyId: number}): Observable<OperationResult> {
    return this.apiBaseService.postNoToast(BackendConfigurationPnCalendarMethods.Boards, model)
      .pipe(tap((res) => this.notify(res)));
  }

  updateBoard(model: {id: number; name: string; color: string}): Observable<OperationResult> {
    return this.apiBaseService.putNoToast(BackendConfigurationPnCalendarMethods.Boards, model)
      .pipe(tap((res) => this.notify(res)));
  }

  deleteBoard(id: number): Observable<OperationResult> {
    return this.apiBaseService.deleteNoToast(`${BackendConfigurationPnCalendarMethods.Boards}/${id}`)
      .pipe(tap((res) => this.notify(res)));
  }

  getBoardEventCount(id: number): Observable<OperationDataResult<number>> {
    return this.apiBaseService.getNoToast(`${BackendConfigurationPnCalendarMethods.Boards}/${id}/event-count`)
      .pipe(tap((res) => this.notifyError(res)));
  }
}
