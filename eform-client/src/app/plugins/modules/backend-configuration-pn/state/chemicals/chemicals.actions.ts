import {CommonDictionaryModel} from 'src/app/common/models';
import {createAction, props} from '@ngrx/store';
import {ChemicalHistoryFiltersModel, ChemicalInventoryFiltersModel} from './chemicals.reducer';

export const chemicalsSelectProperty = createAction('[Chemicals] Select property', props<{payload: number | null}>());

/** The property dictionary, loaded once by the Kemi container; the screens read it instead of fetching it again. */
export const chemicalsSetProperties = createAction('[Chemicals] Set properties', props<{payload: CommonDictionaryModel[]}>());

export const chemicalsUpdateInventoryFilters = createAction(
  '[Chemicals] Update inventory filters', props<{payload: ChemicalInventoryFiltersModel}>());

export const chemicalsUpdateHistoryFilters = createAction(
  '[Chemicals] Update history filters', props<{payload: ChemicalHistoryFiltersModel}>());
