import {createAction} from '@ngrx/store';

export const filesUpdateDateRange = createAction(
  '[Files] Update Date Range',
  (payload) => ({payload})
);

export const filesUpdatePropertyIds = createAction(
  '[Files] Update Property Ids',
  (payload) => ({payload})
);

export const filesUpdateFilters = createAction(
  '[Files] Update Filters',
  (payload) => ({payload})
);

export const filesUpdatePagination = createAction(
  '[Files] Update Pagination',
  (payload) => ({payload})
);

export const filesUpdatePaginationTotalItemsCount = createAction(
    '[Files] Update Pagination Total Items Count',
    (payload) => ({payload})
);
