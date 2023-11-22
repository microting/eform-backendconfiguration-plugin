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

export const initialState: DocumentsState = {
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
}

export const _reducer = createReducer(
  initialState,
  on(updateDocumentsFilters, (state, {payload}) => ({
    ...state,
    filters: {
      propertyId: payload.filters.propertyId,
      folderId: payload.filters.folderId,
      documentId: payload.filters.documentId,
      expiration: payload.filters.expiration,
    }
    }
  )),
  on(updateDocumentsPagination, (state, {payload}) => ({
    ...state,
    pagination: {...state.pagination, ...payload}
    }
  )),
);

export function reducer(state: DocumentsState | undefined, action: Action) {
  return _reducer(state, action);
}
