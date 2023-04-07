import {Injectable} from '@angular/core';
import {
  TaskTrackerStore,
  TaskTrackerQuery,
  TaskTrackerFiltrationModel,
} from './';
import {Observable} from 'rxjs';
import {
  OperationDataResult,
} from 'src/app/common/models';
import {BackendConfigurationPnTaskTrackerService} from '../../../../services';
import {TaskModel} from '../../../../models';

@Injectable({providedIn: 'root'})
export class TaskTrackerStateService {
  constructor(
    public store: TaskTrackerStore,
    private service: BackendConfigurationPnTaskTrackerService,
    private query: TaskTrackerQuery
  ) {
  }

  getAllTasks():
    Observable<OperationDataResult<TaskModel[]>> {
    return this.service
      .getTasks({
        ...this.query.pageSetting.filters,
      });
  }

  getFiltersAsync(): Observable<TaskTrackerFiltrationModel> {
    return this.query.selectFilters$;
  }

  updateFilters(taskManagementFiltrationModel: TaskTrackerFiltrationModel){
    this.store.update((state) => ({
      filters: {
        ...state.filters,
        propertyIds: taskManagementFiltrationModel.propertyIds,
        tags: taskManagementFiltrationModel.tags,
        workers: taskManagementFiltrationModel.workers,
      },
    }));
  }
}
