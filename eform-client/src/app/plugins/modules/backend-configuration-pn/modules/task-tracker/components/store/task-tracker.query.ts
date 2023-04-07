import { Injectable } from '@angular/core';
import { Query } from '@datorama/akita';
import { TaskTrackerState, TaskTrackerStore } from './';

@Injectable({ providedIn: 'root' })
export class TaskTrackerQuery extends Query<TaskTrackerState> {
  constructor(protected store: TaskTrackerStore) {
    super(store);
  }

  get pageSetting() {
    return this.getValue();
  }

  selectFilters$ = this.select((state) => state.filters);
}
