import {describe, it, expect} from 'vitest';
import {ChemicalsState, chemicalsInitialState, chemicalsReducer} from './chemicals.reducer';
import {selectChemicalsHistoryView} from './chemicals.selector';
import {chemicalsSelectProperty, chemicalsSetProperties, chemicalsUpdateHistoryFilters, chemicalsUpdateInventoryFilters} from './chemicals.actions';

describe('chemicalsReducer', () => {
  const filtered = {
    ...chemicalsInitialState,
    propertyId: 1,
    inventoryFilters: {search: 'round', locationId: 4, status: 'deregistered' as const, requiresActionOnly: true},
    historyFilters: {dateFrom: '2026-09-01', dateTo: '2026-09-29', userId: 5, action: 3, locationId: 4},
  };

  it('starts with no property and neutral filters', () => {
    const state = chemicalsReducer(undefined, {type: '@@init'});

    expect(state.propertyId).toBeNull();
    expect(state.inventoryFilters).toEqual({search: '', locationId: null, status: 'all', requiresActionOnly: false});
    expect(state.historyFilters).toEqual({dateFrom: null, dateTo: null, userId: null, action: null, locationId: null});
  });

  it('starts with no loaded properties and stores the list the container loads', () => {
    expect(chemicalsInitialState.properties).toEqual([]);

    const state = chemicalsReducer(filtered, chemicalsSetProperties({payload: [{id: 1, name: 'Engvej 12', description: ''}]}));

    expect(state.properties).toEqual([{id: 1, name: 'Engvej 12', description: ''}]);
    expect(state.propertyId).toBe(1);
  });

  it('selecting ANOTHER property clears the ids that belonged to the old one', () => {
    const state = chemicalsReducer(filtered, chemicalsSelectProperty({payload: 2}));

    expect(state.propertyId).toBe(2);
    expect(state.inventoryFilters).toEqual({search: 'round', locationId: null, status: 'deregistered', requiresActionOnly: true});
    expect(state.historyFilters).toEqual({dateFrom: '2026-09-01', dateTo: '2026-09-29', userId: null, action: 3, locationId: null});
  });

  it('re-selecting the SAME property keeps every filter', () => {
    expect(chemicalsReducer(filtered, chemicalsSelectProperty({payload: 1}))).toBe(filtered);
  });

  it('replaces inventory and history filters', () => {
    const inventory = {search: 'x', locationId: 9, status: 'expired' as const, requiresActionOnly: false};
    const history = {dateFrom: '2026-01-01', dateTo: '2026-01-31', userId: 0, action: null, locationId: null};

    expect(chemicalsReducer(filtered, chemicalsUpdateInventoryFilters({payload: inventory})).inventoryFilters).toEqual(inventory);
    expect(chemicalsReducer(filtered, chemicalsUpdateHistoryFilters({payload: history})).historyFilters).toEqual(history);
  });

  it('hands W6 the property and its filters as one value, once per state change', () => {
    const root = (chemicalsState: ChemicalsState) => ({backendConfigurationPn: {chemicalsState}}) as never;
    const switched = chemicalsReducer(filtered, chemicalsSelectProperty({payload: 2}));

    const before = selectChemicalsHistoryView(root(filtered));
    const after = selectChemicalsHistoryView(root(switched));

    expect(before).toEqual({propertyId: 1, filters: filtered.historyFilters});
    // The switch changes the property and clears the user and location in the same value: no load with the old filters.
    expect(after).toEqual({propertyId: 2, filters: {...filtered.historyFilters, userId: null, locationId: null}});
    expect(selectChemicalsHistoryView(root(switched))).toBe(after);
  });
});
