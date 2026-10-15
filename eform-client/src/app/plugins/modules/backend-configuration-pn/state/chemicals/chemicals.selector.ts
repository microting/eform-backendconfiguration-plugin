import {createSelector} from '@ngrx/store';
import {BackendConfigurationState} from '../';

const selectBackendConfigurationPn =
  (state: {backendConfigurationPn: BackendConfigurationState}) => state.backendConfigurationPn;
export const selectChemicals = createSelector(selectBackendConfigurationPn, (state) => state.chemicalsState);
export const selectChemicalsPropertyId = createSelector(selectChemicals, (state) => state.propertyId);
export const selectChemicalsProperties = createSelector(selectChemicals, (state) => state.properties);
export const selectChemicalsInventoryFilters = createSelector(selectChemicals, (state) => state.inventoryFilters);
export const selectChemicalsHistoryFilters = createSelector(selectChemicals, (state) => state.historyFilters);
