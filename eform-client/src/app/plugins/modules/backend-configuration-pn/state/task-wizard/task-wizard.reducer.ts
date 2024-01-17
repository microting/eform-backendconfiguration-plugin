import {TaskWizardStatusesEnum} from '../../enums';
import {SortState} from 'src/app/common/models';
import {Action, createReducer, on} from '@ngrx/store';
import {
  taskWizardUpdateFilters,
  taskWizardUpdatePagination
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

export const taskWizardInitialState: TaskWizardState = {
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
};

const _taskWizardReducer = createReducer(
  taskWizardInitialState,
  on(taskWizardUpdateFilters, (state, {payload}) => ({
      ...state,
      filters: {...state.filters, ...payload,},
    }
  )),
  on(taskWizardUpdatePagination, (state, {payload}) => ({
      ...state,
      pagination: {...state.pagination, ...payload},
    }
  )),
);

export function taskWizardReducer(state: TaskWizardState | undefined, action: Action) {
  return _taskWizardReducer(state, action);
}
