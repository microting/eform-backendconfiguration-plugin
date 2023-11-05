import {createAction} from '@ngrx/store';

export const propertiesUpdateFilters = createAction(
  '[Properties] Update Filters',
  (payload) => ({payload})
);

export const propertiesUpdatePagination = createAction(
  '[Properties] Update Pagination',
  (payload) => ({payload})
);

export const propertiesUpdateTotalProperties = createAction(
  '[Properties] Update Total Properties',
  (payload) => ({payload})
);
