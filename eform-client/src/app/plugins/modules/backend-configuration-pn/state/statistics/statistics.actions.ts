import {createAction} from '@ngrx/store';

export const statisticsUpdateFilters = createAction(
  '[Statistics] Update filters',
  (payload: any) => ({payload})
);
