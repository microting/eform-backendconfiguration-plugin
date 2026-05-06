import {Injectable} from '@angular/core';
import {Observable} from 'rxjs';
import {ApiBaseService} from 'src/app/common/services';
import {OperationDataResult, OperationResult} from 'src/app/common/models';
import {CalendarTaskAttachment} from '../models/calendar';

export let BackendConfigurationPnCalendarFilesMethods = {
  TasksFilesBase: 'api/backend-configuration-pn/calendar/tasks',
};

/**
 * Calendar event-attachments service. Wraps the four /calendar/tasks/{id}/files
 * endpoints introduced in Layer 2 of the event-attachments feature. See
 * docs/superpowers/specs/2026-05-06-calendar-event-attachments-design.md
 * for the contract.
 */
@Injectable({providedIn: 'root'})
export class BackendConfigurationPnCalendarFilesService {
  constructor(private apiBaseService: ApiBaseService) {}

  uploadFile(taskId: number, file: File): Observable<OperationDataResult<CalendarTaskAttachment>> {
    return this.apiBaseService.postFormData(
      `${BackendConfigurationPnCalendarFilesMethods.TasksFilesBase}/${taskId}/files`,
      {file}
    );
  }

  listFiles(taskId: number): Observable<OperationDataResult<CalendarTaskAttachment[]>> {
    return this.apiBaseService.get(
      `${BackendConfigurationPnCalendarFilesMethods.TasksFilesBase}/${taskId}/files`
    );
  }

  /**
   * Pure URL builder — passed through `authImage` pipe for `<img src>` thumbnails.
   * For PDF/preview opens use {@link getFileBlob} instead: a plain
   * `window.open(downloadUrl, '_blank')` won't carry the bearer token and 401s.
   */
  downloadUrl(taskId: number, fileId: number): string {
    return `/${BackendConfigurationPnCalendarFilesMethods.TasksFilesBase}/${taskId}/files/${fileId}`;
  }

  /**
   * Fetches the raw file as a Blob (with auth headers attached). Caller wraps
   * the Blob in `URL.createObjectURL(...)` and `window.open`s it — mirrors the
   * pattern used by `element-pdf.component.ts` for authenticated PDF previews.
   */
  getFileBlob(taskId: number, fileId: number): Observable<Blob> {
    return this.apiBaseService.getBlobData(
      `${BackendConfigurationPnCalendarFilesMethods.TasksFilesBase}/${taskId}/files/${fileId}`
    );
  }

  deleteFile(taskId: number, fileId: number): Observable<OperationResult> {
    return this.apiBaseService.delete(
      `${BackendConfigurationPnCalendarFilesMethods.TasksFilesBase}/${taskId}/files/${fileId}`
    );
  }
}
