import {Injectable} from '@angular/core';
import {BackendConfigurationPnTaskTrackerService} from '../../../../services';
import {Store} from '@ngrx/store';
import {
  taskTrackerUpdateFilters,
  selectTaskTrackerFilters,
  TaskTrackerFiltrationModel
} from '../../../../state';

@Injectable({providedIn: 'root'})
export class TaskTrackerStateService {
  private selectTaskTrackerFilters$ = this.store.select(selectTaskTrackerFilters);
  currentFilters: TaskTrackerFiltrationModel;

  constructor(
    private store: Store,
    private service: BackendConfigurationPnTaskTrackerService,
  ) {
    this.selectTaskTrackerFilters$.subscribe(x => this.currentFilters = x);
  }

  getAllTasks() {
    return this.service.getTasks({
      ...this.currentFilters,
    });
  }

  updateFilters(taskManagementFiltrationModel: TaskTrackerFiltrationModel) {
    this.store.dispatch(taskTrackerUpdateFilters(taskManagementFiltrationModel));
  }
}
