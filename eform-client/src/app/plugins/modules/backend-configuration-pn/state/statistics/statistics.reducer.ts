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

export const initialState: StatisticsState = {
  filters: {
    propertyId: null,
  }
}

export const _reducer = createReducer(
  initialState,
  on(statisticsUpdateFilters, (state, {payload}) => ({
    ...state,
    filters: {...payload},
    }
  )),
)

export function reducer(state: StatisticsState | undefined, action: any) {
  return _reducer(state, action);
}
