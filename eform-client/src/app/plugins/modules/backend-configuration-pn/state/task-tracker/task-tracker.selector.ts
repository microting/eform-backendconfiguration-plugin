import {
    BackendConfigurationState
} from '../backend-configuration.state';
import {createSelector} from '@ngrx/store';

export const selectBackendConfigurationPn =
    (state: {backendConfigurationPn: BackendConfigurationState}) => state.backendConfigurationPn;
export const selectTaskTracker =
    createSelector(selectBackendConfigurationPn, (state: BackendConfigurationState) => state.taskTrackerState);
export const selectTaskTrackerFilters =
    createSelector(selectTaskTracker, (state) => state.filters);
