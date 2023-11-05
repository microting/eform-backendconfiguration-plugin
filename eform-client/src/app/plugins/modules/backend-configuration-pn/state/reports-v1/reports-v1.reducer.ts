import {FiltrationStateModel} from 'src/app/common/models';
import {Action, createReducer, on} from '@ngrx/store';
import {
  reportsV1UpdateDateRange,
  reportsV1UpdateFilters, reportsV1UpdateScrollPosition
} from './reports-v1.actions';

export interface ReportStateV1 {
  filters: FiltrationStateModel;
  dateRange: {
    startDate: string,
    endDate: string,
  };
  scrollPosition: [number, number];
}

export const initialState: ReportStateV1 = {
  filters: {
    tagIds: [],
    nameFilter: '',
  },
  dateRange: {
    startDate: null,
    endDate: null,
  },
  scrollPosition: [0, 0],
}

export const _reducer = createReducer(
  initialState,
  on(reportsV1UpdateFilters, (state, {payload}) => ({
      ...state,
      filters: payload.filters,
    }
  )),
  on(reportsV1UpdateDateRange, (state, {payload}) => ({
      ...state,
      dateRange: payload.dateRange,
    }
  )),
  on(reportsV1UpdateScrollPosition, (state, {payload}) => ({
      ...state,
      scrollPosition: payload.scrollPosition,
    }
  )),
)

export function reducer(state: ReportStateV1 | undefined, action: Action) {
  return _reducer(state, action);
}
