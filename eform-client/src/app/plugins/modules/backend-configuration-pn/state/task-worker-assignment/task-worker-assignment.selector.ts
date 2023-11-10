import {
  BackendConfigurationState
} from '../backend-configuration.state';
import {createSelector} from '@ngrx/store';

export const selectBackendConfigurationPn =
  (state: {backendConfigurationPn: BackendConfigurationState}) => state.backendConfigurationPn;
export const selectTaskWorkerAssignment =
  createSelector(selectBackendConfigurationPn, (state: BackendConfigurationState) => state.taskWorkerAssignmentState);

export const selectTaskWorkerAssignmentPagination =
  createSelector(selectTaskWorkerAssignment, (state) => state.pagination);
export const selectTaskWorkerAssignmentPaginationSort =
  createSelector(selectTaskWorkerAssignment, (state) => state.pagination.sort);
export const selectTaskWorkerAssignmentPaginationIsSortDsc =
  createSelector(selectTaskWorkerAssignment, (state) => state.pagination.isSortDsc ? 'desc' : 'asc');
