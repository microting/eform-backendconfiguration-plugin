import {createAction} from '@ngrx/store';

export const taskWorkerAssignmentUpdatePagination = createAction(
  '[TaskWorkerAssignment] UpdatePagination',
  (pagination) => ({pagination})
);

export const taskWorkerAssignmentUpdateTotalProperties = createAction(
  '[TaskWorkerAssignment] UpdateTotalProperties',
  (totalProperties) => ({totalProperties})
);
