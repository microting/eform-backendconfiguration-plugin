import {createAction} from '@ngrx/store';
import {TaskTrackerFiltrationModel} from './';

export const taskTrackerUpdateFilters = createAction(
  '[TaskTracker] Update filters',
  (payload: TaskTrackerFiltrationModel) => ({ payload })
)
