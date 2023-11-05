import {
    BackendConfigurationState
} from '../backend-configuration.state';
import {createSelector} from '@ngrx/store';

export const selectBackendConfigurationPn =
    (state: {backendConfigurationPn: BackendConfigurationState}) => state.backendConfigurationPn;
export const selectProperties =
    createSelector(selectBackendConfigurationPn, (state: BackendConfigurationState) => state.propertiesState);
export const selectPropertiesFilters =
    createSelector(selectProperties, (state) => state.filters);
export const selectPropertiesPagination =
    createSelector(selectProperties, (state) => state.pagination);
export const selectPropertiesPaginationSort =
    createSelector(selectProperties, (state) => state.pagination.sort);
export const selectPropertiesPaginationIsSortDsc =
    createSelector(selectProperties, (state) => state.pagination.isSortDsc ? 'asc' : 'desc');
export const selectPropertiesNameFilters =
    createSelector(selectProperties, (state) => state.filters.nameFilter);
export const selectPropertiesPaginationPageSize =
    createSelector(selectProperties, (state) => state.pagination.pageSize);
export const selectPropertiesTotal =
    createSelector(selectProperties, (state) => state.totalProperties);
