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

  selectFilters$ = this.select((state) => state.filters);
}
