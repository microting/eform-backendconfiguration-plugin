import {createAction} from '@ngrx/store';

export const taskWorkerAssignmentUpdatePagination = createAction(
  '[TaskWorkerAssignment] Update pagination',
  (payload) => ({payload})
);

export const taskWorkerAssignmentUpdateTotalProperties = createAction(
  '[TaskWorkerAssignment] Update total properties',
  (payload) => ({payload})
);
