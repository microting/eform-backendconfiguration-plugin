import {
  BackendConfigurationState,
} from '../';
import {createSelector} from '@ngrx/store';

const selectBackendConfigurationPn =
  (state: {backendConfigurationPn: BackendConfigurationState}) => state.backendConfigurationPn;
export const selectFiles =
    createSelector(selectBackendConfigurationPn, (state) => state.filesState);
export const selectFilesFilters =
    createSelector(selectFiles, (state) => state.filters);
export const selectFilesPagination =
    createSelector(selectFiles, (state) => state.pagination);
export const selectFilesPaginationSort =
    createSelector(selectFiles, (state) => state.pagination.sort);
export const selectFilesPaginationIsSortDsc =
    createSelector(selectFiles, (state) => state.pagination.isSortDsc ? 'desc' : 'asc');
export const selectFilesNameFilters =
    createSelector(selectFiles, (state) => state.filters.nameFilter);
export const selectFilesPaginationTotalItemsCount =
    createSelector(selectFiles, (state) => state.pagination.total);
