import {
    BackendConfigurationState
} from '../backend-configuration.state';
import {createSelector} from '@ngrx/store';

export const selectBackendConfigurationPn =
    (state: {backendConfigurationPn: BackendConfigurationState}) => state.backendConfigurationPn;
export const selectReportsV1 =
    createSelector(selectBackendConfigurationPn, (state: BackendConfigurationState) => state.reportsV1State);
export const selectReportsV1Filters =
    createSelector(selectReportsV1, (state) => state.filters);
export const selectReportsV1FiltersTagIds =
    createSelector(selectReportsV1Filters, (state) => state.tagIds);
export const selectReportsV1DateRange =
    createSelector(selectReportsV1, (state) => state.dateRange);
export const selectReportsV1ScrollPosition =
    createSelector(selectReportsV1, (state) => state.scrollPosition);
export const selectReportsV1TagIds =
    createSelector(selectReportsV1, (state) => state.filters.tagIds);
