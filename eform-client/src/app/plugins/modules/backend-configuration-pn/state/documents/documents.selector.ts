import {
    BackendConfigurationState
} from '../backend-configuration.state';
import {createSelector} from '@ngrx/store';

export const selectBackendConfigurationPn =
    (state: {backendConfigurationPn: BackendConfigurationState}) => state.backendConfigurationPn;
export const selectDocuments =
    createSelector(selectBackendConfigurationPn, (state: BackendConfigurationState) => state.documentsState);
export const selectDocumentsFilters =
    createSelector(selectDocuments, (state) => state.filters);
export const selectDocumentsFiltersPropertyId =
    createSelector(selectDocuments, (state) => state.filters.propertyId);
export const selectDocumentsPagination =
    createSelector(selectDocuments, (state) => state.pagination);
export const selectDocumentsPaginationSort =
    createSelector(selectDocuments, (state) => state.pagination.sort);
export const selectDocumentsPaginationIsSortDsc =
    createSelector(selectDocuments, (state) => state.pagination.isSortDsc ? 'asc' : 'desc');
