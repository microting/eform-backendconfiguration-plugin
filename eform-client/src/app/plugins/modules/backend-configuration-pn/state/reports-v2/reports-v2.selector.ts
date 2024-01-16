import {
  BackendConfigurationState,
} from '../';
import {createSelector} from '@ngrx/store';

const selectBackendConfigurationPn =
  (state: {backendConfigurationPn: BackendConfigurationState}) => state.backendConfigurationPn;
export const selectReportsV2 =
    createSelector(selectBackendConfigurationPn, (state) => state.reportsV2State);
export const selectReportsV2Filters =
    createSelector(selectReportsV2, (state) => state.filters);
export const selectReportsV2FiltersTagIds =
  createSelector(selectReportsV2Filters, (state) => state.tagIds);
export const selectReportsV2DateRange =
    createSelector(selectReportsV2, (state) => state.dateRange);
export const selectReportsV2ScrollPosition =
    createSelector(selectReportsV2, (state) => state.scrollPosition);
export const selectReportsV2TagIds =
    createSelector(selectReportsV2, (state) => state.filters.tagIds);
