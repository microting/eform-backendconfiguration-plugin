import {Action, createReducer, on} from '@ngrx/store';
import {calendarUpdateFilters} from './calendar.actions';

export interface CalendarFiltersModel {
  propertyId: number | null;
  activeBoardIds: number[];
  activeSiteIds: number[];
  activeTeamIds: number[];
  activeTagNames: string[];
  currentDate: string;           // ISO "YYYY-MM-DD"
  viewMode: 'week' | 'day' | 'schedule' | 'month';
}

export interface CalendarState {
  filters: CalendarFiltersModel;
}

export const calendarInitialState: CalendarState = {
  filters: {
    propertyId: null,
    activeBoardIds: [],
    activeSiteIds: [],
    activeTeamIds: [],
    activeTagNames: [],
    currentDate: new Date().toISOString().split('T')[0],
    viewMode: 'week',
  },
};

export const _calendarReducer = createReducer(
  calendarInitialState,
  on(calendarUpdateFilters, (state, {payload}) => ({
    ...state,
    filters: {...state.filters, ...payload},
  })),
);

export function calendarReducer(state: CalendarState | undefined, action: Action) {
  return _calendarReducer(state, action);
}
