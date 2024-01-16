import {createAction} from '@ngrx/store';
import {CommonPaginationState} from 'src/app/common/models';

export const taskWorkerAssignmentUpdatePagination = createAction(
  '[TaskWorkerAssignment] Update pagination',
  (payload: CommonPaginationState) => ({payload})
);

export const taskWorkerAssignmentUpdateTotalProperties = createAction(
  '[TaskWorkerAssignment] Update total properties',
  (payload: number) => ({payload})
);
