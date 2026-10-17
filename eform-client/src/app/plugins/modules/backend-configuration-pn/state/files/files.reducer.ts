import {CommonPaginationState, FiltrationStateModel} from 'src/app/common/models';
import {Action, createReducer, on} from '@ngrx/store';
import {
  filesUpdateDateRange,
  filesUpdateFilters,
  filesUpdatePagination,
  filesUpdatePaginationTotalItemsCount,
  filesUpdatePropertyIds,
  filesSelectFolder,
  filesToggleDateCheck
} from './files.actions';

export interface FilesFiltrationModel extends FiltrationStateModel {
  propertyIds: number[],
  dateRange: {
    dateFrom: string,
    dateTo: string,
  },
  /** The folder the table lists; not sent while a search or the date check spans all folders. */
  folderId: number | null,
  /** "Dato skal tjekkes" chip. */
  needsDateCheck: boolean,
}

export interface FilesState {
  pagination: CommonPaginationState;
  filters: FilesFiltrationModel;
  total: number;
}

export const filesInitialState: FilesState = {
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
    folderId: null,
    needsDateCheck: false,
  },
  total: 0,
};

const _filesReducer = createReducer(
  filesInitialState,
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
  )),
  on(filesUpdateFilters, (state, {payload}) => ({
      ...state,
      filters: payload,
    }
  )),
  on(filesUpdatePaginationTotalItemsCount, (state, {payload}) => ({
      ...state,
      pagination: {
        ...state.pagination,
        total: payload,
      },
    }
  )),
  on(filesSelectFolder, (state, {payload}) => ({
      ...state,
      filters: {
        ...state.filters,
        folderId: payload,
        needsDateCheck: false,
        nameFilter: '',
      },
    }
  )),
  on(filesToggleDateCheck, (state, {payload}) => ({
      ...state,
      filters: {
        ...state.filters,
        needsDateCheck: payload,
      },
    }
  )),
);

export function filesReducer(state: FilesState | undefined, action: Action) {
  return _filesReducer(state, action);
}
