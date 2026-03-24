import {Action, createReducer, on} from '@ngrx/store';
import {calendarUpdateFilters} from './calendar.actions';

export interface CalendarSidebarSections {
  properties: boolean;
  boards: boolean;
  teams: boolean;
  employees: boolean;
  tags: boolean;
}

export interface CalendarFiltersModel {
  propertyId: number | null;
  activeBoardIds: number[];
  activeTagNames: string[];
  currentDate: string;           // ISO "YYYY-MM-DD"
  viewMode: 'week' | 'day' | 'schedule';
  sidebarOpen: boolean;
  sidebarSections: CalendarSidebarSections;
}

export interface CalendarState {
  filters: CalendarFiltersModel;
}

export const calendarInitialState: CalendarState = {
  filters: {
    propertyId: null,
    activeBoardIds: [],
    activeTagNames: [],
    currentDate: new Date().toISOString().split('T')[0],
    viewMode: 'week',
    sidebarOpen: true,
    sidebarSections: {
      properties: true,
      boards: true,
      teams: false,
      employees: false,
      tags: false,
    },
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
