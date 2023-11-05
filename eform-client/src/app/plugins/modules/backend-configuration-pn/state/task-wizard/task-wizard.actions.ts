import {createAction} from '@ngrx/store';

export const taskWizardUpdateFilters = createAction(
  'taskWizardUpdateFilters',
  (filters) => ({filters})
)

export const taskWizardUpdatePagination = createAction(
  'taskWizardUpdatePagination',
  (pagination) => ({pagination})
)
