import {CommonPaginationState} from 'src/app/common/models';
import {Action, createReducer, on} from '@ngrx/store';
import {
  updateAreaRulesPagination
} from './area-rules.actions';

export interface AreaRulesState {
  pagination: CommonPaginationState;
}

export const initialState: AreaRulesState = {
  pagination: {
    sort: 'Id',
    isSortDsc: false,
    offset: 0,
    pageSize: 10,
    total: 0,
    pageIndex: 0,
  }
}

export const _reducer = createReducer(
  initialState,
  on(updateAreaRulesPagination, (state, {payload}) => ({
    ...state,
      pagination: {...state.pagination, ...payload}
    }
  )),
);

export function reducer(state: AreaRulesState | undefined, action: Action) {
  return _reducer(state, action);
}
