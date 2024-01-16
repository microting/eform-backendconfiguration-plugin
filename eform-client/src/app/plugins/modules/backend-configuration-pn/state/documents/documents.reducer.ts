import {DocumentsExpirationFilterEnum} from '../../enums';
import {CommonPaginationState} from 'src/app/common/models';
import {Action, createReducer, on} from '@ngrx/store';
import {
  updateDocumentsFilters, updateDocumentsPagination
} from './documents.actions';

export interface DocumentsFiltrationModel {
  propertyId: number,
  folderId?: string,
  documentId?: string,
  expiration?: DocumentsExpirationFilterEnum,
}

export interface DocumentsState {
  pagination: CommonPaginationState;
  filters: DocumentsFiltrationModel;
}

export const documentsInitialState: DocumentsState = {
  filters: {
    propertyId: -1,
    folderId: null,
    documentId: null,
    expiration: null,
  },
  pagination: {
    sort: 'Id',
    isSortDsc: false,
    offset: 0,
    pageSize: 10,
    total: 0,
    pageIndex: 0,
  }
};

const _documentsReducer = createReducer(
  documentsInitialState,
  on(updateDocumentsFilters, (state, {payload}) => ({
      ...state,
      filters: {...state.filters, ...payload,}
    }
  )),
  on(updateDocumentsPagination, (state, {payload}) => ({
      ...state,
      pagination: {...state.pagination, ...payload}
    }
  )),
);

export function documentsReducer(state: DocumentsState | undefined, action: Action) {
  return _documentsReducer(state, action);
}
