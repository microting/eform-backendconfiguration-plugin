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

export const taskTrackerInitialState: TaskTrackerState = {
  filters: {
    propertyIds: [],
    tagIds: [],
    workerIds: [],
  }
};

const _taskTrackerReducer = createReducer(
  taskTrackerInitialState,
  on(taskTrackerUpdateFilters, (state, {payload}) => ({
      ...state,
      filters: {...state.filters, ...payload,},
    }
  ))
);

export function taskTrackerReducer(state: TaskTrackerState | undefined, action: any) {
  return _taskTrackerReducer(state, action);
}
