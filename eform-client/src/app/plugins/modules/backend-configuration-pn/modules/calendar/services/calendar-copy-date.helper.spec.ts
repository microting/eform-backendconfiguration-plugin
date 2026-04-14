import {computeCopyDate} from './calendar-copy-date.helper';

// All `now` fixtures use local-time constructor so that getDay/getHours align
// with what the production helper sees in the browser.
function localDate(y: number, m: number, d: number, hh = 0, mm = 0): Date {
  return new Date(y, m - 1, d, hh, mm, 0, 0);
}

describe('computeCopyDate', () => {
  it('bumps a past date to today', () => {
    const now = localDate(2026, 4, 14, 12, 0); // Tuesday, 12:00
    expect(computeCopyDate('2026-04-10', 9.0, now)).toBe('2026-04-14');
  });

  it('bumps to today even when source start hour is later than now', () => {
    const now = localDate(2026, 4, 14, 8, 0);
    expect(computeCopyDate('2026-04-13', 14.0, now)).toBe('2026-04-14');
  });

  it('bumps to tomorrow when source is today and start hour already passed', () => {
    const now = localDate(2026, 4, 14, 12, 0);
    expect(computeCopyDate('2026-04-14', 9.0, now)).toBe('2026-04-15');
  });

  it('keeps today when source is today and start hour is later than now', () => {
    const now = localDate(2026, 4, 14, 9, 0);
    expect(computeCopyDate('2026-04-14', 14.0, now)).toBe('2026-04-14');
  });

  it('keeps today when source is today and start hour exactly equals now', () => {
    const now = localDate(2026, 4, 14, 10, 30);
    expect(computeCopyDate('2026-04-14', 10.5, now)).toBe('2026-04-14');
  });

  it('keeps a future date unchanged', () => {
    const now = localDate(2026, 4, 14, 12, 0);
    expect(computeCopyDate('2026-04-20', 9.0, now)).toBe('2026-04-20');
  });

  it('keeps a far-future date unchanged', () => {
    const now = localDate(2026, 4, 14, 12, 0);
    expect(computeCopyDate('2030-01-01', 9.0, now)).toBe('2030-01-01');
  });

  it('rolls over end of month when bumping to tomorrow', () => {
    const now = localDate(2026, 4, 30, 12, 0);
    expect(computeCopyDate('2026-04-30', 9.0, now)).toBe('2026-05-01');
  });

  it('rolls over end of year when bumping to tomorrow', () => {
    const now = localDate(2026, 12, 31, 23, 30);
    expect(computeCopyDate('2026-12-31', 9.0, now)).toBe('2027-01-01');
  });

  it('handles leap-day source today before now', () => {
    const now = localDate(2024, 2, 29, 14, 0); // 2024 is a leap year
    expect(computeCopyDate('2024-02-29', 9.0, now)).toBe('2024-03-01');
  });

  it('handles leap-day source date in past', () => {
    const now = localDate(2024, 3, 5, 12, 0);
    expect(computeCopyDate('2024-02-29', 9.0, now)).toBe('2024-03-05');
  });

  it('uses default new Date() when now is not supplied', () => {
    // sanity check: pass a far-future source so the result is unchanged regardless of now
    const farFuture = '2099-12-31';
    expect(computeCopyDate(farFuture, 9.0)).toBe(farFuture);
  });

  it('treats sourceStartHour at fractional minute boundaries correctly', () => {
    const now = localDate(2026, 4, 14, 10, 29); // 10.483
    // sourceStartHour = 10.5 (10:30) > 10.483 → today unchanged
    expect(computeCopyDate('2026-04-14', 10.5, now)).toBe('2026-04-14');
  });

  it('bumps when sourceStartHour is just barely past now', () => {
    const now = localDate(2026, 4, 14, 10, 31); // 10.5166...
    // sourceStartHour = 10.5 (10:30) < 10.51 → tomorrow
    expect(computeCopyDate('2026-04-14', 10.5, now)).toBe('2026-04-15');
  });
});
