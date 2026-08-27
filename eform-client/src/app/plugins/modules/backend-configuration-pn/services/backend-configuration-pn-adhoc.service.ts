import {Injectable} from '@angular/core';
import {Observable} from 'rxjs';
import {OperationDataResult, OperationResult, Paged} from 'src/app/common/models';
import {ApiBaseService} from 'src/app/common/services';
import {
  AdhocAreaCreateModel,
  AdhocAreaModel,
  AdhocAreaRenameModel,
  AdhocCommentCreateModel,
  AdhocCopyTaskModel,
  AdhocHistoryFiltersModel,
  AdhocPropertyModel,
  AdhocSetCompletedModel,
  AdhocTagCreateModel,
  AdhocTagModel,
  AdhocTaskCreateModel,
  AdhocTaskFiltersModel,
  AdhocTaskHistoryRowModel,
  AdhocTaskIndexResultModel,
  AdhocTaskModel,
  AdhocTaskPhotoModel,
  AdhocWorkerModel,
} from '../models';

/**
 * REST façade for the "Adhoc overblik" dashboard (M5/F3). Routes mirror the
 * merged AdhocController.cs (M3-B6, extended M5-P2/P3) exactly - see
 * flutter-adhoc/.superpowers/sdd/m5-p1-p3-report.md for the contract this
 * was verified against.
 */
export let BackendConfigurationPnAdhocMethods = {
  Index: 'api/backend-configuration-pn/adhoc/index',
  Tasks: 'api/backend-configuration-pn/adhoc/',
  HistoryIndex: 'api/backend-configuration-pn/adhoc/history/index',
  Properties: 'api/backend-configuration-pn/adhoc/properties',
  Areas: 'api/backend-configuration-pn/adhoc/areas',
  Workers: 'api/backend-configuration-pn/adhoc/workers',
  Tags: 'api/backend-configuration-pn/adhoc/tags',
  Photos: 'api/backend-configuration-pn/adhoc/photos',
};

@Injectable({
  providedIn: 'root',
})
export class BackendConfigurationPnAdhocService {
  constructor(private apiBaseService: ApiBaseService) {
  }

  // -----------------------------------------------------------------
  // Tasks
  // -----------------------------------------------------------------

  getTasks(model: AdhocTaskFiltersModel): Observable<OperationDataResult<AdhocTaskIndexResultModel>> {
    return this.apiBaseService.post(BackendConfigurationPnAdhocMethods.Index, model);
  }

  getTask(id: number): Observable<OperationDataResult<AdhocTaskModel>> {
    return this.apiBaseService.get(`${BackendConfigurationPnAdhocMethods.Tasks}${id}`);
  }

  createTask(model: AdhocTaskCreateModel): Observable<OperationDataResult<AdhocTaskModel>> {
    return this.apiBaseService.post(BackendConfigurationPnAdhocMethods.Tasks, model);
  }

  updateTask(id: number, model: AdhocTaskCreateModel): Observable<OperationDataResult<AdhocTaskModel>> {
    return this.apiBaseService.put(`${BackendConfigurationPnAdhocMethods.Tasks}${id}`, model);
  }

  setCompleted(
    id: number,
    completed: boolean,
    completedByWorkerId?: number | null
  ): Observable<OperationDataResult<AdhocTaskModel>> {
    const body: AdhocSetCompletedModel = {completed, completedByWorkerId};
    return this.apiBaseService.post(`${BackendConfigurationPnAdhocMethods.Tasks}${id}/completed`, body);
  }

  archiveTask(id: number): Observable<OperationDataResult<AdhocTaskModel>> {
    return this.apiBaseService.post(`${BackendConfigurationPnAdhocMethods.Tasks}${id}/archive`, {});
  }

  reopenTask(id: number): Observable<OperationDataResult<AdhocTaskModel>> {
    return this.apiBaseService.post(`${BackendConfigurationPnAdhocMethods.Tasks}${id}/reopen`, {});
  }

  deleteTask(id: number): Observable<OperationResult> {
    return this.apiBaseService.delete(`${BackendConfigurationPnAdhocMethods.Tasks}${id}`);
  }

