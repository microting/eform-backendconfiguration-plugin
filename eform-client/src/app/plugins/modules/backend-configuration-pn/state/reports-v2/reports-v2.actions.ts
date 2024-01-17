import {createAction} from '@ngrx/store';
import {FiltrationStateModel} from 'src/app/common/models';

export const reportsV2UpdateFilters = createAction(
  '[ReportsV2] Update filters',
  (payload: FiltrationStateModel) => ({payload}),
);

export const reportsV2UpdateDateRange = createAction(
  '[ReportsV2] Update date range',
  (payload: { startDate: string, endDate: string }) => ({payload}),
);

export const reportsV2UpdateScrollPosition = createAction(
  '[ReportsV2] Update scroll position',
  (payload: [number, number]) => ({payload}),
);
