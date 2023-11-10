import {createAction} from '@ngrx/store';

export const taskWizardUpdateFilters = createAction(
  '[TaskWizard] Update filters',
  (payload) => ({payload})
)

export const taskWizardUpdatePagination = createAction(
  '[TaskWizard] Update pagination',
  (payload) => ({payload})
)
