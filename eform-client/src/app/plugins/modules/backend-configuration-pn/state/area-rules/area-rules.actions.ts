import {createAction} from '@ngrx/store';

export const updateAreaRulesPagination = createAction(
  '[AreaRules] Update pagination',
  (payload) => ({ payload })
);
