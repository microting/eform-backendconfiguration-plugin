import {dateOnlyToLocal, isOverdue, occupancyValidFrom, parseServerUtc, toDateOnly, toUtcMidnightIso} from './tail-bite-dates';

describe('tail-bite dates', () => {
  it('reads an offset-less server timestamp as UTC', () => {
    expect(parseServerUtc('2026-10-01T06:12:00')!.toISOString()).toBe('2026-10-01T06:12:00.000Z');
  });

  it('keeps an explicit Z or offset', () => {
    expect(parseServerUtc('2026-10-01T06:12:00Z')!.toISOString()).toBe('2026-10-01T06:12:00.000Z');
    expect(parseServerUtc('2026-10-01T08:12:00+02:00')!.toISOString()).toBe('2026-10-01T06:12:00.000Z');
    expect(parseServerUtc('2026-10-01T06:12:00.1234567Z')!.getUTCMinutes()).toBe(12);
  });

  it('returns null for empty or garbage input', () => {
    expect(parseServerUtc(null)).toBeNull();
    expect(parseServerUtc('')).toBeNull();
    expect(parseServerUtc('not a date')).toBeNull();
  });

  it('formats a picked day without shifting it', () => {
    const picked = new Date(2026, 9, 6, 23, 30);
    expect(toDateOnly(picked)).toBe('2026-10-06');
    expect(toUtcMidnightIso(picked)).toBe('2026-10-06T00:00:00Z');
  });

  it('dates a pig count from midnight UTC of the picked day', () => {
    expect(occupancyValidFrom(new Date(2026, 8, 15), new Date(Date.UTC(2026, 9, 6, 8, 0)))).toBe('2026-09-15T00:00:00Z');
  });

  it('never dates a pig count in the future: today at 00:30 in UTC+2 is 22:30 UTC the day before', () => {
    // The picked day is 6 October in the user's calendar; "now" is 00:30 on 6 October at UTC+2.
    const now = new Date(Date.UTC(2026, 9, 5, 22, 30));
    expect(occupancyValidFrom(new Date(2026, 9, 6), now)).toBe('2026-10-05T22:30:00.000Z');
  });

  it('flags a follow-up date before today as overdue, today itself is not', () => {
    const today = new Date(2026, 9, 6, 8, 0);
    expect(isOverdue('2026-10-05T00:00:00', today)).toBe(true);
    expect(isOverdue('2026-10-06T00:00:00', today)).toBe(false);
    expect(isOverdue('2026-10-07', today)).toBe(false);
  });

  it('turns a date-only value into a local date on the same day', () => {
    const d = dateOnlyToLocal('2026-10-06T00:00:00');
    expect([d.getFullYear(), d.getMonth(), d.getDate()]).toEqual([2026, 9, 6]);
  });
});
