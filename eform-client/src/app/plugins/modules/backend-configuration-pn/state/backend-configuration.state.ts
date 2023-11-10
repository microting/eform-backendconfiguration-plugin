import {StatisticsState} from './statistics/statistics.reducer';
import {
  TaskTrackerState
} from './task-tracker/task-tracker.reducer';
import {
  TaskWorkerAssignmentState
} from './task-worker-assignment/task-worker-assignment.reducer';
import {FilesState} from './files/files.reducer';
import {DocumentsState} from './documents/documents.reducer';
import {PropertiesState} from './properties/properties.reducer';
import {ReportStateV1} from './reports-v1/reports-v1.reducer';
import {ReportStateV2} from './reports-v2/reports-v2.reducer';
import {
  TaskManagementState
} from './task-management/task-management.reducer';
import {TaskWizardState} from './task-wizard/task-wizard.reducer';
import {
  PropertyWorkersState
} from './property-workers/property-workers.reducer';
import {AreaRulesState} from './area-rules/area-rules.reducer';

export interface BackendConfigurationState {
  documentsState: DocumentsState;
  filesState: FilesState;
  propertiesState: PropertiesState;
  propertyWorkersState: PropertyWorkersState
  reportsV1State: ReportStateV1;
  reportsV2State: ReportStateV2;
  statisticsState: StatisticsState;
  taskManagementState: TaskManagementState;
  taskTrackerState: TaskTrackerState;
  taskWizardState: TaskWizardState;
  taskWorkerAssignmentState: TaskWorkerAssignmentState;
  areaRulesState: AreaRulesState;
}
