import {createAction} from '@ngrx/store';
import {DocumentsFiltrationModel} from './';
import {CommonPaginationState} from 'src/app/common/models';

export const updateDocumentsFilters = createAction(
    '[Documents] Update filters',
  (payload: DocumentsFiltrationModel) => ({ payload })
);

export const updateDocumentsPagination = createAction(
    '[Documents] Update pagination',
  (payload: CommonPaginationState) => ({ payload })
);
