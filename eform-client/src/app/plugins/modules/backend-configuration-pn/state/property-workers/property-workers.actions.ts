import {createAction} from '@ngrx/store';

export const propertyWorkersUpdateFilters = createAction(
  '[PropertyWorkers] Update filters',
  (payload: any) => ({payload})
);

export const propertyWorkersUpdatePagination = createAction(
  '[PropertyWorkers] Update pagination',
  (pagination: any) => ({pagination})
);
