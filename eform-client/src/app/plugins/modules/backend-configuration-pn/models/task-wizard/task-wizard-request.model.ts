import {TaskWizardFiltrationModel, TaskWizardPaginationModel} from '../../modules/task-wizard/components/store';

export interface TaskWizardRequestModel {
  filters: TaskWizardFiltrationModel
  pagination: TaskWizardPaginationModel
}
