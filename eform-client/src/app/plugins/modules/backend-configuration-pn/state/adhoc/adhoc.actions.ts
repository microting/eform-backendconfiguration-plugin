import {createAction} from '@ngrx/store';
import {AdhocFiltrationModel, AdhocHistoryFiltrationModel} from './adhoc.reducer';
import {CommonPaginationState} from 'src/app/common/models';

export const adhocUpdateFilters = createAction(
  '[Adhoc] Update filters',
  (payload: AdhocFiltrationModel) => ({payload})
)

export const adhocUpdatePagination = createAction(
  '[Adhoc] Update pagination',
  (payload: CommonPaginationState) => ({payload})
)

export const adhocUpdateHiddenColumns = createAction(
  '[Adhoc] Update hidden columns',
  (payload: string[]) => ({payload})
)

export const adhocUpdateHistoryFilters = createAction(
  '[Adhoc] Update history filters',
  (payload: AdhocHistoryFiltrationModel) => ({payload})
)
