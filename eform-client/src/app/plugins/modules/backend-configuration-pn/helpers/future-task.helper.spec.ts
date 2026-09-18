import {
  copenhagenDateKey,
  isFutureLegacyDeadline,
  isFutureTask,
  taskDateKey,
} from './future-task.helper';

/**
 * #1300 — "future" means the task's date is after TODAY in Copenhagen
 * (date-level). Every cell passes `now` explicitly, so the matrix is
 * independent of the machine's clock and time zone.
 */
describe('future-task.helper (#1300)', () => {
  describe('copenhagenDateKey', () => {
    it.each([
      // CET (UTC+1)
      ['2026-01-15T22:59:00Z', '2026-01-15', 'CET 23:59'],
      ['2026-01-15T23:00:00Z', '2026-01-16', 'CET 00:00 — UTC date still the 15th'],
      ['2026-01-15T23:01:00Z', '2026-01-16', 'CET 00:01'],
      // CEST (UTC+2)
      ['2026-07-15T21:59:00Z', '2026-07-15', 'CEST 23:59'],
      ['2026-07-15T22:00:00Z', '2026-07-16', 'CEST 00:00 — UTC date still the 15th'],
      ['2026-07-15T22:01:00Z', '2026-07-16', 'CEST 00:01'],
      // Spring forward (2026-03-29, 02:00 CET → 03:00 CEST)
      ['2026-03-28T23:00:00Z', '2026-03-29', 'spring-forward day starts at 23:00 UTC'],
      ['2026-03-29T21:59:00Z', '2026-03-29', 'spring-forward day, 23:59 CEST'],
      ['2026-03-29T22:00:00Z', '2026-03-30', 'day after spring-forward starts at 22:00 UTC'],
      // Fall back (2026-10-25, 03:00 CEST → 02:00 CET)
      ['2026-10-24T22:00:00Z', '2026-10-25', 'fall-back day starts at 22:00 UTC'],
      ['2026-10-25T22:59:00Z', '2026-10-25', 'fall-back day, 23:59 CET'],
      ['2026-10-25T23:00:00Z', '2026-10-26', 'day after fall-back starts at 23:00 UTC'],
    ])('%s → %s (%s)', (utc, expected) => {
      expect(copenhagenDateKey(new Date(utc))).toBe(expected);
    });
  });

  describe('taskDateKey', () => {
    it.each([
      ['2026-09-18', '2026-09-18'],
      ['2026-09-18T00:00:00', '2026-09-18'],
      ['2026-09-18T00:00:00Z', '2026-09-18'],
      ['not a date', null],
      ['', null],
    ])('string %p → %p', (input, expected) => {
      expect(taskDateKey(input)).toBe(expected);
    });

    it('reads a Date by its UTC components (the server\'s midnight-UTC convention)', () => {
      expect(taskDateKey(new Date('2026-09-18T00:00:00Z'))).toBe('2026-09-18');
    });

    it('returns null for null, undefined and an invalid Date', () => {
      expect(taskDateKey(null)).toBeNull();
      expect(taskDateKey(undefined)).toBeNull();
      expect(taskDateKey(new Date('nope'))).toBeNull();
    });
  });

  describe('isFutureTask', () => {
    // 12:00 in Copenhagen on 2026-09-18.
    const noon = new Date('2026-09-18T10:00:00Z');

    it.each([
      ['yesterday', '2026-09-17', false],
      ['today', '2026-09-18', false],
      ['tomorrow', '2026-09-19', true],
      ['next month', '2026-10-18', true],
      ['next year, earlier month', '2027-01-01', true],
      ['last year, later month', '2025-12-31', false],
    ])('%s (%s) → %s', (_label, taskDate, expected) => {
      expect(isFutureTask(taskDate as string, noon)).toBe(expected);
    });

    it('is date-level: a task dated today is never future, however late its start hour', () => {
      expect(isFutureTask('2026-09-18', new Date('2026-09-17T22:00:00Z'))).toBe(false); // 00:00 local
      expect(isFutureTask('2026-09-18', new Date('2026-09-18T21:59:00Z'))).toBe(false); // 23:59 local
    });

    it('today late in the evening: tomorrow is still future until Copenhagen midnight', () => {
      expect(isFutureTask('2026-09-19', new Date('2026-09-18T21:59:00Z'))).toBe(true); // 23:59 CEST
      expect(isFutureTask('2026-09-19', new Date('2026-09-18T22:00:00Z'))).toBe(false); // 00:00 CEST
    });

    it.each([
      ['spring forward, 23:59 CET on the eve', '2026-03-29', '2026-03-28T22:59:00Z', true],
      ['spring forward, 00:00 CET', '2026-03-29', '2026-03-28T23:00:00Z', false],
      ['fall back, 23:59 CEST on the eve', '2026-10-25', '2026-10-24T21:59:00Z', true],
      ['fall back, 00:00 CEST', '2026-10-25', '2026-10-24T22:00:00Z', false],
    ])('DST: %s', (_label, taskDate, now, expected) => {
      expect(isFutureTask(taskDate as string, new Date(now as string))).toBe(expected);
    });

    it('accepts a Date (server timestamp parsed as UTC midnight)', () => {
      expect(isFutureTask(new Date('2026-09-19T00:00:00Z'), noon)).toBe(true);
      expect(isFutureTask(new Date('2026-09-18T00:00:00Z'), noon)).toBe(false);
    });

    it('never treats an unreadable date as future', () => {
      expect(isFutureTask(null, noon)).toBe(false);
      expect(isFutureTask('garbage', noon)).toBe(false);
    });
  });

  describe('isFutureLegacyDeadline (displayed = Compliance.Deadline − 1 day)', () => {
    const noon = new Date('2026-09-18T10:00:00Z');

    it.each([
      ['displayed yesterday → task today', '2026-09-17T00:00:00Z', false],
      ['displayed the day before yesterday → task yesterday', '2026-09-16T00:00:00Z', false],
      ['displayed today → task tomorrow', '2026-09-18T00:00:00Z', true],
      ['displayed tomorrow → task in two days', '2026-09-19T00:00:00Z', true],
    ])('%s', (_label, displayed, expected) => {
      expect(isFutureLegacyDeadline(new Date(displayed as string), noon)).toBe(expected);
    });

    it('rolls over month and year ends', () => {
      // displayed 31 Dec → task 1 Jan
      expect(isFutureLegacyDeadline(new Date('2026-12-31T00:00:00Z'), new Date('2026-12-31T12:00:00Z'))).toBe(true);
      expect(isFutureLegacyDeadline(new Date('2026-12-31T00:00:00Z'), new Date('2027-01-01T12:00:00Z'))).toBe(false);
    });

    it('is false for a missing deadline', () => {
      expect(isFutureLegacyDeadline(null, noon)).toBe(false);
    });
  });
});
