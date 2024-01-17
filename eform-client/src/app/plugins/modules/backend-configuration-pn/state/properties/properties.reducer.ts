import {CommonPaginationState} from 'src/app/common/models';
import {Action, createReducer, on} from '@ngrx/store';
import {
  propertiesUpdateFilters,
  propertiesUpdatePagination,
  propertiesUpdateTotalProperties
} from './properties.actions';

export interface PropertiesFiltrationModel {
  nameFilter: string;
}

export interface PropertiesState {
  pagination: CommonPaginationState;
  filters: PropertiesFiltrationModel;
  total: number;
}

export const propertiesInitialState: PropertiesState = {
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
  },
  total: 0,
};

export const _propertiesReducer = createReducer(
  propertiesInitialState,
  on(propertiesUpdateFilters, (state, {payload}) => ({
    ...state,
    filters: {
      ...state.filters,
      ...payload,
    },
  })),
  on(propertiesUpdatePagination, (state, {payload}) => ({
    ...state,
    pagination: { ...state, ...payload, },
  })),
  on(propertiesUpdateTotalProperties, (state, {payload}) => ({
      ...state,
      pagination: {
        ...state.pagination,
        total: payload,
      },
      total: payload,
    }
  )),
);

export function propertiesReducer(state: PropertiesState | undefined, action: Action) {
  return _propertiesReducer(state, action);
}
