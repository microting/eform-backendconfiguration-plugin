import {Injectable} from '@angular/core';
import {Observable} from 'rxjs';
import {
  OperationDataResult,
} from 'src/app/common/models';
import {BackendConfigurationPnTaskTrackerService} from '../../../../services';
import {TaskModel} from '../../../../models';
import {Store} from '@ngrx/store';
import {
  TaskTrackerFiltrationModel
} from '../../../../state/task-tracker/task-tracker.reducer';
import {
  selectTaskTrackerFilters
} from '../../../../state/task-tracker/task-tracker.selector';

@Injectable({providedIn: 'root'})
export class TaskTrackerStateService {
  private selectTaskTrackerFilters$ = this.store.select(selectTaskTrackerFilters);
  constructor(
      private store: Store,
    private service: BackendConfigurationPnTaskTrackerService,
  ) {
  }

  getAllTasks():
    Observable<OperationDataResult<TaskModel[]>> {
    let _filters: TaskTrackerFiltrationModel;
    this.selectTaskTrackerFilters$.subscribe((filters) => {
      _filters = filters;
    }).unsubscribe();
    return this.service.getTasks({
      ..._filters,
    });
    // return this.service
    //   .getTasks({
    //     ...this.query.pageSetting.filters,
    //   });
  }

  // getFiltersAsync(): Observable<TaskTrackerFiltrationModel> {
  //   return this.query.selectFilters$;
  // }

  updateFilters(taskManagementFiltrationModel: TaskTrackerFiltrationModel){
    this.store.dispatch({
      type: '[TaskTracker] Update Filters',
      payload: taskManagementFiltrationModel,
    })
    // this.store.update((state) => ({
    //   filters: {
    //     ...state.filters,
    //     propertyIds: taskManagementFiltrationModel.propertyIds,
    //     tagIds: taskManagementFiltrationModel.tagIds,
    //     workerIds: taskManagementFiltrationModel.workerIds,
    //   },
    // }));
  }
}
