import {createAction, props} from '@ngrx/store';
import {ChemicalHistoryFiltersModel, ChemicalInventoryFiltersModel} from './chemicals.reducer';

export const chemicalsSelectProperty = createAction('[Chemicals] Select property', props<{payload: number | null}>());

export const chemicalsUpdateInventoryFilters = createAction(
  '[Chemicals] Update inventory filters', props<{payload: ChemicalInventoryFiltersModel}>());

export const chemicalsUpdateHistoryFilters = createAction(
  '[Chemicals] Update history filters', props<{payload: ChemicalHistoryFiltersModel}>());
