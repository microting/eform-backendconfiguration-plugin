import {CommonPaginationState, FiltrationStateModel} from 'src/app/common/models';
import {Action, createReducer, on} from '@ngrx/store';
import {
  propertyWorkersUpdateFilters,
  propertyWorkersUpdatePagination
} from './property-workers.actions';

export interface PropertyWorkersFiltrationModel extends FiltrationStateModel {
  propertyIds: number[];
  showResigned: boolean;
}

export interface PropertyWorkersState {
  pagination: CommonPaginationState;
  filters: PropertyWorkersFiltrationModel;
}

export const propertyWorkersInitialState: PropertyWorkersState = {
  pagination: {
    sort: 'MicrotingUid',
    isSortDsc: false,
    offset: 0,
    pageSize: 10,
    pageIndex: 0,
    total: 0,
  },
  filters: {
    propertyIds: [],
    nameFilter: '',
    tagIds: [],
    showResigned: false,
  }
};

const _propertyWorkersReducer = createReducer(
  propertyWorkersInitialState,
  on(propertyWorkersUpdateFilters, (state, {payload}) => ({
      ...state,
      filters: {
        ...state.filters,
        ...payload,
      }
    }
  )),
  on(propertyWorkersUpdatePagination, (state, {payload}) => ({
      ...state,
      pagination: {...state.pagination, ...payload}
    }
  ))
);

export function propertyWorkersReducer(state: PropertyWorkersState | undefined, action: Action) {
  return _propertyWorkersReducer(state, action);
}
