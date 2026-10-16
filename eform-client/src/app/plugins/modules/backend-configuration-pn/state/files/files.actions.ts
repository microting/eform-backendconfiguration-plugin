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

/** Lists one folder; clears the search and "Dato skal tjekkes", which both span all folders. */
export const filesSelectFolder = createAction(
  '[Files] Select Folder',
  (payload: number) => ({payload})
);

/** "Dato skal tjekkes": documents whose date is only suggested, across all folders. */
export const filesToggleDateCheck = createAction(
  '[Files] Toggle Date Check',
  (payload: boolean) => ({payload})
);
