import {Injectable} from '@angular/core';
import {StatisticsQuery, StatisticsStore} from './';
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

@Injectable({providedIn: 'root'})
export class StatisticsStateService {
  constructor(
    public store: StatisticsStore,
    private service: BackendConfigurationPnStatisticsService,
    private query: StatisticsQuery,
  ) {
  }

  getPlannedTaskDays(propertyId: number | null = undefined): Observable<OperationDataResult<PlannedTaskDaysModel>> {
    return this.service.getPlannedTaskDays({propertyId: propertyId === undefined ? this.getPropertyId() : propertyId});
  }

  getAdHocTaskPriorities(propertyId: number | null = undefined): Observable<OperationDataResult<AdHocTaskPrioritiesModel>> {
    return this.service.getAdHocTaskPriorities({propertyId: propertyId === undefined ? this.getPropertyId() : propertyId});
  }

  getDocumentUpdatedDays(propertyId: number | null = undefined): Observable<OperationDataResult<DocumentUpdatedDaysModel>> {
    return this.service.getDocumentUpdatedDays({propertyId: propertyId === undefined ? this.getPropertyId() : propertyId});
  }

  getPlannedTaskWorkers(propertyId: number | null = undefined): Observable<OperationDataResult<PlannedTaskWorkers>> {
    return this.service.getPlannedTaskWorkers({propertyId: propertyId === undefined ? this.getPropertyId() : propertyId});
  }

  getAdHocTaskWorkers(propertyId: number | null = undefined): Observable<OperationDataResult<AdHocTaskWorkers>> {
    return this.service.getAdHocTaskWorkers({propertyId: propertyId === undefined ? this.getPropertyId() : propertyId});
  }

  getPropertyIdAsync(): Observable<number> {
    return this.query.selectPropertyId$;
  }

  getPropertyId(): number | null {
    return this.store.getValue().filters.propertyId;
  }

  updatePropertyId(propertyId: number | null) {
    this.store.update((state) => ({
      filters: {
        ...state.filters,
        ...{
          propertyId: propertyId
        }
      }
    }));
  }
}
