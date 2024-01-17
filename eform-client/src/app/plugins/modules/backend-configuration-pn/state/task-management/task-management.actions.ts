import {createAction} from '@ngrx/store';
import {TaskManagementFiltrationModel} from './';
import {CommonPaginationState} from 'src/app/common/models';

export const taskManagementUpdateFilters = createAction(
  '[TaskManagement] Update filters',
  (payload: TaskManagementFiltrationModel) => ({payload})
)

export const taskManagementUpdatePagination = createAction(
  '[TaskManagement] Update pagination',
  (payload: CommonPaginationState) => ({payload})
)
