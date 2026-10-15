import {Action, createReducer, on} from '@ngrx/store';
import {CommonDictionaryModel} from 'src/app/common/models';
import {chemicalsSelectProperty, chemicalsSetProperties, chemicalsUpdateHistoryFilters, chemicalsUpdateInventoryFilters} from './chemicals.actions';

/** W1 Status filter. BMD codes: approved 5, deregistered 6, expired 7. */
export type ChemicalStatusFilter = 'all' | 'approved' | 'deregistered' | 'expired';

export interface ChemicalInventoryFiltersModel {
  search: string;
  locationId: number | null;
  status: ChemicalStatusFilter;
  requiresActionOnly: boolean;
}

/** W6 filters. Dates are yyyy-MM-dd (inclusive); null = the page's default period. userId 0 = System. */
export interface ChemicalHistoryFiltersModel {
  dateFrom: string | null;
  dateTo: string | null;
  userId: number | null;
  action: number | null;
  locationId: number | null;
}

export interface ChemicalsState {
  /** The property every Kemi tab shows (spec §11: the plugin's property picker). */
  propertyId: number | null;
  /** Property dictionary for the Kemi screens (set by the container once per visit). */
  properties: CommonDictionaryModel[];
  inventoryFilters: ChemicalInventoryFiltersModel;
  historyFilters: ChemicalHistoryFiltersModel;
}

export const chemicalsInitialState: ChemicalsState = {
  propertyId: null,
  properties: [],
  inventoryFilters: {search: '', locationId: null, status: 'all', requiresActionOnly: false},
  historyFilters: {dateFrom: null, dateTo: null, userId: null, action: null, locationId: null},
};

const reducer = createReducer(
  chemicalsInitialState,
  on(chemicalsSelectProperty, (state, {payload}) =>
    payload === state.propertyId
      ? state
      : {
        ...state,
        propertyId: payload,
        // Location and user ids belong to the old property.
        inventoryFilters: {...state.inventoryFilters, locationId: null},
        historyFilters: {...state.historyFilters, locationId: null, userId: null},
      }),
  on(chemicalsSetProperties, (state, {payload}) => ({...state, properties: payload})),
  on(chemicalsUpdateInventoryFilters, (state, {payload}) => ({...state, inventoryFilters: payload})),
  on(chemicalsUpdateHistoryFilters, (state, {payload}) => ({...state, historyFilters: payload})),
);

export function chemicalsReducer(state: ChemicalsState | undefined, action: Action): ChemicalsState {
  return reducer(state, action);
}
