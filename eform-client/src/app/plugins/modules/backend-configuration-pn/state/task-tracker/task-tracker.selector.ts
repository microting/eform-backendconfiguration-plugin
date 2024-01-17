import {
  BackendConfigurationState,
} from '../';
import {createSelector} from '@ngrx/store';

const selectBackendConfigurationPn =
  (state: {backendConfigurationPn: BackendConfigurationState}) => state.backendConfigurationPn;
export const selectTaskTracker =
    createSelector(selectBackendConfigurationPn, (state) => state.taskTrackerState);
export const selectTaskTrackerFilters =
    createSelector(selectTaskTracker, (state) => state.filters);
