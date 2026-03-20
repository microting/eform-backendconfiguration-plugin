import {
  AreaRulesState,
  DocumentsState,
  FilesState,
  CalendarState,
  PropertiesState,
  PropertyWorkersState,
  ReportStateV1,
  ReportStateV2,
  StatisticsState,
  TaskManagementState,
  TaskTrackerState,
  TaskWizardState,
  TaskWorkerAssignmentState,
} from './';

export interface BackendConfigurationState {
  areaRulesState: AreaRulesState,
  documentsState: DocumentsState,
  filesState: FilesState;
  calendarState: CalendarState;
  propertiesState: PropertiesState;
  propertyWorkersState: PropertyWorkersState
  reportsV1State: ReportStateV1;
  reportsV2State: ReportStateV2;
  statisticsState: StatisticsState;
  taskManagementState: TaskManagementState;
  taskTrackerState: TaskTrackerState;
  taskWizardState: TaskWizardState;
  taskWorkerAssignmentState: TaskWorkerAssignmentState;
}
