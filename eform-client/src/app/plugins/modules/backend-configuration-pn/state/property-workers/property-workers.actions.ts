import {createAction} from '@ngrx/store';

export const propertyWorkersUpdateFilters = createAction(
  '[PropertyWorkers] UpdateFilters',
  (filters: any) => ({filters})
);

export const propertyWorkersUpdatePagination = createAction(
  '[PropertyWorkers] UpdatePagination',
  (pagination: any) => ({pagination})
);
