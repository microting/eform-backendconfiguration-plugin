import {assigneeDisplayNames, mapRepeatType, mapResponseToCalendarTask} from './calendar-task.mapper';
import {CalendarTaskModel} from '../../../models/calendar';

// Regression-pinning for the repeat-type wire encoding → CalendarRepeatRule
// classification. The backend conversion persists (repeatType, repeatEvery)
// integer pairs; mapRepeatType is the single derivation point both the
// week-grid and the task-list page share. Each test asserts the REAL
// function's current behaviour for one cell of the input table — including
// the deliberate collapse of every repeatEvery > 1 rule to 'custom', which
// reconstructMetaFromTask later refines (daily→everyNd, weekly→everyNWeekOne,
// monthly-ordinal→everyNMonthFirstWeekday, …).
describe('mapRepeatType', () => {
  it('(0, 1) → "none" (repeatType 0 means no repetition)', () => {
    expect(mapRepeatType(0, 1)).toBe('none');
  });

  it('(0, 5) → "none" (repeatEvery is ignored when repeatType is 0)', () => {
    expect(mapRepeatType(0, 5)).toBe('none');
  });

  it('(1, 0) → "custom" (pins current behaviour: repeatEvery=0 is not treated as 1)', () => {
    expect(mapRepeatType(1, 0)).toBe('custom');
  });

  it('(1, 1) → "daily"', () => {
    expect(mapRepeatType(1, 1)).toBe('daily');
  });

  it('(1, 4) → "custom" (every-N-days routes through the custom branch)', () => {
    expect(mapRepeatType(1, 4)).toBe('custom');
  });

  it('(2, 1) → "weeklyOne"', () => {
    expect(mapRepeatType(2, 1)).toBe('weeklyOne');
  });

  it('(2, 2) → "custom" (every-N-weeks routes through the custom branch)', () => {
    expect(mapRepeatType(2, 2)).toBe('custom');
  });

  it('(3, 1) → "monthlyDom"', () => {
    expect(mapRepeatType(3, 1)).toBe('monthlyDom');
  });

  it('(3, 6) → "custom" (every-N-months routes through the custom branch)', () => {
    expect(mapRepeatType(3, 6)).toBe('custom');
  });

  it('(4, 1) → "yearlyOne"', () => {
    expect(mapRepeatType(4, 1)).toBe('yearlyOne');
  });

  it('(4, 2) → "custom" (every-N-years routes through the custom branch)', () => {
    expect(mapRepeatType(4, 2)).toBe('custom');
  });

  it('unknown repeatType (5, 1) → "custom" (default branch)', () => {
    expect(mapRepeatType(5, 1)).toBe('custom');
  });

  it('nullish repeatType → "none" (falsy guard, pinned via runtime cast)', () => {
    // The signature declares number, but wire data could be absent; the
    // `!repeatType` guard handles it. Cast so the runtime path is exercised.
    expect(mapRepeatType(null as unknown as number, 1)).toBe('none');
    expect(mapRepeatType(undefined as unknown as number, 1)).toBe('none');
  });
});

describe('mapResponseToCalendarTask', () => {
  it('derives repeatRule from the DTO repeat integers', () => {
    const task = mapResponseToCalendarTask({id: 1, repeatType: 2, repeatEvery: 1});
    expect(task.repeatRule).toBe('weeklyOne');
  });

  it('missing repeatType defaults to 0 → "none"', () => {
    const task = mapResponseToCalendarTask({id: 1});
    expect(task.repeatRule).toBe('none');
  });

  it('missing repeatEvery defaults to 1 → built-in rule, not "custom"', () => {
    const task = mapResponseToCalendarTask({id: 1, repeatType: 3});
    expect(task.repeatRule).toBe('monthlyDom');
  });

  it('preserves all other DTO fields verbatim (spread)', () => {
    const task = mapResponseToCalendarTask({
      id: 7, title: 'x', repeatType: 1, repeatEvery: 4, repeatOrdinalWeek: 1,
    });
    expect(task.id).toBe(7);
    expect(task.title).toBe('x');
    expect(task.repeatOrdinalWeek).toBe(1);
    expect(task.repeatRule).toBe('custom');
  });
});

// #1385 — the task list's "Assigned to" column: workers, then teams.
describe('assigneeDisplayNames', () => {
  const task = (workerNames?: string[], workerTagNames?: string[]) =>
    ({workerNames, workerTagNames} as CalendarTaskModel);

  it('shows the team of a team-only task instead of nothing', () => {
    expect(assigneeDisplayNames(task([], ['Service team']))).toEqual(['Service team']);
  });

  it('lists the workers first, then the teams', () => {
    expect(assigneeDisplayNames(task(['Jane Doe'], ['Service team']))).toEqual(['Jane Doe', 'Service team']);
  });

  it('drops a team the server could not name and tolerates missing lists', () => {
    expect(assigneeDisplayNames(task(['Jane Doe'], ['', 'Night shift']))).toEqual(['Jane Doe', 'Night shift']);
    expect(assigneeDisplayNames(task())).toEqual([]);
  });
});
