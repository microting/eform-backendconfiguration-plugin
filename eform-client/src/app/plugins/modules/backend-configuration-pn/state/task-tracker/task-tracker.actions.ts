import {createAction} from '@ngrx/store';

export const taskTrackerUpdateFilters = createAction(
  '[TaskTracker] Update filters',
  (payload) => ({ payload })
)
