import {TaskWizardStatusesEnum} from 'src/app/plugins/modules/backend-configuration-pn/enums';
import {SortState} from 'src/app/common/models';
import {Action, createReducer, on} from '@ngrx/store';
import {
  taskWizardUpdateFilters, taskWizardUpdatePagination
} from './task-wizard.actions';

export interface TaskWizardFiltrationModel {
  propertyIds: number[];
  tagIds: number[];
  folderIds: number[];
  assignToIds: number[];
  status: TaskWizardStatusesEnum | null;
}

export interface TaskWizardPaginationModel extends SortState {
}

export interface TaskWizardState {
  filters: TaskWizardFiltrationModel;
  pagination: TaskWizardPaginationModel;
}

export const initialState: TaskWizardState = {
  filters: {
    propertyIds: [],
    tagIds: [],
    folderIds: [],
    assignToIds: [],
    status: null,
  },
  pagination: {
    sort: 'Id',
    isSortDsc: false,
  },
}

export const _reducer = createReducer(
  initialState,
  on(taskWizardUpdateFilters, (state, {payload}) => ({
    ...state,
    filters: {
      propertyIds: payload.filters.propertyIds,
      tagIds: payload.filters.tagIds,
      folderIds: payload.filters.folderIds,
      assignToIds: payload.filters.assignToIds,
      status: payload.filters.status,
    },
    }
  )),
  on(taskWizardUpdatePagination, (state, {payload}) => ({
    ...state,
    pagination: {...state.pagination, ...payload},
    }
  )),
);

export function reducer(state: TaskWizardState | undefined, action: Action) {
  return _reducer(state, action);
}
