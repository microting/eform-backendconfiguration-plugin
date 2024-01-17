import {createAction} from '@ngrx/store';
import {StatisticsModel} from './';

export const statisticsUpdateFilters = createAction(
  '[Statistics] Update filters',
  (payload: StatisticsModel) => ({payload})
);
