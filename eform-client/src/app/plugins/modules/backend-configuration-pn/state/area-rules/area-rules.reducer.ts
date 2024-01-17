import {SortModel} from 'src/app/common/models';
import {Action, createReducer, on} from '@ngrx/store';
import {
  updateAreaRulesPagination
} from './area-rules.actions';

export interface AreaRulesState {
  pagination: SortModel;
}

export const areaRulesInitialState: AreaRulesState = {
  pagination: {
    sort: 'Id',
    isSortDsc: false,
  }
};

const _areaRulesReducer = createReducer(
  areaRulesInitialState,
  on(updateAreaRulesPagination, (state, {payload}) => ({
      ...state,
      pagination: {...state.pagination, ...payload}
    }
  )),
);

export function areaRulesReducer(state: AreaRulesState | undefined, action: Action) {
  return _areaRulesReducer(state, action);
}
