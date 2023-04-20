import {propertiesPersistProvider} from './components/properties/store';
import {propertyWorkersPersistProvider} from './modules/property-workers/components/store';
import {compliancesPersistProvider} from './modules/compliance/components/store';
import {taskWorkerAssignmentsPersistProvider} from './modules/task-worker-assignments/components/store';
import {taskManagementPersistProvider} from './modules/task-management/components/store';
import {taskTrackerPersistProvider} from './modules/task-tracker/components/store';

export const backendConfigurationStoreProviders = [
  propertiesPersistProvider,
  propertyWorkersPersistProvider,
  compliancesPersistProvider,
  taskWorkerAssignmentsPersistProvider,
  taskManagementPersistProvider,
  taskTrackerPersistProvider,
];
