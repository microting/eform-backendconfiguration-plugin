import {createAction} from '@ngrx/store';

export const updateDocumentsFilters = createAction(
    '[Documents] Update filters',
  (payload) => ({ payload })
);

export const updateDocumentsPagination = createAction(
    '[Documents] Update pagination',
  (payload) => ({ payload })
);
