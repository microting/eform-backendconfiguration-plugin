import {createAction} from '@ngrx/store';

export const taskManagementUpdateFilters = createAction(
  '[TaskManagement] Update filters',
  (payload: any) => ({payload})
)

export const taskManagementUpdatePagination = createAction(
  '[TaskManagement] Update pagination',
  (payload: any) => ({payload})
)
