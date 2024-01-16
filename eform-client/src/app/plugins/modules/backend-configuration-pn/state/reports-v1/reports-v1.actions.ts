import {createAction} from '@ngrx/store';
import {FiltrationStateModel} from 'src/app/common/models';

export const reportsV1UpdateFilters = createAction(
  '[ReportsV1] Update filters',
  (payload: FiltrationStateModel) => ({payload}),
);

export const reportsV1UpdateDateRange = createAction(
  '[ReportsV1] Update date range',
  (payload: { startDate: string, endDate: string }) => ({payload}),
);

export const reportsV1UpdateScrollPosition = createAction(
  '[ReportsV1] Update scroll position',
  (payload: [number, number]) => ({payload}),
);
