import {createAction} from '@ngrx/store';

export const updateDocumentsFilters = createAction(
    '[Documents] Update Documents Filters',
  (payload) => ({ payload })
);

export const updateDocumentsPagination = createAction(
    '[Documents] Update Documents Pagination',
  (payload) => ({ payload })
);
