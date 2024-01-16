import {
  BackendConfigurationState,
} from '../';
import {createSelector} from '@ngrx/store';

const selectBackendConfigurationPn =
  (state: {backendConfigurationPn: BackendConfigurationState}) => state.backendConfigurationPn;
export const selectPropertyWorkers =
    createSelector(selectBackendConfigurationPn, (state) => state.propertyWorkersState);
export const selectPropertyWorkersFilters =
    createSelector(selectPropertyWorkers, (state) => state.filters);
export const selectPropertyWorkersPagination =
    createSelector(selectPropertyWorkers, (state) => state.pagination);
export const selectPropertyWorkersPaginationSort =
    createSelector(selectPropertyWorkers, (state) => state.pagination.sort);
export const selectPropertyWorkersPaginationIsSortDsc =
    createSelector(selectPropertyWorkers, (state) => state.pagination.isSortDsc ? 'desc' : 'asc');
export const selectPropertyWorkersNameFilters =
    createSelector(selectPropertyWorkers, (state) => state.filters.nameFilter);
