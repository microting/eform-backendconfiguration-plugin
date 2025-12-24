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

  getPlannedTaskDaysByFilters(
    model: PlannedTaskDaysRequestModel
  ): Observable<OperationDataResult<PlannedTaskDaysModel>> {

    const params: any = {};

    if (model.propertyIds?.length) {
      params.propertyIds = model.propertyIds;
    }
    if (model.tagIds?.length) {
      params.tagIds = model.tagIds;
    }
    if (model.workerIds?.length) {
      params.workerIds = model.workerIds;
    }
    if (model.statuses?.length) {
      params.statuses = model.statuses;
    }

    return this.apiBaseService.get(
      BackendConfigurationPnStatisticsMethods.PlannedTaskDays,
      params
    );
  }


  getAdHocTaskPriorities(model: StatisticsRequestModel): Observable<OperationDataResult<AdHocTaskPrioritiesModel>> {
    return this.apiBaseService.get(BackendConfigurationPnStatisticsMethods.AdHocTaskPriorities, model);
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
