import {createReducer, on} from '@ngrx/store';
import {
  taskTrackerUpdateFilters
} from './task-tracker.actions';

export interface TaskTrackerFiltrationModel {
  propertyIds: number[];
  tagIds: number[];
  workerIds: number[];
}

export interface TaskTrackerState {
  filters: TaskTrackerFiltrationModel;
}

export const initialState: TaskTrackerState = {
  filters: {
    propertyIds: [],
    tagIds: [],
    workerIds: [],
  }
}

export const _reducer = createReducer(
  initialState,
  on(taskTrackerUpdateFilters, (state, {payload}) => ({
      ...state,
      filters: {
        ...state.filters,
        ...payload,
      },
    }
  ))
)

export function reducer(state: TaskTrackerState | undefined, action: any) {
  return _reducer(state, action);
}
