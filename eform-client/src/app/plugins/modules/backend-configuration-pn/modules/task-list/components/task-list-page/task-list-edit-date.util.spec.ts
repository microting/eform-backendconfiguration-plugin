import {resolveEditOccurrenceDate} from './task-list-edit-date.util';

/**
 * #1302 / #1140 — which date the task list opens the edit modal on.
 *
 * NOW is Thursday 17 September 2026, 12:00 LOCAL time (passed explicitly, so no
 * clock is read). `upcomingOccurrenceDates` is what `tasks/index` sends: the
 * rule's next occurrences from UTC yesterday on, computed server-side.
 *
 * One row per cell of: start position (past / today-started / today-ahead /
 * future) × repeat kind (daily, weekly, monthly, yearly, one-off, ended).
 */
describe('resolveEditOccurrenceDate', () => {
  const NOW = new Date(2026, 8, 17, 12, 0, 0);

  it.each([
    // [case, taskDate (series start), startHour, upcoming, expected]
    ['daily, past start, today still ahead (14:00) → today',
      '2023-09-17', 14, ['2026-09-16', '2026-09-17', '2026-09-18'], '2026-09-17'],
    ['daily, past start, today already started (09:00) → tomorrow',
      '2023-09-17', 9, ['2026-09-16', '2026-09-17', '2026-09-18'], '2026-09-18'],
    ['daily, past start, today at exactly now (12:00) → tomorrow (must start AFTER now)',
      '2023-09-17', 12, ['2026-09-16', '2026-09-17', '2026-09-18'], '2026-09-18'],
    ['weekly (Mon), past start → next Monday',
      '2025-01-06', 9, ['2026-09-21', '2026-09-28', '2026-10-05'], '2026-09-21'],
    ['every 2nd week, past start, today is an occurrence still ahead → today',
      '2026-09-03', 15.5, ['2026-09-17', '2026-10-01', '2026-10-15'], '2026-09-17'],
    ['monthly (2nd Tuesday), past start → next month\'s',
      '2026-01-13', 9, ['2026-10-13', '2026-11-10', '2026-12-08'], '2026-10-13'],
    ['yearly, past start → next year\'s',
      '2024-03-14', 9, ['2027-03-14', '2028-03-14', '2029-03-14'], '2027-03-14'],
    ['series started EARLIER TODAY → next occurrence',
      '2026-09-17', 8, ['2026-09-16', '2026-09-17', '2026-09-18'], '2026-09-18'],
    ['series starts LATER TODAY → series start (unchanged)',
      '2026-09-17', 15, ['2026-09-16', '2026-09-17', '2026-09-18'], '2026-09-17'],
    ['future-start series → series start (unchanged), not the first occurrence',
      '2026-10-01', 9, ['2026-10-05', '2026-10-12', '2026-10-19'], '2026-10-01'],
    ['one-off in the past (no upcoming) → its own date (opens read-only, as before)',
      '2026-06-09', 9, null, '2026-06-09'],
    ['one-off in the past (upcoming undefined) → its own date',
      '2026-06-09', 9, undefined, '2026-06-09'],
    ['ended series (upcoming empty) → series start',
      '2026-01-01', 9, [], '2026-01-01'],
    ['ended series whose only upcoming date has already started → series start',
      '2026-09-01', 9, ['2026-09-16', '2026-09-17'], '2026-09-01'],
  ])('%s', (_case, taskDate, startHour, upcoming, expected) => {
    expect(resolveEditOccurrenceDate(
      {taskDate: taskDate as string, startHour: startHour as number, upcomingOccurrenceDates: upcoming as string[] | null},
      NOW,
    )).toBe(expected);
  });

  it('honours fractional start hours (09:30 on today is past at 12:00, 12:30 is ahead)', () => {
    const upcoming = ['2026-09-17', '2026-09-18'];
    expect(resolveEditOccurrenceDate({taskDate: '2026-01-01', startHour: 9.5, upcomingOccurrenceDates: upcoming}, NOW))
      .toBe('2026-09-18');
    expect(resolveEditOccurrenceDate({taskDate: '2026-01-01', startHour: 12.5, upcomingOccurrenceDates: upcoming}, NOW))
      .toBe('2026-09-17');
  });

  it('returns an empty taskDate unchanged', () => {
    expect(resolveEditOccurrenceDate({taskDate: '', startHour: 9, upcomingOccurrenceDates: ['2026-09-18']}, NOW))
      .toBe('');
  });
});
