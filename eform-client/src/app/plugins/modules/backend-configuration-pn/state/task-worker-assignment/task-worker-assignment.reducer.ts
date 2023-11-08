import {CommonPaginationState} from 'src/app/common/models';
import {Action, createReducer, on} from '@ngrx/store';
import {
  taskWorkerAssignmentUpdatePagination, taskWorkerAssignmentUpdateTotalProperties
} from './task-worker-assignment.actions';

export interface TaskWorkerAssignmentState {
  pagination: CommonPaginationState;
  // filters: FiltrationStateModel;
  totalProperties: number;
}

export const initialState: TaskWorkerAssignmentState = {
  pagination: {
    pageSize: 10,
    sort: 'Id',
    isSortDsc: false,
    offset: 0,
    pageIndex: 0,
    total: 0,
  },
  totalProperties: 0,
}

export const _reducer = createReducer(
  initialState,
  on(taskWorkerAssignmentUpdatePagination, (state, {payload}) => ({
    ...state,
      pagination: {...state.pagination, ...payload},
    }
  )),
  on(taskWorkerAssignmentUpdateTotalProperties, (state, {payload}) => ({
    ...state,
      payload,
    }
  )),
);

export function reducer(state: TaskWorkerAssignmentState | undefined, action: Action) {
  return _reducer(state, action);
}
