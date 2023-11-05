import {createAction} from '@ngrx/store';

export const taskTrackerUpdateFilters = createAction(
  '[TaskTracker] Update Filters',
  (payload) => ({ payload })
)
