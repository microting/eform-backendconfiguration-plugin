import {Injectable} from '@angular/core';
import {Query} from '@datorama/akita';
import {TaskWizardState, TaskWizardStore} from './';

@Injectable({providedIn: 'root'})
export class TaskWizardQuery extends Query<TaskWizardState> {
  constructor(protected store: TaskWizardStore) {
    super(store);
  }

  get pageSetting() {
    return this.getValue();
  }

  selectActiveSort$ = this.select((state) => state.pagination.sort);
  selectActiveSortDirection$ = this.select((state) => state.pagination.isSortDsc ? 'desc' : 'asc');
  selectFilters$ = this.select((state) => state.filters);
}
