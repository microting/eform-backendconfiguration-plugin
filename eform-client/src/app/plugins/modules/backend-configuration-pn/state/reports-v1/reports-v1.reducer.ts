import {FiltrationStateModel} from 'src/app/common/models';
import {Action, createReducer, on} from '@ngrx/store';
import {
  reportsV1UpdateDateRange,
  reportsV1UpdateFilters,
  reportsV1UpdateScrollPosition
} from './reports-v1.actions';

export interface ReportStateV1 {
  filters: FiltrationStateModel;
  dateRange: {
    startDate: string,
    endDate: string,
  };
  scrollPosition: [number, number];
}

export const reportV1InitialState: ReportStateV1 = {
  filters: {
    tagIds: [],
    nameFilter: '',
  },
  dateRange: {
    startDate: null,
    endDate: null,
  },
  scrollPosition: [0, 0],
};

const _reportV1Reducer = createReducer(
  reportV1InitialState,
  on(reportsV1UpdateFilters, (state, {payload}) => ({
      ...state,
      filters: {
        ...state.filters,
        ...payload
      },
    }
  )),
  on(reportsV1UpdateDateRange, (state, {payload}) => ({
      ...state,
      dateRange: payload,
    }
  )),
  on(reportsV1UpdateScrollPosition, (state, {payload}) => ({
      ...state,
      scrollPosition: payload,
    }
  )),
);

export function reportV1Reducer(state: ReportStateV1 | undefined, action: Action) {
  return _reportV1Reducer(state, action);
}
