import {CommonPaginationState} from 'src/app/common/models';
import {Action, createReducer, on} from '@ngrx/store';
import {
  taskManagementUpdateFilters, taskManagementUpdatePagination
} from "src/app/plugins/modules/backend-configuration-pn/state/task-management/task-management.actions";

export interface TaskManagementFiltrationModel {
  propertyId: number;
  areaName?: string;
  createdBy?: string;
  lastAssignedTo?: number;
  status?: number;
  dateFrom?: Date | string;
  dateTo?: Date | string;
  priority?: number;
  delayed: boolean;
}

export interface TaskManagementState {
  pagination: CommonPaginationState;
  filters: TaskManagementFiltrationModel;
  // total: number;
}

export const initialState: TaskManagementState = {
  pagination: {
    pageSize: 10,
    sort: 'Id',
    isSortDsc: false,
    offset: 0,
    pageIndex: 0,
    total: 0,
  },
  filters: {
    propertyId: null,
    status: null,
    areaName: null,
    createdBy: null,
    dateFrom: null,
    dateTo: null,
    delayed: null,
    lastAssignedTo: null,
    priority: null,
  },
}

export const _reducer = createReducer(
  initialState,
  on(taskManagementUpdateFilters, (state, {payload}) => ({
    ...state,
    filters: payload,
    }
  )),
  on(taskManagementUpdatePagination, (state, {payload}) => ({
    ...state,
    pagination: payload,
    }
  )),
);

export function reducer(state: TaskManagementState | undefined, action: Action) {
  return _reducer(state, action);
}
