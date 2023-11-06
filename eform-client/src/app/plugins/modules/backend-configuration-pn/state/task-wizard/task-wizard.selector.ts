import {
  BackendConfigurationState
} from '../backend-configuration.state';
import {createSelector} from '@ngrx/store';

export const selectBackendConfigurationPn =
  (state: {backendConfigurationPn: BackendConfigurationState}) => state.backendConfigurationPn;
export const selectTaskWizard =
  createSelector(selectBackendConfigurationPn, (state: BackendConfigurationState) => state.taskWizardState);
export const selectTaskWizardPagination =
  createSelector(selectTaskWizard, (state) => state.pagination);
export const selectTaskWizardPaginationSort =
  createSelector(selectTaskWizard, (state) => state.pagination.sort);
export const selectTaskWizardPaginationIsSortDsc =
  createSelector(selectTaskWizard, (state) => state.pagination.isSortDsc ? 'asc' : 'desc');
export const selectTaskWizardFilters =
  createSelector(selectTaskWizard, (state) => state.filters);
export const selectTaskWizardPropertyIds =
  createSelector(selectTaskWizardFilters, (state) => state.propertyIds);
