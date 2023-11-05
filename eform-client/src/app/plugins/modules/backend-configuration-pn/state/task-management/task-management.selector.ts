import {
    BackendConfigurationState
} from '../backend-configuration.state';
import {createSelector} from '@ngrx/store';

export const selectBackendConfigurationPn =
    (state: {backendConfigurationPn: BackendConfigurationState}) => state.backendConfigurationPn;
export const selectTaskManagement =
    createSelector(selectBackendConfigurationPn, (state: BackendConfigurationState) => state.taskManagementState);
export const selectTaskManagementPagination =
    createSelector(selectTaskManagement, (state) => state.pagination);
export const selectTaskManagementPaginationSort =
    createSelector(selectTaskManagement, (state) => state.pagination.sort);
export const selectTaskManagementPaginationIsSortDsc =
    createSelector(selectTaskManagement, (state) => state.pagination.isSortDsc ? 'asc' : 'desc');
export const selectTaskManagementFilters =
    createSelector(selectTaskManagement, (state) => state.filters);
export const selectTaskManagementPropertyId =
    createSelector(selectTaskManagement, (state) => state.filters.propertyId);
export const selectTaskManagementPropertyIdIsNullOrUndefined =
    createSelector(selectTaskManagement, (state) => state.filters.propertyId === null || state.filters.propertyId === undefined);
