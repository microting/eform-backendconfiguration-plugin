import {ComplianceReportRowModel} from '../../../models';
import {
  COMPLIANCE_EMPTY_CELL,
  complianceWeekKey,
  formatComplianceDayLabel,
  formatComplianceTimeRange,
  formatComplianceWeekTitle,
  groupRowsByWeek,
  isTaskOverdue,
  isoWeekNumber,
  mondayOf,
  parseTaskDate,
} from './compliance-week-grouping';

// en-GB rather than da-DK: the assertions are about WHICH date the month name
// is taken from, not about Danish, and en-GB month names are available in
// every ICU build jest might run against.
const LOCALE = 'en-GB';
const WEEK = 'Week';

function row(taskDate: string, over: Partial<ComplianceReportRowModel> = {}): ComplianceReportRowModel {
  return {
    complianceId: 1,
    taskDate,
    startHour: 8,
    duration: 1,
    isAllDay: false,
    title: 'Task',
    propertyId: 1,
    propertyName: 'Property',
    boardId: 1,
    boardName: 'Board',
    tags: [],
    workerNames: [],
    completed: false,
    doneAt: null,
    sdkCaseId: 1,
    eformId: null,
    planningId: 1,
    areaRulePlanningId: 1,
    checkListId: null,
    ...over,
  };
}

describe('parseTaskDate', () => {
  it('parses yyyy-MM-dd as LOCAL midnight, not UTC', () => {
    const d = parseTaskDate('2026-08-11');
    expect(d.getFullYear()).toBe(2026);
    expect(d.getMonth()).toBe(7);
    expect(d.getDate()).toBe(11);
    expect(d.getHours()).toBe(0);
  });

  it('tolerates a full ISO timestamp by taking the date half', () => {
    const d = parseTaskDate('2026-08-11T00:00:00');
    expect(d.getDate()).toBe(11);
    expect(d.getMonth()).toBe(7);
  });
});

describe('mondayOf', () => {
  it('returns the same day for a Monday', () => {
    expect(mondayOf(parseTaskDate('2026-08-31')).getDate()).toBe(31);
  });

  it('walks a Sunday BACK to its Monday, not forward', () => {
    // 6 September 2026 is a Sunday; getDay() is 0 and must map to 7.
    const monday = mondayOf(parseTaskDate('2026-09-06'));
    expect(monday.getMonth()).toBe(7); // August
    expect(monday.getDate()).toBe(31);
  });

  it('walks a mid-week day back', () => {
    const monday = mondayOf(parseTaskDate('2026-08-11')); // Tuesday
    expect(monday.getDate()).toBe(10);
  });
});

describe('isoWeekNumber', () => {
  it('numbers the 31 August 2026 week as 36', () => {
    expect(isoWeekNumber(parseTaskDate('2026-08-31'))).toBe(36);
  });

  it('numbers 29 December 2025 as week 1 (of the next ISO year)', () => {
    expect(isoWeekNumber(parseTaskDate('2025-12-29'))).toBe(1);
  });
});

describe('formatComplianceWeekTitle', () => {
  it('is {Month} {Year} {Week} {N}, capitalised', () => {
    expect(formatComplianceWeekTitle(parseTaskDate('2026-08-10'), LOCALE, WEEK))
      .toBe('August 2026 Week 33');
  });
});

