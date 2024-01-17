import {createAction} from '@ngrx/store';
import {CommonPaginationState} from 'src/app/common/models';
import {PropertyWorkersFiltrationModel} from './';

export const propertyWorkersUpdateFilters = createAction(
  '[PropertyWorkers] Update filters',
  (payload: PropertyWorkersFiltrationModel) => ({payload})
);

export const propertyWorkersUpdatePagination = createAction(
  '[PropertyWorkers] Update pagination',
  (payload: CommonPaginationState) => ({payload})
);
