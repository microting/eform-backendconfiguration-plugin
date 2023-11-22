import {FiltrationStateModel} from 'src/app/common/models';
import {createReducer, on} from '@ngrx/store';
import {
  reportsV2UpdateDateRange,
  reportsV2UpdateFilters, reportsV2UpdateScrollPosition
} from './reports-v2.actions';

export interface ReportStateV2 {
  filters: FiltrationStateModel;
  dateRange: {
    startDate: string,
    endDate: string,
  };
  scrollPosition: [number, number];
}

export const initialState: ReportStateV2 = {
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
  on(reportsV2UpdateFilters, (state, {payload}) => ({
    ...state,
      filters: payload.filters,
    }
  )),
  on(reportsV2UpdateDateRange, (state, {payload}) => ({
    ...state,
      dateRange: payload.dateRange,
    }
  )),
  on(reportsV2UpdateScrollPosition, (state, {payload}) => ({
    ...state,
      scrollPosition: payload.scrollPosition,
    }
  )),
)

export function reducer(state: ReportStateV2 | undefined, action: any) {
  return _reducer(state, action);
}
