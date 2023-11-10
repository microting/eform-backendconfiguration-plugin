import {
  TaskWizardFiltrationModel, TaskWizardPaginationModel
} from '../../state/task-wizard/task-wizard.reducer';

export interface TaskWizardRequestModel {
  filters: TaskWizardFiltrationModel
  pagination: TaskWizardPaginationModel
}
