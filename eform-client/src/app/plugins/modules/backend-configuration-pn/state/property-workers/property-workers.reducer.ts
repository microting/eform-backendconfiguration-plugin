import {CommonPaginationState, FiltrationStateModel} from 'src/app/common/models';
import {Action, createReducer, on} from '@ngrx/store';
import {
  propertyWorkersUpdateFilters, propertyWorkersUpdatePagination
} from './property-workers.actions';

export interface PropertyWorkersFiltrationModel extends FiltrationStateModel {
  propertyIds: number[];
}

export interface PropertyWorkersState {
  pagination: CommonPaginationState;
  filters: PropertyWorkersFiltrationModel;
}

export const initialState: PropertyWorkersState = {
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
  }
};

export const _reducer = createReducer(
  initialState,
  on(propertyWorkersUpdateFilters, (state, {filters}) => ({
    ...state,
    filters: {...state.filters, ...filters}
    }
  )),
  on(propertyWorkersUpdatePagination, (state, {pagination}) => ({
    ...state,
    pagination: {...state.pagination, ...pagination}
    }
  ))
);

export function reducer(state: PropertyWorkersState | undefined, action: Action) {
  return _reducer(state, action);
}
