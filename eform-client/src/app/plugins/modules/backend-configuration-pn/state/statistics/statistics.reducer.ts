import {createReducer, on} from '@ngrx/store';
import {
  statisticsUpdateFilters
} from './statistics.actions';

export interface StatisticsModel {
  propertyId: number | null,
}

export interface StatisticsState {
  filters: StatisticsModel;
}

export const statisticsInitialState: StatisticsState = {
  filters: {
    propertyId: null,
  }
};

const _statisticsReducer = createReducer(
  statisticsInitialState,
  on(statisticsUpdateFilters, (state, {payload}) => ({
      ...state,
      filters: {...state.filters, ...payload},
    }
  )),
);

export function statisticsReducer(state: StatisticsState | undefined, action: any) {
  return _statisticsReducer(state, action);
}
