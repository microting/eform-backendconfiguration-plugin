import {
  AdhocState,
  AreaRulesState,
  DocumentsState,
  FilesState,
  CalendarState,
  ChemicalsState,
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
  adhocState: AdhocState;
  areaRulesState: AreaRulesState,
  documentsState: DocumentsState,
  filesState: FilesState;
  calendarState: CalendarState;
  chemicalsState: ChemicalsState;
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
