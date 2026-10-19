import {createSelector} from '@ngrx/store';
import {BackendConfigurationState} from '../';

const selectBackendConfigurationPn =
  (state: {backendConfigurationPn: BackendConfigurationState}) => state.backendConfigurationPn;
export const selectChemicals = createSelector(selectBackendConfigurationPn, (state) => state.chemicalsState);
export const selectChemicalsPropertyId = createSelector(selectChemicals, (state) => state.propertyId);
export const selectChemicalsProperties = createSelector(selectChemicals, (state) => state.properties);
export const selectChemicalsInventoryFilters = createSelector(selectChemicals, (state) => state.inventoryFilters);
export const selectChemicalsHistoryFilters = createSelector(selectChemicals, (state) => state.historyFilters);
/**
 * W6 reads the property and its filters together: a property switch also clears the history's user and
 * location filter (reducer), and two separate selects would load once with the old filters first.
 */
export const selectChemicalsHistoryView = createSelector(
  selectChemicalsPropertyId, selectChemicalsHistoryFilters, (propertyId, filters) => ({propertyId, filters}));
