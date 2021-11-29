import { propertiesPersistProvider } from './components/properties/store';
import { propertyWorkersPersistProvider } from './modules/property-workers/components/store';

export const backendConfigurationStoreProviders = [
  propertiesPersistProvider,
  propertyWorkersPersistProvider,
];
