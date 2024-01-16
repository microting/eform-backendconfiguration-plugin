import {createAction} from '@ngrx/store';
import {CommonPaginationState} from 'src/app/common/models';
import {PropertiesFiltrationModel} from './';

export const propertiesUpdateFilters = createAction(
  '[Properties] Update Filters',
  (payload: PropertiesFiltrationModel) => ({payload})
);

export const propertiesUpdatePagination = createAction(
  '[Properties] Update Pagination',
  (payload: CommonPaginationState) => ({payload})
);

export const propertiesUpdateTotalProperties = createAction(
  '[Properties] Update Total Properties',
  (payload: number) => ({payload})
);
