import {BackendConfigurationState} from '../';
import {createSelector} from '@ngrx/store';

const selectBackendConfigurationPn =
  (state: {backendConfigurationPn: BackendConfigurationState}) => state.backendConfigurationPn;

export const selectCalendar =
  createSelector(selectBackendConfigurationPn, (state) => state.calendarState);

export const selectCalendarFilters =
  createSelector(selectCalendar, (state) => state.filters);

export const selectCalendarPropertyId =
  createSelector(selectCalendarFilters, (f) => f.propertyId);

export const selectCalendarViewMode =
  createSelector(selectCalendarFilters, (f) => f.viewMode);

export const selectCalendarCurrentDate =
  createSelector(selectCalendarFilters, (f) => f.currentDate);

export const selectCalendarActiveBoardIds =
  createSelector(selectCalendarFilters, (f) => f.activeBoardIds);

export const selectCalendarActiveSiteIds =
  createSelector(selectCalendarFilters, (f) => f.activeSiteIds);

export const selectCalendarActiveTeamIds =
  createSelector(selectCalendarFilters, (f) => f.activeTeamIds);

export const selectCalendarActiveTagNames =
  createSelector(selectCalendarFilters, (f) => f.activeTagNames);

export const selectCalendarSidebarOpen =
  createSelector(selectCalendarFilters, (f) => f.sidebarOpen);
