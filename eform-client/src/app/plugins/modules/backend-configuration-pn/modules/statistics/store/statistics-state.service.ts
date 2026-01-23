import {Injectable} from '@angular/core';
import {Observable} from 'rxjs';
import {OperationDataResult} from 'src/app/common/models';
import {BackendConfigurationPnStatisticsService} from '../../../services';
import {
  AdHocTaskPrioritiesModel,
  AdHocTaskWorkers,
  DocumentUpdatedDaysModel,
  PlannedTaskDaysModel,
  PlannedTaskWorkers,
} from '../../../models';
import {Store} from '@ngrx/store';
import {
  selectStatisticsPropertyId,
  statisticsUpdateFilters
} from '../../../state';

@Injectable({providedIn: 'root'})
export class StatisticsStateService {
  private selectStatisticsPropertyId$ = this.store.select(selectStatisticsPropertyId);
  propertyId: number | null;

  constructor(
    private store: Store,
    private service: BackendConfigurationPnStatisticsService,
  ) {
    this.selectStatisticsPropertyId$.subscribe((id) => this.propertyId = id);
  }

  getPlannedTaskDays(propertyId: number | null = undefined):
    Observable<OperationDataResult<PlannedTaskDaysModel>> {
    return this.service.getPlannedTaskDays({
      propertyId: propertyId === undefined ? this.getPropertyId() : propertyId,
      priority: null, status: null, siteId: null, statuses: null
    });
  }

  getPlannedTaskDaysByFilters(filters: {
    propertyIds: number[];
    tagIds: number[];
    workerIds: number[];
  }) {
    return this.service.getPlannedTaskDaysByFilters({
      propertyIds: filters.propertyIds ?? [],
      tagIds: filters.tagIds ?? [],
      workerIds: filters.workerIds ?? [],
    });
  }

  getAdHocTaskPriorities(
    propertyId: number | null = undefined,
    priority: number | null = undefined,
    status: number | null = undefined,
    siteId: number | null = undefined,
    statuses: number[] | null = undefined):
    Observable<OperationDataResult<AdHocTaskPrioritiesModel>> {
    return this.service.getAdHocTaskPriorities({
      propertyId: propertyId === undefined ? this.getPropertyId() : propertyId,
      priority: priority, status: status, siteId: null, statuses: null
    });
  }

  getAdHocTaskPrioritiesByFilters(model: {
    propertyId?: number | null;
    propertyIds?: number[];
    statuses?: number[];
    priority?: number | null;
    lastAssignedTo?: number | null;
    dateFrom?: string | Date | null;
    dateTo?: string | Date | null;
  }) {
    return this.service.getAdHocTaskPrioritiesByFilters({
      propertyId: model.propertyId ?? null,
      propertyIds: model.propertyIds ?? [],
      statuses: model.statuses ?? [],
      priority: model.priority ?? null,
      lastAssignedTo: model.lastAssignedTo ?? null,
      dateFrom: model.dateFrom ?? null,
      dateTo: model.dateTo ?? null,
    });
  }


  getDocumentUpdatedDays(propertyId: number | null = undefined):
    Observable<OperationDataResult<DocumentUpdatedDaysModel>> {
    return this.service.getDocumentUpdatedDays({
      propertyId: propertyId === undefined ? this.getPropertyId() : propertyId,
      priority: null, status: null, siteId: null, statuses: null
    });
  }

  getPlannedTaskWorkers(propertyId: number | null = undefined, status: number | null = undefined, siteId: number | null = undefined):
    Observable<OperationDataResult<PlannedTaskWorkers>> {
    return this.service.getPlannedTaskWorkers({
      propertyId: propertyId === undefined ? this.getPropertyId() : propertyId,
      priority: null, status: status, siteId: siteId, statuses: null
    });
  }

  getPlannedTaskWorkersByFilters(filters: {
    propertyIds: number[];
    status: number[];
    folderIds: number[];
    tagIds: number[];
    assignToIds: number[];
  }) {
    return this.service.getPlannedTaskWorkersByFilters({
      propertyIds: filters.propertyIds ?? [],
      status: filters.status ?? [],
      folderIds: filters.folderIds ?? [],
      tagIds: filters.tagIds ?? [],
      assignToIds: filters.assignToIds ?? [],
    });
  }


  getAdHocTaskWorkers(propertyId: number | null = undefined, siteId: number | null = undefined):
    Observable<OperationDataResult<AdHocTaskWorkers>> {
    return this.service.getAdHocTaskWorkers({
      propertyId: propertyId === undefined ? this.getPropertyId() : propertyId,
      priority: null, status: null, siteId: siteId, statuses: null
    });
  }

  getAdHocTaskWorkersByFilters(filters: {
    propertyId: number[];
    areaName: number[];
    createdBy: number[];
    lastAssignedTo: number[];
    statuses: number[];
    priority: number[];
    dateFrom: number[];
    dateTo: number[];
  }) {
    return this.service.getAdHocTaskWorkersByFilters({
      propertyId: filters.propertyId ?? [],
      areaName: filters.areaName ?? [],
      createdBy: filters.createdBy ?? [],
      lastAssignedTo: filters.lastAssignedTo ?? [],
      statuses: filters.statuses ?? [],
      priority: filters.priority ?? [],
      dateFrom: filters.dateFrom ?? [],
      dateTo: filters.dateTo ?? [],
    });
  }

  getPropertyId(): number | null {
    return this.propertyId;
  }

  updatePropertyId(propertyId: number | null) {
    this.store.dispatch(statisticsUpdateFilters({propertyId: propertyId}));
  }
}
