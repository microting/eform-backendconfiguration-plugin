import {AdhocFiltrationModel} from 'src/app/plugins/modules/backend-configuration-pn/state';
import {CommonPaginationState} from 'src/app/common/models';

/**
 * Shape parity with `models/task-management/task-management-request.model.ts`
 * - the UI-facing (ngrx) filters + pagination bundle the state facade (F5)
 * composes before mapping to the wire-level `AdhocTaskFiltersModel`.
 */
export interface AdhocTaskRequestModel {
  filters: AdhocFiltrationModel;
  pagination: CommonPaginationState;
}
