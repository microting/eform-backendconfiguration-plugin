import {
  calendarFiltersStorageKey,
  parseSavedCalendarDate,
  readSavedCalendarFilters,
  writeSavedCalendarFilters,
} from './calendar-filters.storage';

describe('calendar-filters.storage (#1303)', () => {
  const USER = 7;
  const filters = {
    propertyId: 2,
    activeBoardIds: [11, 12],
    activeSiteIds: [5],
    activeTeamIds: [3],
    activeTagNames: ['x'],
    currentDate: '2026-10-05',
    viewMode: 'month' as const,
  };

  beforeEach(() => window.localStorage.clear());
  afterEach(() => jest.restoreAllMocks());

  it('keys the settings per user', () => {
    expect(calendarFiltersStorageKey(USER)).toBe('bcpn.calendar.filters.7');
  });

  it('saves only property, calendars, workers and week (not view mode, teams or tags)', () => {
    writeSavedCalendarFilters(USER, filters);

    expect(JSON.parse(window.localStorage.getItem('bcpn.calendar.filters.7'))).toEqual({
      propertyId: 2, activeBoardIds: [11, 12], activeSiteIds: [5], currentDate: '2026-10-05',
    });
  });

  it('round-trips a valid saved state', () => {
    writeSavedCalendarFilters(USER, filters);

    expect(readSavedCalendarFilters(USER)).toEqual({
      propertyId: 2, activeBoardIds: [11, 12], activeSiteIds: [5], currentDate: '2026-10-05',
    });
  });

  it('keeps a week in the past as saved', () => {
    writeSavedCalendarFilters(USER, {...filters, currentDate: '2020-01-06'});

    expect(readSavedCalendarFilters(USER).currentDate).toBe('2020-01-06');
  });

  it('does not save a state without a property (initial state / after logout)', () => {
    writeSavedCalendarFilters(USER, {...filters, propertyId: null});

    expect(window.localStorage.getItem('bcpn.calendar.filters.7')).toBeNull();
  });

  it('does not save or read without a user id', () => {
    writeSavedCalendarFilters(0, filters);
    expect(window.localStorage.length).toBe(0);
    expect(readSavedCalendarFilters(0)).toBeNull();
  });

  it('never reads another user\'s settings', () => {
    writeSavedCalendarFilters(USER, filters);

    expect(readSavedCalendarFilters(8)).toBeNull();
  });

  it('returns null when nothing is saved (first visit)', () => {
    expect(readSavedCalendarFilters(USER)).toBeNull();
  });

  it.each([
    ['not JSON', '{nope'],
    ['JSON null', 'null'],
    ['a number', '42'],
    ['no property', JSON.stringify({activeBoardIds: [1]})],
    ['a string property', JSON.stringify({propertyId: '2'})],
    ['a negative property', JSON.stringify({propertyId: -2})],
    ['a fractional property', JSON.stringify({propertyId: 2.5})],
  ])('returns null for a corrupt payload (%s)', (_label, raw) => {
    window.localStorage.setItem('bcpn.calendar.filters.7', raw);

    expect(readSavedCalendarFilters(USER)).toBeNull();
  });

  it('drops non-id entries from the saved calendar and worker lists', () => {
    window.localStorage.setItem('bcpn.calendar.filters.7', JSON.stringify({
      propertyId: 2, activeBoardIds: [11, '12', null, 0, 13], activeSiteIds: 'x', currentDate: '2026-10-05',
    }));

    expect(readSavedCalendarFilters(USER)).toEqual({
      propertyId: 2, activeBoardIds: [11, 13], activeSiteIds: [], currentDate: '2026-10-05',
    });
  });

  it.each([
    ['missing', undefined],
    ['a number', 20261005],
    ['garbage', 'yesterday'],
    ['an ISO timestamp', '2026-10-05T00:00:00Z'],
    ['a non-existent day', '2026-02-31'],
    ['month 13', '2026-13-01'],
    ['day 0', '2026-10-00'],
  ])('leaves the date out (store keeps today) when it is %s', (_label, currentDate) => {
    window.localStorage.setItem('bcpn.calendar.filters.7', JSON.stringify({
      propertyId: 2, activeBoardIds: [11], activeSiteIds: [], currentDate,
    }));

    const restored = readSavedCalendarFilters(USER);
    expect(restored.propertyId).toBe(2);
    expect('currentDate' in restored).toBe(false);
  });

  it.each([
    ['2024-02-29', '2024-02-29'],
    ['2025-02-29', null],
    ['2026-12-31', '2026-12-31'],
    ['2026-01-01', '2026-01-01'],
  ])('parseSavedCalendarDate(%s) -> %s', (input, expected) => {
    expect(parseSavedCalendarDate(input)).toBe(expected);
  });

  it('does not throw when reading from storage throws', () => {
    jest.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('SecurityError'); });

    expect(() => readSavedCalendarFilters(USER)).not.toThrow();
    expect(readSavedCalendarFilters(USER)).toBeNull();
  });

  it('does not throw when writing to storage throws', () => {
    jest.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('QuotaExceededError'); });

    expect(() => writeSavedCalendarFilters(USER, filters)).not.toThrow();
  });
});
