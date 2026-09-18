import {Store} from '@ngrx/store';
import {BehaviorSubject, of} from 'rxjs';
import {selectAuthUser} from 'src/app/state/auth/auth.selector';
import {calendarUpdateFilters, selectCalendarFilters} from '../../../../state';
import {CalendarStateService} from './calendar.service';

describe('CalendarStateService', () => {
  let store: {select: jest.Mock; dispatch: jest.Mock};
  let service: CalendarStateService;

  beforeEach(() => {
    store = {select: jest.fn().mockReturnValue(of({})), dispatch: jest.fn()};
    service = new CalendarStateService(store as unknown as Store);
  });

  // #1292: the restore path must not wipe the selections the way a property
  // switch does, or re-entering the calendar loses the chosen calendars.
  it('restoreFilters writes the property and its selections without clearing anything', () => {
    service.restoreFilters({propertyId: 2, activeBoardIds: [11], activeSiteIds: [5]});

    expect(store.dispatch).toHaveBeenCalledTimes(1);
    expect(store.dispatch).toHaveBeenCalledWith(
      calendarUpdateFilters({propertyId: 2, activeBoardIds: [11], activeSiteIds: [5]}));
  });

  it('updatePropertyId still clears the selections (a user picking another property)', () => {
    service.updatePropertyId(2);

    expect(store.dispatch).toHaveBeenCalledWith(calendarUpdateFilters({
      propertyId: 2, activeBoardIds: [], activeSiteIds: [], activeTeamIds: [], activeTagNames: [],
    }));
  });
});

