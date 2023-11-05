import {TaskWizardStatusesEnum} from 'src/app/plugins/modules/backend-configuration-pn/enums';
import {SortState} from 'src/app/common/models';
import {Action, createReducer, on} from '@ngrx/store';
import {
  taskWizardUpdateFilters, taskWizardUpdatePagination
} from "src/app/plugins/modules/backend-configuration-pn/state/task-wizard/task-wizard.actions";

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
  on(taskWizardUpdateFilters, (state, {filters}) => ({
    ...state,
    filters: {...state.filters, ...filters},
    }
  )),
  on(taskWizardUpdatePagination, (state, {pagination}) => ({
    ...state,
    pagination: {...state.pagination, ...pagination},
    }
  )),
);

export function reducer(state: TaskWizardState | undefined, action: Action) {
  return _reducer(state, action);
}
