import {
  BackendConfigurationState,
} from '../';
import {createSelector} from '@ngrx/store';

const selectBackendConfigurationPn =
  (state: {backendConfigurationPn: BackendConfigurationState}) => state.backendConfigurationPn;
export const selectAdhoc =
    createSelector(selectBackendConfigurationPn, (state) => state.adhocState);
export const selectAdhocPagination =
    createSelector(selectAdhoc, (state) => state.pagination);
export const selectAdhocPaginationSort =
    createSelector(selectAdhoc, (state) => state.pagination.sort);
export const selectAdhocPaginationIsSortDsc =
    createSelector(selectAdhoc, (state) => state.pagination.isSortDsc ? 'desc' : 'asc');
export const selectAdhocFilters =
    createSelector(selectAdhoc, (state) => state.filters);
export const selectAdhocStatus =
    createSelector(selectAdhoc, (state) => state.filters.status);
export const selectAdhocHiddenColumns =
    createSelector(selectAdhoc, (state) => state.hiddenColumns);
export const selectAdhocHistoryFilters =
    createSelector(selectAdhoc, (state) => state.historyFilters);
