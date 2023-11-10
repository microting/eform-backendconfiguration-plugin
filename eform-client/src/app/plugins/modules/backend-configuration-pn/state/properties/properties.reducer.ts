import {CommonPaginationState, FiltrationStateModel} from 'src/app/common/models';
import {Action, createReducer, on} from '@ngrx/store';
import {
  propertiesUpdateFilters, propertiesUpdatePagination, propertiesUpdateTotalProperties
} from './properties.actions';

export interface PropertiesState {
  pagination: CommonPaginationState;
  filters: FiltrationStateModel;
  total: number;
}

export const initialState: PropertiesState = {
  pagination: {
    pageSize: 10,
    sort: 'Id',
    isSortDsc: false,
    offset: 0,
    pageIndex: 0,
    total: 0,
  },
  filters: {
    nameFilter: '',
    tagIds: [],
  },
  total: 0,
}

export const _reducer = createReducer(
  initialState,
  on(propertiesUpdateFilters, (state, {payload}) => ({
    ...state,
    filters: {
      nameFilter: payload.filters.nameFilter,
      tagIds: payload.filters.tagIds,
    },
  })),
  on(propertiesUpdatePagination, (state, {payload}) => ({
    ...state,
    pagination: {
      offset: payload.pagination.offset,
      pageSize: payload.pagination.pageSize,
      pageIndex: payload.pagination.pageIndex,
      sort: payload.pagination.sort,
      isSortDsc: payload.pagination.isSortDsc,
      total: payload.pagination.total,
    },
  })),
  on(propertiesUpdateTotalProperties, (state, {payload}) => ({
    ...state,
    totalProperties: payload,
    }
  )
  ),
);

export function reducer(state: PropertiesState | undefined, action: Action) {
  return _reducer(state, action);
}
