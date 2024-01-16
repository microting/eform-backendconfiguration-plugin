import {createAction} from '@ngrx/store';
import {SortModel} from 'src/app/common/models';

export const updateAreaRulesPagination = createAction(
  '[AreaRules] Update pagination',
  (payload: SortModel) => ({ payload })
);
