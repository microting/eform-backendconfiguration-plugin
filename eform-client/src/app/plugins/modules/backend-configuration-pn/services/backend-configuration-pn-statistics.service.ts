import {Injectable} from '@angular/core';
import {ApiBaseService} from 'src/app/common/services';
import {Observable} from 'rxjs';
import {OperationDataResult} from 'src/app/common/models';
import {
  StatisticsRequestModel,
  PlannedTaskDaysModel,
  AdHocTaskPrioritiesModel,
  DocumentUpdatedDaysModel,
  PlannedTaskWorkers,
  AdHocTaskWorkers, PlannedTaskDaysRequestModel,
} from '../models';

export let BackendConfigurationPnStatisticsMethods = {
  PlannedTaskDays: 'api/backend-configuration-pn/stats/planned-task-days',
  AdHocTaskPriorities: 'api/backend-configuration-pn/stats/ad-hoc-task-priorities',
  DocumentUpdatedDays: 'api/backend-configuration-pn/stats/document-updated-days',
  PlannedTaskWorkers: 'api/backend-configuration-pn/stats/planned-task-workers',
  AdHocTaskWorkers: 'api/backend-configuration-pn/stats/ad-hoc-task-workers',
};

@Injectable({
  providedIn: 'root',
})
export class BackendConfigurationPnStatisticsService {
  constructor(private apiBaseService: ApiBaseService) {
  }

  getPlannedTaskDays(model: StatisticsRequestModel): Observable<OperationDataResult<PlannedTaskDaysModel>> {
    return this.apiBaseService.get(BackendConfigurationPnStatisticsMethods.PlannedTaskDays, model);
  }

  buildParams<T extends Record<string, any>>(model: T): Partial<T> {
    return Object.fromEntries(
      Object.entries(model).filter(
        ([_, v]) => Array.isArray(v) ? v.length > 0 : v != null
      )
    ) as Partial<T>;
  }

  getPlannedTaskDaysByFilters(
    model: PlannedTaskDaysRequestModel
  ): Observable<OperationDataResult<PlannedTaskDaysModel>> {

    const params = this.buildParams(model);

    return this.apiBaseService.get(
      BackendConfigurationPnStatisticsMethods.PlannedTaskDays,
      params
    );
  }

  getPlannedTaskWorkersByFilters(model: {
    propertyIds: number[];
    status: number[];
    folderIds: number[];
    tagIds: number[];
    assignToIds: number[];
  }): Observable<OperationDataResult<PlannedTaskWorkers>> {
    const params = this.buildParams(model);

    return this.apiBaseService.get(
      BackendConfigurationPnStatisticsMethods.PlannedTaskWorkers,
      params
    );
  }


  getAdHocTaskPriorities(model: StatisticsRequestModel): Observable<OperationDataResult<AdHocTaskPrioritiesModel>> {
    return this.apiBaseService.get(BackendConfigurationPnStatisticsMethods.AdHocTaskPriorities, model);
  }

  getAdHocTaskPrioritiesByFilters(model: {
    propertyId?: number | null;
    propertyIds?: number[];
    statuses?: number[];
    priority?: number | null;
    lastAssignedTo?: number | null;
    dateFrom?: string | Date | null;
    dateTo?: string | Date | null;
  }) : Observable<OperationDataResult<AdHocTaskPrioritiesModel>> {
    return this.apiBaseService.get(BackendConfigurationPnStatisticsMethods.AdHocTaskPriorities, model);
  }

  getAdHocTaskWorkersByFilters(model: {
    propertyId: number[];
    areaName: number[];
    createdBy: number[];
    lastAssignedTo: number[];
    statuses: number[];
    priority: number[];
    dateFrom: number[];
    dateTo: number[];
  }): Observable<OperationDataResult<PlannedTaskWorkers>> {
    const params = this.buildParams(model);

    return this.apiBaseService.get(
      BackendConfigurationPnStatisticsMethods.AdHocTaskWorkers,
      params
    );
  }

  getDocumentUpdatedDays(model: StatisticsRequestModel): Observable<OperationDataResult<DocumentUpdatedDaysModel>> {
    return this.apiBaseService.get(BackendConfigurationPnStatisticsMethods.DocumentUpdatedDays, model);
  }

  getPlannedTaskWorkers(model: StatisticsRequestModel): Observable<OperationDataResult<PlannedTaskWorkers>> {
    return this.apiBaseService.get(BackendConfigurationPnStatisticsMethods.PlannedTaskWorkers, model);
  }

  getAdHocTaskWorkers(model: StatisticsRequestModel): Observable<OperationDataResult<AdHocTaskWorkers>> {
    return this.apiBaseService.get(BackendConfigurationPnStatisticsMethods.AdHocTaskWorkers, model);
  }
}
