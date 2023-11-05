import {CommonPaginationState, FiltrationStateModel} from 'src/app/common/models';
import {Action, createReducer, on} from '@ngrx/store';
import {
  propertiesUpdateFilters, propertiesUpdatePagination, propertiesUpdateTotalProperties
} from './properties.actions';

export interface PropertiesState {
  pagination: CommonPaginationState;
  filters: FiltrationStateModel;
  totalProperties: number;
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
  totalProperties: 0,
}

export const _reducer = createReducer(
  initialState,
  on(propertiesUpdateFilters, (state, {payload}) => ({
    ...state,
    filters: {...state.filters, ...payload},
    }
  )
  ),
  on(propertiesUpdatePagination, (state, {payload}) => ({
    ...state,
    pagination: {...state.pagination, ...payload},
    }
  )
  ),
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