describe('groupRowsByWeek', () => {
  it('takes the header month/year from the week MONDAY, not from each row', () => {
    // The week of 31 Aug – 6 Sep 2026 straddles a month boundary. Every row in
    // it, including the September ones, must be headed "August 2026 Week 36".
    const groups = groupRowsByWeek(
      [row('2026-09-06'), row('2026-09-01'), row('2026-08-31')],
      LOCALE,
      WEEK
    );
    expect(groups).toHaveLength(1);
    expect(groups[0].label).toBe('August 2026 Week 36');
    expect(groups[0].rows).toHaveLength(3);
  });

  it('keeps a week straddling the year boundary in ONE block, named after the Monday', () => {
    const groups = groupRowsByWeek(
      [row('2026-01-04'), row('2026-01-01'), row('2025-12-29')],
      LOCALE,
      WEEK
    );
    expect(groups).toHaveLength(1);
    expect(groups[0].label).toBe('December 2025 Week 1');
  });

  it('does not collide two different weeks that share a year and a week number', () => {
    // Monday 1 Jan 2024 and Monday 30 Dec 2024 are both ISO week 1 with
    // getFullYear() === 2024. A `${year}-W${week}` key merges them.
    expect(complianceWeekKey(parseTaskDate('2024-12-30')))
      .not.toBe(complianceWeekKey(parseTaskDate('2024-01-01')));
    const groups = groupRowsByWeek([row('2024-12-30'), row('2024-01-01')], LOCALE, WEEK);
    expect(groups).toHaveLength(2);
  });

  it('opens a new block on every change of week', () => {
    const groups = groupRowsByWeek(
      [row('2026-08-31'), row('2026-08-28'), row('2026-08-21')],
      LOCALE,
      WEEK
    );
    expect(groups.map((g) => g.label)).toEqual([
      'August 2026 Week 36',
      'August 2026 Week 35',
      'August 2026 Week 34',
    ]);
  });

  it('splits one week across two pages, repeating the header (intended)', () => {
    const week = [
      row('2026-09-06'), row('2026-09-05'), row('2026-09-04'), row('2026-09-03'),
      row('2026-09-02'), row('2026-09-01'), row('2026-08-31'),
    ];
    const pageOne = groupRowsByWeek(week.slice(0, 4), LOCALE, WEEK);
    const pageTwo = groupRowsByWeek(week.slice(4), LOCALE, WEEK);
    expect(pageOne).toHaveLength(1);
    expect(pageTwo).toHaveLength(1);
    expect(pageOne[0].label).toBe(pageTwo[0].label);
    expect(pageOne[0].rows).toHaveLength(4);
    expect(pageTwo[0].rows).toHaveLength(3);
  });

  it('preserves the server ordering — it never re-sorts', () => {
    // Fed in the DOCUMENTED server order (`taskDate` DESC). Ascending input
    // would be no test at all: a helper that sorted ascending would return it
    // untouched and still pass.
    const groups = groupRowsByWeek(
      [row('2026-09-02', {title: 'newer'}), row('2026-08-31', {title: 'older'})],
      LOCALE,
      WEEK
    );
    // Both dates fall in the same ISO week (Mon 31 Aug 2026), so one block.
    expect(groups).toHaveLength(1);
    expect(groups[0].rows.map((r) => r.title)).toEqual(['newer', 'older']);
  });

  it('returns nothing for an empty page', () => {
    expect(groupRowsByWeek([], LOCALE, WEEK)).toEqual([]);
  });

  it('treats a null/undefined page as empty rather than throwing', () => {
    // The component always passes an array, but `rows ?? []` is load-bearing:
    // a failed response path that handed in null would otherwise crash the
    // whole view instead of rendering nothing.
    expect(groupRowsByWeek(null as unknown as ComplianceReportRowModel[], LOCALE, WEEK)).toEqual([]);
    expect(
      groupRowsByWeek(undefined as unknown as ComplianceReportRowModel[], LOCALE, WEEK)
    ).toEqual([]);
  });
});

describe('formatComplianceDayLabel', () => {
  it('is {Weekday} {D} {month}, weekday capitalised and no year', () => {
    expect(formatComplianceDayLabel('2026-08-11', LOCALE)).toBe('Tuesday 11 August');
  });
});

describe('formatComplianceTimeRange', () => {
  it('renders HH:MM - HH:MM', () => {
    expect(formatComplianceTimeRange(row('2026-08-11', {startHour: 8, duration: 1})))
      .toBe('08:00 - 09:00');
  });

  it('renders half hours', () => {
    expect(formatComplianceTimeRange(row('2026-08-11', {startHour: 8.5, duration: 1.25})))
      .toBe('08:30 - 09:45');
  });

  it('is empty for an all-day row, which suppresses the cell', () => {
    expect(formatComplianceTimeRange(row('2026-08-11', {isAllDay: true}))).toBe('');
  });
});

