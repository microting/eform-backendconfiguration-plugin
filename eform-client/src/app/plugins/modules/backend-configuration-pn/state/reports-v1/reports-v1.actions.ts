import {createAction} from '@ngrx/store';

export const reportsV1UpdateFilters = createAction(
  '[ReportsV1] Update filters',
  (payload) => ({payload}),
)

export const reportsV1UpdateDateRange = createAction(
  '[ReportsV1] Update date range',
  (payload) => ({payload}),
)

export const reportsV1UpdateScrollPosition = createAction(
  '[ReportsV1] Update scroll position',
  (payload) => ({payload}),
)
