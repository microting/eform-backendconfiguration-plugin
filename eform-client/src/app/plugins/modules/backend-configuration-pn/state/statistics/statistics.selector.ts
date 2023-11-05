import {
    BackendConfigurationState
} from '../backend-configuration.state';
import {createSelector} from '@ngrx/store';

export const selectBackendConfigurationPn =
    (state: {backendConfigurationPn: BackendConfigurationState}) => state.backendConfigurationPn;
export const selectStatistics =
    createSelector(selectBackendConfigurationPn, (state: BackendConfigurationState) => state.statisticsState);
export const selectStatisticsPropertyId =
    createSelector(selectStatistics, (state) => state.filters.propertyId);
export const selectStatisticsFilters =
    createSelector(selectStatistics, (state) => state.filters);
