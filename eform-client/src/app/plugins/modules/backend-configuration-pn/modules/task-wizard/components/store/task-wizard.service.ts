import {Injectable} from '@angular/core';
import {
  TaskWizardStore,
  TaskWizardQuery,
  TaskWizardFiltrationModel,
} from './';
import {Observable} from 'rxjs';
import {BackendConfigurationPnTaskWizardService} from '../../../../services';

@Injectable({providedIn: 'root'})
export class TaskWizardStateService {
  constructor(
    public store: TaskWizardStore,
    private service: BackendConfigurationPnTaskWizardService,
    private query: TaskWizardQuery
  ) {
  }

  getAllTasks() {
    return this.service
      .getTasks({
        ...this.query.pageSetting.filters,
      });
  }

  getFiltersAsync(): Observable<TaskWizardFiltrationModel> {
    return this.query.selectFilters$;
  }

  updateFilters(taskManagementFiltrationModel: TaskWizardFiltrationModel) {
    this.store.update((state) => ({
      filters: {
        ...state.filters,
        ...taskManagementFiltrationModel
      },
    }));
  }
}
