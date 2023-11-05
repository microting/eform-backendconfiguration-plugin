import {CommonPaginationState, FiltrationStateModel} from 'src/app/common/models';
import {Action, createReducer, on} from '@ngrx/store';
import {
  filesUpdateDateRange, filesUpdateFilters, filesUpdatePagination, filesUpdatePaginationTotalItemsCount,
  filesUpdatePropertyIds
} from "src/app/plugins/modules/backend-configuration-pn/state/files/files.asctions";

export interface FilesFiltrationModel extends FiltrationStateModel{
  propertyIds: number[],
  dateRange: {
    dateFrom: string,
    dateTo: string,
  },
}

export interface FilesState {
  pagination: CommonPaginationState;
  filters: FilesFiltrationModel;
  total: number;
}

export const initialState: FilesState = {
  pagination: {
    sort: 'Id',
    isSortDsc: false,
    offset: 0,
    pageSize: 10,
    pageIndex: 0,
    total: 0,
  },
  filters: {
    propertyIds: [],
    nameFilter: '',
    dateRange: {
      dateFrom: '',
      dateTo: '',
    },
    tagIds: [],
  },
  total: 0,
}

export const _reducer = createReducer(
  initialState,
  on(filesUpdateDateRange, (state, {payload}) => ({
    ...state,
    filters: {
      ...state.filters,
      dateRange: payload,
    },
    }
  )),
  on(filesUpdatePropertyIds, (state, {payload}) => ({
    ...state,
    filters: {
      ...state.filters,
      propertyIds: payload,
    },
    }
  )),
  on(filesUpdatePagination, (state, {payload}) => ({
    ...state,
    pagination: payload,
    }
  )
  ),
  on(filesUpdateFilters, (state, {payload}) => ({
    ...state,
    filters: payload,
    }
  )
  ),
    on(filesUpdatePaginationTotalItemsCount, (state, {payload}) => ({
        ...state,
        pagination: {
          ...state.pagination,
          total: payload,
        },
        }
    )
    ),
);

export function reducer(state: FilesState | undefined, action: Action) {
  return _reducer(state, action);
}
