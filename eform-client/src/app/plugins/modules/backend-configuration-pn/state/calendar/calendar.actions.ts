import {createAction} from '@ngrx/store';
import {CalendarFiltersModel} from './calendar.reducer';

export const calendarUpdateFilters = createAction(
  '[Calendar] Update filters',
  (payload: Partial<CalendarFiltersModel>) => ({payload})
);
