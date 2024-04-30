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
      priority: null, status: null, siteId: null, statuses: null});
  }

  getAdHocTaskPriorities(propertyId: number | null = undefined, priority: number | null = undefined, status: number | null = undefined, siteId: number | null = undefined, statuses: number[] | null = undefined):
    Observable<OperationDataResult<AdHocTaskPrioritiesModel>> {
    return this.service.getAdHocTaskPriorities({
      propertyId: propertyId === undefined ? this.getPropertyId() : propertyId,
      priority: priority, status: status, siteId: null, statuses: null});
  }

  getDocumentUpdatedDays(propertyId: number | null = undefined):
    Observable<OperationDataResult<DocumentUpdatedDaysModel>> {
    return this.service.getDocumentUpdatedDays({
      propertyId: propertyId === undefined ? this.getPropertyId() : propertyId,
      priority: null, status: null, siteId: null, statuses: null});
  }

  getPlannedTaskWorkers(propertyId: number | null = undefined, status: number | null = undefined, siteId: number | null = undefined):
    Observable<OperationDataResult<PlannedTaskWorkers>> {
    return this.service.getPlannedTaskWorkers({
      propertyId: propertyId === undefined ? this.getPropertyId() : propertyId,
      priority: null, status: status, siteId: siteId, statuses: null});
  }

  getAdHocTaskWorkers(propertyId: number | null = undefined, siteId: number | null = undefined):
    Observable<OperationDataResult<AdHocTaskWorkers>> {
    return this.service.getAdHocTaskWorkers({
      propertyId: propertyId === undefined ? this.getPropertyId() : propertyId,
      priority: null, status: null, siteId: siteId, statuses: null});
  }

  getPropertyId(): number | null {
    return this.propertyId;
  }

  updatePropertyId(propertyId: number | null) {
    this.store.dispatch(statisticsUpdateFilters({propertyId: propertyId}));
  }
}
