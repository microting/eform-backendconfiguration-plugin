import {
  BackendConfigurationState,
} from '../';
import {createSelector} from '@ngrx/store';

const selectBackendConfigurationPn =
  (state: {backendConfigurationPn: BackendConfigurationState}) => state.backendConfigurationPn;
export const selectStatistics =
    createSelector(selectBackendConfigurationPn, (state) => state.statisticsState);
export const selectStatisticsPropertyId =
    createSelector(selectStatistics, (state) => state.filters.propertyId);
export const selectStatisticsFilters =
    createSelector(selectStatistics, (state) => state.filters);
