import {
  BackendConfigurationState,
} from '../';
import {createSelector} from '@ngrx/store';

const selectBackendConfigurationPn =
  (state: {backendConfigurationPn: BackendConfigurationState}) => state.backendConfigurationPn;
export const selectTaskWizard =
  createSelector(selectBackendConfigurationPn, (state) => state.taskWizardState);
export const selectTaskWizardPagination =
  createSelector(selectTaskWizard, (state) => state.pagination);
export const selectTaskWizardPaginationSort =
  createSelector(selectTaskWizard, (state) => state.pagination.sort);
export const selectTaskWizardPaginationIsSortDsc =
  createSelector(selectTaskWizard, (state) => state.pagination.isSortDsc ? 'desc' : 'asc');
export const selectTaskWizardFilters =
  createSelector(selectTaskWizard, (state) => state.filters);
export const selectTaskWizardPropertyIds =
  createSelector(selectTaskWizardFilters, (state) => state.propertyIds);
