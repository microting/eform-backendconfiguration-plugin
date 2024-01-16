import {createAction} from '@ngrx/store';
import {CommonPaginationState} from 'src/app/common/models';
import {FilesFiltrationModel} from './';

export const filesUpdateDateRange = createAction(
  '[Files] Update Date Range',
  (payload: { dateFrom: '', dateTo: '', }) => ({payload})
);

export const filesUpdatePropertyIds = createAction(
  '[Files] Update Property Ids',
  (payload: number[]) => ({payload})
);

export const filesUpdateFilters = createAction(
  '[Files] Update Filters',
  (payload: FilesFiltrationModel) => ({payload})
);

export const filesUpdatePagination = createAction(
  '[Files] Update Pagination',
  (payload: CommonPaginationState) => ({payload})
);

export const filesUpdatePaginationTotalItemsCount = createAction(
  '[Files] Update Pagination Total Items Count',
  (payload: number) => ({payload})
);
