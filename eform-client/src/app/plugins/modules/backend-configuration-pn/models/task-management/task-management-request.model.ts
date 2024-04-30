import {TaskManagementFiltrationModel} from 'src/app/plugins/modules/backend-configuration-pn/state';
import {CommonPaginationState} from 'src/app/common/models';

export interface TaskManagementRequestModel {
  filters: TaskManagementFiltrationModel;
  pagination: CommonPaginationState;
}
