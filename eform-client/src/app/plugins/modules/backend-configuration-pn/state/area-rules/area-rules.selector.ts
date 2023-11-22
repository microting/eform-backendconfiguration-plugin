import {
  BackendConfigurationState
} from '../backend-configuration.state';
import {createSelector} from '@ngrx/store';

export const selectBackendConfigurationPn =
  (state: {backendConfigurationPn: BackendConfigurationState}) => state.backendConfigurationPn;
export const selectAreaRules =
  createSelector(selectBackendConfigurationPn, (state: BackendConfigurationState) => state.areaRulesState);
export const selectAreaRulesPagination =
  createSelector(selectAreaRules, (state) => state.pagination);
export const selectAreaRulesPaginationSort =
  createSelector(selectAreaRules, (state) => state.pagination.sort);
export const selectAreaRulesPaginationIsSortDsc =
  createSelector(selectAreaRules, (state) => state.pagination.isSortDsc ? 'desc' : 'asc');