  copyTask(id: number, includeComments: boolean): Observable<OperationDataResult<AdhocTaskModel>> {
    const body: AdhocCopyTaskModel = {includeComments};
    return this.apiBaseService.post(`${BackendConfigurationPnAdhocMethods.Tasks}${id}/copy`, body);
  }

  addComment(id: number, text: string): Observable<OperationDataResult<AdhocTaskModel>> {
    const body: AdhocCommentCreateModel = {text};
    return this.apiBaseService.post(`${BackendConfigurationPnAdhocMethods.Tasks}${id}/comments`, body);
  }

  // -----------------------------------------------------------------
  // History
  // -----------------------------------------------------------------

  getHistory(model: AdhocHistoryFiltersModel): Observable<OperationDataResult<Paged<AdhocTaskHistoryRowModel>>> {
    return this.apiBaseService.post(BackendConfigurationPnAdhocMethods.HistoryIndex, model);
  }

  // -----------------------------------------------------------------
  // Reference data
  // -----------------------------------------------------------------

  getProperties(): Observable<OperationDataResult<AdhocPropertyModel[]>> {
    return this.apiBaseService.get(BackendConfigurationPnAdhocMethods.Properties);
  }

  getAreas(propertyId: number): Observable<OperationDataResult<AdhocAreaModel[]>> {
    return this.apiBaseService.get(BackendConfigurationPnAdhocMethods.Areas, {propertyId});
  }

  createAreas(propertyId: number, names: string[]): Observable<OperationDataResult<AdhocAreaModel[]>> {
    const body: AdhocAreaCreateModel = {propertyId, names};
    return this.apiBaseService.post(BackendConfigurationPnAdhocMethods.Areas, body);
  }

  renameArea(id: number, name: string): Observable<OperationDataResult<AdhocAreaModel>> {
    const body: AdhocAreaRenameModel = {name};
    return this.apiBaseService.put(`${BackendConfigurationPnAdhocMethods.Areas}/${id}`, body);
  }

  deleteArea(id: number): Observable<OperationResult> {
    return this.apiBaseService.delete(`${BackendConfigurationPnAdhocMethods.Areas}/${id}`);
  }

  getWorkers(propertyId: number): Observable<OperationDataResult<AdhocWorkerModel[]>> {
    return this.apiBaseService.get(BackendConfigurationPnAdhocMethods.Workers, {propertyId});
  }

  // -----------------------------------------------------------------
  // Tags
  // -----------------------------------------------------------------

  getTags(): Observable<OperationDataResult<AdhocTagModel[]>> {
    return this.apiBaseService.get(BackendConfigurationPnAdhocMethods.Tags);
  }

  createTag(name: string): Observable<OperationDataResult<AdhocTagModel>> {
    const body: AdhocTagCreateModel = {name};
    return this.apiBaseService.post(BackendConfigurationPnAdhocMethods.Tags, body);
  }

  renameTag(id: number, name: string): Observable<OperationDataResult<AdhocTagModel>> {
    const body: AdhocTagCreateModel = {name};
    return this.apiBaseService.put(`${BackendConfigurationPnAdhocMethods.Tags}/${id}`, body);
  }

  deleteTag(id: number): Observable<OperationResult> {
    return this.apiBaseService.delete(`${BackendConfigurationPnAdhocMethods.Tags}/${id}`);
  }

  // -----------------------------------------------------------------
  // Photos
  // -----------------------------------------------------------------

  uploadPhoto(taskId: number, file: File): Observable<OperationDataResult<AdhocTaskPhotoModel>> {
    return this.apiBaseService.postFormData(
      `${BackendConfigurationPnAdhocMethods.Tasks}${taskId}/photos`,
      {file}
    );
  }

  /**
   * Pure URL builder for `<img src>` thumbnails (via the repo's authImage
   * pipe) - mirrors `BackendConfigurationPnCalendarFilesService.downloadUrl`.
   * For an authenticated Blob fetch use {@link getPhotoBlob} instead.
   */
  photoUrl(photoId: number): string {
    return `/${BackendConfigurationPnAdhocMethods.Photos}/${photoId}`;
  }

  getPhotoBlob(photoId: number): Observable<Blob> {
    return this.apiBaseService.getBlobData(`${BackendConfigurationPnAdhocMethods.Photos}/${photoId}`);
  }
}