describe('isTaskOverdue', () => {
  const today = new Date(2026, 7, 11, 14, 30);

  it('is true for a not-done task due strictly before today', () => {
    expect(isTaskOverdue(row('2026-08-10'), today)).toBe(true);
  });

  it('is FALSE for a not-done task due today, however late in the day', () => {
    expect(isTaskOverdue(row('2026-08-11'), today)).toBe(false);
  });

  it('is false for a future task', () => {
    expect(isTaskOverdue(row('2026-08-12'), today)).toBe(false);
  });

  it('is false for a completed task, however old', () => {
    expect(isTaskOverdue(row('2020-01-01', {completed: true}), today)).toBe(false);
  });
});

describe('parseTaskDate — unparseable input', () => {
  // Pins the CURRENT behaviour, which is a deliberate trade: an Invalid Date
  // would render the literal string "Invalid Date" in every cell it reaches,
  // so the helper falls back to local midnight TODAY. The cost is that a
  // corrupt row is silently filed under the CURRENT week rather than standing
  // out — worth flagging if the wire format ever becomes unreliable, but it is
  // what the view does today and is asserted, not changed, here.
  const cases = ['', '   ', 'not-a-date', '2026-13', 'T08:00:00'];

  it.each(cases)('falls back to local midnight today for %p', (input) => {
    const today = new Date();
    today.setHours(0, 0, 0, 0);
    expect(parseTaskDate(input).getTime()).toBe(today.getTime());
  });

  it('files a corrupt row under the current week rather than throwing', () => {
    const groups = groupRowsByWeek([row('not-a-date')], LOCALE, WEEK);
    expect(groups).toHaveLength(1);
    expect(groups[0].key).toBe(complianceWeekKey(mondayOf(new Date())));
  });

  it('still parses a full ISO timestamp by its date half', () => {
    expect(parseTaskDate('2026-08-11T22:30:00Z').getTime())
      .toBe(new Date(2026, 7, 11).getTime());
  });
});

describe('parseTaskDate — out-of-range parts do NOT roll over', () => {
  // `new Date(y, m - 1, d)` normalises silently: month 13 walks into the next
  // year and 31 February into March. That would file a corrupt row under a
  // plausible-looking but WRONG week instead of taking the unparseable path,
  // which is the one the helper documents.
  function today(): number {
    const t = new Date();
    t.setHours(0, 0, 0, 0);
    return t.getTime();
  }

  it('rejects month 13 rather than rolling into the next January', () => {
    const d = parseTaskDate('2026-13-05');
    expect(d.getTime()).toBe(today());
    // The rollover would have been 5 January 2027.
    expect(d.getTime()).not.toBe(new Date(2027, 0, 5).getTime());
  });

  it('rejects 31 February rather than rolling into March', () => {
    const d = parseTaskDate('2026-02-31');
    expect(d.getTime()).toBe(today());
    // The rollover would have been 3 March 2026.
    expect(d.getTime()).not.toBe(new Date(2026, 2, 3).getTime());
  });

  it.each(['2026-00-10', '2026-08-00', '2026-08-32', '2026-02-30'])(
    'falls back to today for the out-of-range date %p',
    (input) => {
      expect(parseTaskDate(input).getTime()).toBe(today());
    }
  );

  it('files an out-of-range row under the current week, like any corrupt row', () => {
    const groups = groupRowsByWeek([row('2026-13-05')], LOCALE, WEEK);
    expect(groups).toHaveLength(1);
    expect(groups[0].key).toBe(complianceWeekKey(mondayOf(new Date())));
  });

  it.each(['2026-01-31', '2026-12-31', '2024-02-29'])(
    'still accepts the in-range boundary date %p',
    (input) => {
      const [y, m, d] = input.split('-').map((p) => parseInt(p, 10));
      expect(parseTaskDate(input).getTime()).toBe(new Date(y, m - 1, d).getTime());
    }
  );

  it('rejects 29 February in a NON-leap year', () => {
    expect(parseTaskDate('2026-02-29').getTime()).toBe(today());
  });
});

describe('COMPLIANCE_EMPTY_CELL', () => {
  it('is an en dash (U+2013), not a hyphen or an em dash', () => {
    expect(COMPLIANCE_EMPTY_CELL).toBe('–');
  });
});