// #1303: the last property / calendars / workers / week, per user, in localStorage.
describe('CalendarStateService — saved settings (#1303)', () => {
  const KEY = (userId: number) => `bcpn.calendar.filters.${userId}`;
  let filters$: BehaviorSubject<any>;
  let user$: BehaviorSubject<any>;
  let store: {select: jest.Mock; dispatch: jest.Mock};
  let service: CalendarStateService;

  function initialFilters() {
    return {
      propertyId: null, activeBoardIds: [], activeSiteIds: [], activeTeamIds: [], activeTagNames: [],
      currentDate: '2026-09-18', viewMode: 'week',
    };
  }

  beforeEach(() => {
    window.localStorage.clear();
    filters$ = new BehaviorSubject<any>(initialFilters());
    user$ = new BehaviorSubject<any>({id: 1});
    store = {
      select: jest.fn((selector: any) => {
        if (selector === selectCalendarFilters) return filters$;
        if (selector === selectAuthUser) return user$;
        return of({});
      }),
      // Behaves like the reducer: merges the payload synchronously.
      dispatch: jest.fn((action: any) => filters$.next({...filters$.value, ...action.payload})),
    };
    service = new CalendarStateService(store as unknown as Store);
  });

  afterEach(() => jest.restoreAllMocks());

  function saved(userId: number) {
    const raw = window.localStorage.getItem(KEY(userId));
    return raw ? JSON.parse(raw) : null;
  }

  it('saves nothing before the calendar was opened for a user', () => {
    filters$.next({...filters$.value, propertyId: 2});

    expect(saved(1)).toBeNull();
  });

  it('saves property, calendars, workers and week on every filter change', () => {
    service.restoreSavedFilters(1);
    service.updatePropertyId(2);
    service.setActiveBoardIds([11]);
    service.toggleSite(5);
    service.updateCurrentDate('2026-10-05');

    expect(saved(1)).toEqual({propertyId: 2, activeBoardIds: [11], activeSiteIds: [5], currentDate: '2026-10-05'});

    service.toggleSite(5);
    expect(saved(1).activeSiteIds).toEqual([]);
  });

  it('does not save view mode, teams or tags', () => {
    service.restoreSavedFilters(1);
    service.updatePropertyId(2);
    service.updateViewMode('month');
    service.toggleTeam(3);

    expect(Object.keys(saved(1)).sort()).toEqual(['activeBoardIds', 'activeSiteIds', 'currentDate', 'propertyId']);
  });

  it('restores the saved settings through the restore path (no clearing)', () => {
    window.localStorage.setItem(KEY(1), JSON.stringify(
      {propertyId: 2, activeBoardIds: [11], activeSiteIds: [5], currentDate: '2026-10-05'}));

    service.restoreSavedFilters(1);

    expect(store.dispatch).toHaveBeenCalledTimes(1);
    expect(store.dispatch).toHaveBeenCalledWith(calendarUpdateFilters(
      {propertyId: 2, activeBoardIds: [11], activeSiteIds: [5], currentDate: '2026-10-05'}));
    expect(filters$.value.viewMode).toBe('week');
  });

  it('keeps today when the saved date is unusable', () => {
    window.localStorage.setItem(KEY(1), JSON.stringify(
      {propertyId: 2, activeBoardIds: [], activeSiteIds: [], currentDate: 'not-a-date'}));

    service.restoreSavedFilters(1);

    expect(filters$.value.propertyId).toBe(2);
    expect(filters$.value.currentDate).toBe('2026-09-18');
  });

  it('changes nothing on a first visit (nothing saved)', () => {
    service.restoreSavedFilters(1);

    expect(store.dispatch).not.toHaveBeenCalled();
    expect(filters$.value).toEqual(initialFilters());
  });

  it('keeps the in-memory settings on re-entry for the same user (#1292) without re-reading storage', () => {
    service.restoreSavedFilters(1);
    service.updatePropertyId(3);
    window.localStorage.setItem(KEY(1), JSON.stringify({propertyId: 2, activeBoardIds: [], activeSiteIds: []}));
    store.dispatch.mockClear();

    service.restoreSavedFilters(1);

    expect(store.dispatch).not.toHaveBeenCalled();
    expect(filters$.value.propertyId).toBe(3);
  });

  it('reads only the signed-in user\'s key', () => {
    window.localStorage.setItem(KEY(1), JSON.stringify({propertyId: 2, activeBoardIds: [11], activeSiteIds: [5]}));

    service.restoreSavedFilters(2);

    expect(store.dispatch).not.toHaveBeenCalled();
    expect(filters$.value.propertyId).toBeNull();
  });

  it('after a logout reset, the next user gets their own settings and never writes into the first user\'s key', () => {
    window.localStorage.setItem(KEY(2), JSON.stringify({propertyId: 4, activeBoardIds: [40], activeSiteIds: []}));
    service.restoreSavedFilters(1);
    service.updatePropertyId(2);
    service.setActiveBoardIds([11]);

    // logout: the reducer resets the calendar state and the auth user.
    user$.next({id: 0});
    filters$.next(initialFilters());
    expect(saved(1)).toEqual(expect.objectContaining({propertyId: 2, activeBoardIds: [11]}));

    user$.next({id: 2});
    service.restoreSavedFilters(2);
    expect(filters$.value).toEqual(expect.objectContaining({propertyId: 4, activeBoardIds: [40]}));

    service.setActiveBoardIds([41]);
    expect(saved(2).activeBoardIds).toEqual([41]);
    expect(saved(1)).toEqual(expect.objectContaining({propertyId: 2, activeBoardIds: [11]}));
  });

  it('when the signed-in user changed without a logout, resets the first user\'s in-memory settings and never cross-writes', () => {
    service.restoreSavedFilters(1);
    service.updatePropertyId(2);
    service.setActiveBoardIds([11]);

    // Another tab signed in as user 2 (auth synced via storage, no logout here).
    user$.next({id: 2});
    service.setActiveBoardIds([12]);
    expect(saved(2)).toBeNull();
    expect(saved(1).activeBoardIds).toEqual([11]);

    service.restoreSavedFilters(2);
    expect(filters$.value.propertyId).toBeNull();
    expect(filters$.value.activeBoardIds).toEqual([]);
  });

  it('does not crash when storage throws, and behaves as a first visit', () => {
    jest.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('SecurityError'); });
    jest.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('SecurityError'); });

    expect(() => service.restoreSavedFilters(1)).not.toThrow();
    expect(() => service.updatePropertyId(2)).not.toThrow();
    expect(filters$.value.propertyId).toBe(2);
  });
});
