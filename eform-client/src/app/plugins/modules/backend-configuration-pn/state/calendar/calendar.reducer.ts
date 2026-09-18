import {Action, createReducer, on} from '@ngrx/store';
import {logout} from 'src/app/state/auth/auth.actions';
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

/**
 * A fresh initial state. A factory rather than only the constant below because
 * `currentDate` is "today": the constant is evaluated once, when the bundle
 * loads, so a reset hours (or days) later must not jump back to that date.
 */
export function createCalendarInitialState(): CalendarState {
  return {
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
}

export const calendarInitialState: CalendarState = createCalendarInitialState();

export const _calendarReducer = createReducer(
  calendarInitialState,
  on(calendarUpdateFilters, (state, {payload}) => ({
    ...state,
    filters: {...state.filters, ...payload},
  })),
  // #1292: the calendar filters live in a module-level feature store that
  // outlives both the component and the session, so without this a second
  // user signing in in the same tab inherited the first user's property and
  // calendar/worker selections. The core auth `logout` action is broadcast to
  // every registered reducer, so handling it here resets the calendar without
  // any change to the core AuthStateService.
  on(logout, () => createCalendarInitialState()),
);

export function calendarReducer(state: CalendarState | undefined, action: Action) {
  return _calendarReducer(state, action);
}
