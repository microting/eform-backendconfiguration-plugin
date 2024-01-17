import {FiltrationStateModel} from 'src/app/common/models';
import {createReducer, on} from '@ngrx/store';
import {
  reportsV2UpdateDateRange,
  reportsV2UpdateFilters,
  reportsV2UpdateScrollPosition
} from './reports-v2.actions';

export interface ReportStateV2 {
  filters: FiltrationStateModel;
  dateRange: {
    startDate: string,
    endDate: string,
  };
  scrollPosition: [number, number];
}

export const reportV2InitialState: ReportStateV2 = {
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

const _reportV2Reducer = createReducer(
  reportV2InitialState,
  on(reportsV2UpdateFilters, (state, {payload}) => ({
      ...state,
      filters: {
        ...state.filters,
        ...payload
      },
    }
  )),
  on(reportsV2UpdateDateRange, (state, {payload}) => ({
      ...state,
      dateRange: payload,
    }
  )),
  on(reportsV2UpdateScrollPosition, (state, {payload}) => ({
      ...state,
      scrollPosition: payload,
    }
  )),
);

export function reportV2Reducer(state: ReportStateV2 | undefined, action: any) {
  return _reportV2Reducer(state, action);
}
