import {createAction} from '@ngrx/store';
import {TaskWizardFiltrationModel, TaskWizardPaginationModel} from './';

export const taskWizardUpdateFilters = createAction(
  '[TaskWizard] Update filters',
  (payload: TaskWizardFiltrationModel) => ({payload})
)

export const taskWizardUpdatePagination = createAction(
  '[TaskWizard] Update pagination',
  (payload: TaskWizardPaginationModel) => ({payload})
)
