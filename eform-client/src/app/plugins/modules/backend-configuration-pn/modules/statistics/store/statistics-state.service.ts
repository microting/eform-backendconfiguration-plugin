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

  getPlannedTaskDays(): Observable<OperationDataResult<PlannedTaskDaysModel>> {
    return this.service.getPlannedTaskDays({propertyId: this.getPropertyId()});
  }

  getAdHocTaskPriorities(): Observable<OperationDataResult<AdHocTaskPrioritiesModel>> {
    return this.service.getAdHocTaskPriorities({propertyId: this.getPropertyId()});
  }

  getDocumentUpdatedDays(): Observable<OperationDataResult<DocumentUpdatedDaysModel>> {
    return this.service.getDocumentUpdatedDays({propertyId: this.getPropertyId()});
  }

  getPlannedTaskWorkers(): Observable<OperationDataResult<PlannedTaskWorkers>> {
    return this.service.getPlannedTaskWorkers({propertyId: this.getPropertyId()});
  }

  getAdHocTaskWorkers(): Observable<OperationDataResult<AdHocTaskWorkers>> {
    return this.service.getAdHocTaskWorkers({propertyId: this.getPropertyId()});
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
