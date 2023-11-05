import {createAction} from '@ngrx/store';

export const reportsV2UpdateFilters = createAction(
  '[ReportsV2] Update filters',
  (payload) => ({payload}),
)

export const reportsV2UpdateDateRange = createAction(
  '[ReportsV2] Update date range',
  (payload) => ({payload}),
)

export const reportsV2UpdateScrollPosition = createAction(
  '[ReportsV2] Update scroll position',
  (payload) => ({payload}),
)
