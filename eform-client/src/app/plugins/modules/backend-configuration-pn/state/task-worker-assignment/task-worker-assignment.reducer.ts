import {CommonPaginationState} from 'src/app/common/models';
import {Action, createReducer, on} from '@ngrx/store';
import {
  taskWorkerAssignmentUpdatePagination, taskWorkerAssignmentUpdateTotalProperties
} from './task-worker-assignment.actions';

export interface TaskWorkerAssignmentsState {
  pagination: CommonPaginationState;
  // filters: FiltrationStateModel;
  totalProperties: number;
}

export const initialState: TaskWorkerAssignmentsState = {
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
  on(taskWorkerAssignmentUpdatePagination, (state, {pagination}) => ({
    ...state,
    pagination: {...state.pagination, ...pagination},
    }
  )),
  on(taskWorkerAssignmentUpdateTotalProperties, (state, {totalProperties}) => ({
    ...state,
    totalProperties,
    }
  )),
);

export function reducer(state: TaskWorkerAssignmentsState | undefined, action: Action) {
  return _reducer(state, action);
}
