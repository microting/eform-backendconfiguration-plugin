import {CommonPaginationState} from 'src/app/common/models';
import {Action, createReducer, on} from '@ngrx/store';
import {
  taskWorkerAssignmentUpdatePagination,
  taskWorkerAssignmentUpdateTotalProperties
} from './task-worker-assignment.actions';

export interface TaskWorkerAssignmentState {
  pagination: CommonPaginationState;
  // filters: FiltrationStateModel;
  total: number;
}

export const taskWorkerAssignmentInitialState: TaskWorkerAssignmentState = {
  pagination: {
    pageSize: 10,
    sort: 'Id',
    isSortDsc: false,
    offset: 0,
    pageIndex: 0,
    total: 0,
  },
  total: 0,
};

const _taskWorkerAssignmentReducer = createReducer(
  taskWorkerAssignmentInitialState,
  on(taskWorkerAssignmentUpdatePagination, (state, {payload}) => ({
      ...state,
      pagination: {...state.pagination, ...payload},
    }
  )),
  on(taskWorkerAssignmentUpdateTotalProperties, (state, {payload}) => ({
      ...state,
      pagination: {
        ...state.pagination,
        total: payload
      },
      total: payload,
    }
  )),
);

export function taskWorkerAssignmentReducer(state: TaskWorkerAssignmentState | undefined, action: Action) {
  return _taskWorkerAssignmentReducer(state, action);
}
