import {logout} from 'src/app/state/auth/auth.actions';
import {calendarUpdateFilters} from './calendar.actions';
import {calendarReducer, CalendarState} from './calendar.reducer';

describe('calendarReducer', () => {
  const firstUsersState: CalendarState = {
    filters: {
      propertyId: 42,
      activeBoardIds: [3, 4],
      activeSiteIds: [5],
      activeTeamIds: [6],
      activeTagNames: ['x'],
      currentDate: '2020-01-06',
      viewMode: 'month',
    },
  };

  // #1292: the calendar feature store outlives the session, so without a
  // reset on logout a second user in the same tab inherited the first
  // user's property and calendar/worker selections.
  it('resets every calendar filter on logout', () => {
    const state = calendarReducer(firstUsersState, logout());

    expect(state.filters.propertyId).toBeNull();
    expect(state.filters.activeBoardIds).toEqual([]);
    expect(state.filters.activeSiteIds).toEqual([]);
    expect(state.filters.activeTeamIds).toEqual([]);
    expect(state.filters.activeTagNames).toEqual([]);
    expect(state.filters.viewMode).toBe('week');
  });

  it('resets the date to today on logout, not to the date the app was loaded', () => {
    const state = calendarReducer(firstUsersState, logout());

    expect(state.filters.currentDate).toBe(new Date().toISOString().split('T')[0]);
  });

  it('still merges filter updates without touching the other fields', () => {
    const state = calendarReducer(firstUsersState, calendarUpdateFilters({activeBoardIds: [9]}));

    expect(state.filters).toEqual({...firstUsersState.filters, activeBoardIds: [9]});
  });
});
