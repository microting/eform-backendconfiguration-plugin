import {ComplianceReportOverviewRowModel} from '../../../models';
import {
  COMPLIANCE_HIGH_MIN,
  COMPLIANCE_MID_MIN,
  OVERDUE_URGENT_MIN,
  OVERVIEW_COLUMNS,
  complianceLevel,
  formatCompliancePercent,
  initialOverviewSort,
  nextOverviewSort,
  overdueLevel,
  sortSummaries,
} from './compliance-overview.helper';

/**
 * The SORTING, FORMATTING and BANDING half of the prototype's
 * `lorem-ipsum/kalender/tests/compliance-overview.test.js`.
 *
 * THE REAL NUMBERS, so nobody has to take a round one on trust. This file has
 * **22** `it()` cases. The prototype has **24** `test()` cases, and they are NOT
 * the same 24 — the port is neither a subset nor a superset. The prototype's 24
 * break down as:
 *
 *  - **10 `buildCompanySummaries` (aggregation) cases** — grouping, the overdue
 *    boundary, future tasks leaving the denominator alone, `null` vs NaN, the
 *    33/41 → 80 rounding and the weighted totals. NOT dropped and NOT duplicated
 *    here: the aggregation moved server-side (#1162), so they live where the
 *    maths lives, in
 *    `BackendConfiguration.Pn.Integration.Test/ComplianceReportOverviewTests.cs`
 *    (`..._GroupsCasesByPropertyAndCountsDone`,
 *    `..._OverdueCountsOnlyIncompleteCasesDatedBeforeToday`,
 *    `..._FutureTasksIgnored_EighteenDoneAndOneUpcomingIsHundredPercent`,
 *    `..._OnlyFutureTasks_HasNullPercentageNotZero`,
 *    `..._Rounding_ThirtyThreeOfFortyOneDue_IsEighty`,
 *    `..._TotalsAreWeighted_NotAnAverageOfRowPercentages`, and the rest).
 *    Re-asserting them in TypeScript would mean a second implementation of the
 *    maths on the client, which is exactly what #1164 forbids.
 *  - **6 `renderOverviewTable` string-building cases** — `renders one row per
 *    company plus a totals row`, `renders only Virksomhed, Overskredet and
 *    Compliance %, in that order`, `render marks the sorted column and applies
 *    level classes`, `render escapes company names`, `render shows an empty state
 *    when there are no rows`, `render preserves the row order it is given rather
 *    than sorting internally`. The render is an Angular template, not a string
 *    builder, so none of them ports verbatim. The second one's guarantee IS
 *    ported, onto `OVERVIEW_COLUMNS` in the first describe below; Angular's own
 *    interpolation escaping replaces the `escHtml` case; and the remaining four
 *    are asserted against the real DOM by the Playwright suite
 *    `playwright/e2e/plugins/backend-configuration-pn/s/compliance-overview.spec.ts`
 *    (column order, sorted-header marking, the empty state, row order).
 *  - **1 export case** — `export rows mirror the rendered table and end with
 *    totals`. That is #1169's, and #1169 is not merged; it belongs with the
 *    export, not here.
 *  - **7 sorting / formatting / banding cases** — the only ones this file is a
 *    port OF. With the columns case above that makes 8 prototype cases with a
 *    counterpart here; the other 14 of the 22 are new, covering behaviour the
 *    prototype never pinned (the two-state `nextOverviewSort` cycle, the
 *    direction-independent name tie-break, `null` vs `undefined`, the en-dash
 *    codepoint, the threshold constants, a null row list).
 */

function summary(
  propertyId: number,
  propertyName: string,
  compliancePct: number | null,
  overdue = 0
): ComplianceReportOverviewRowModel {
  return {
    propertyId,
    propertyName,
    total: 1,
    done: 0,
    overdue,
    dueTotal: 1,
    dueDone: 0,
    compliancePct,
  };
}

describe('compliance-overview.helper — columns', () => {
  it('renders exactly Property, Overdue and Compliance %, in that order', () => {
    expect(OVERVIEW_COLUMNS.map((c) => c.key)).toEqual([
      'propertyName',
      'overdue',
      'compliancePct',
    ]);
    // i18n KEYS, not display strings. `Property` is Danish `Ejendom` and
    // `Overdue` is `Overskredet` in the plugin dictionary; the prototype's
    // `Virksomhed` is deliberately not used (see the helper's comment).
    expect(OVERVIEW_COLUMNS.map((c) => c.label)).toEqual([
      'Property',
      'Overdue',
      'Compliance %',
    ]);
  });

  it('marks the two numeric columns and only those', () => {
    expect(OVERVIEW_COLUMNS.map((c) => c.numeric)).toEqual([false, true, true]);
  });

  it('has no column for the counters the aggregation still returns', () => {
    const keys = OVERVIEW_COLUMNS.map((c) => c.key as string);
    expect(keys).not.toContain('total');
    expect(keys).not.toContain('done');
    expect(keys).not.toContain('dueTotal');
    expect(keys).not.toContain('dueDone');
  });
});

describe('compliance-overview.helper — sortSummaries', () => {
  it('sorts by compliance ascending with worst first', () => {
    const rows = [
      summary(1, 'Ejendom 1', 88),
      summary(9, 'Ejendom 9', 25, 1),
      summary(6, 'Ejendom 6', 40, 1),
    ];
    expect(sortSummaries(rows, 'compliancePct', 'asc').map((r) => r.propertyId)).toEqual([
      9, 6, 1,
    ]);
  });

  it('sorts by compliance descending with best first', () => {
    const rows = [
      summary(1, 'Ejendom 1', 88),
      summary(9, 'Ejendom 9', 25, 1),
      summary(6, 'Ejendom 6', 40, 1),
    ];
    expect(sortSummaries(rows, 'compliancePct', 'desc').map((r) => r.propertyId)).toEqual([
      1, 6, 9,
    ]);
  });

  it('does not mutate its input', () => {
    const rows = [summary(1, 'Ejendom 1', 88), summary(9, 'Ejendom 9', 25, 1)];
    sortSummaries(rows, 'compliancePct', 'asc');
    expect(rows[0].propertyId).toBe(1);
  });

  it('breaks ties on property name with numeric collation (Ejendom 10 after Ejendom 9)', () => {
    const rows = [
      summary(10, 'Ejendom 10', 61),
      summary(4, 'Ejendom 4', 61),
      summary(2, 'Ejendom 2', 61),
      summary(9, 'Ejendom 9', 61),
    ];
    expect(sortSummaries(rows, 'compliancePct', 'asc').map((r) => r.propertyId)).toEqual([
      2, 4, 9, 10,
    ]);
  });

  it('keeps the name tie-break ascending even when the direction is descending', () => {
    const rows = [
      summary(10, 'Ejendom 10', 61),
      summary(2, 'Ejendom 2', 61),
      summary(4, 'Ejendom 4', 61),
    ];
    expect(sortSummaries(rows, 'compliancePct', 'desc').map((r) => r.propertyId)).toEqual([
      2, 4, 10,
    ]);
  });

  it('sorts null percentages last in BOTH directions', () => {
    const rows = [summary(1, 'Ejendom 1', null), summary(2, 'Ejendom 2', 100)];
    expect(sortSummaries(rows, 'compliancePct', 'asc')[1].propertyId).toBe(1);
    expect(sortSummaries(rows, 'compliancePct', 'desc')[1].propertyId).toBe(1);
  });

  it('orders two null percentages by name rather than by input order', () => {
    const rows = [summary(10, 'Ejendom 10', null), summary(2, 'Ejendom 2', null)];
    expect(sortSummaries(rows, 'compliancePct', 'asc').map((r) => r.propertyId)).toEqual([2, 10]);
  });

  // NOT titled "numerically, not lexically": swapping the numeric branch in
  // `sortSummaries` for `compareNames` would NOT fail this case, because
  // `compareNames` collates with `{numeric: true}` and therefore orders '9'
  // before '10' exactly as `9 - 10` does. No non-negative integer input can
  // separate them, so the distinction is not assertable here at all — it is
  // pinned instead by the name cases, which is where the collator matters.
  // What this case DOES catch: sorting the wrong column, and a reversed
  // comparator.
  it('sorts by the overdue column, largest count first, on desc', () => {
    const rows = [
      summary(1, 'Ejendom 1', 50, 9),
      summary(2, 'Ejendom 2', 50, 10),
      summary(3, 'Ejendom 3', 50, 2),
    ];
    expect(sortSummaries(rows, 'overdue', 'desc').map((r) => r.propertyId)).toEqual([2, 1, 3]);
  });

  it('sorts by property name with the Danish numeric collator', () => {
    const rows = [
      summary(10, 'Ejendom 10', 1),
      summary(9, 'Ejendom 9', 2),
      summary(1, 'Ejendom 1', 3),
    ];
    expect(sortSummaries(rows, 'propertyName', 'asc').map((r) => r.propertyId)).toEqual([
      1, 9, 10,
    ]);
    expect(sortSummaries(rows, 'propertyName', 'desc').map((r) => r.propertyId)).toEqual([
      10, 9, 1,
    ]);
  });

  it('tolerates a null/undefined row list', () => {
    expect(sortSummaries(null, 'compliancePct', 'asc')).toEqual([]);
    expect(sortSummaries(undefined, 'compliancePct', 'asc')).toEqual([]);
  });
});

describe('compliance-overview.helper — sort state machine', () => {
  it('lands on compliancePct ascending, worst first', () => {
    expect(initialOverviewSort()).toEqual({key: 'compliancePct', direction: 'asc'});
  });

  it('flips the direction when the active key is clicked again', () => {
    const asc = {key: 'compliancePct', direction: 'asc'} as const;
    const desc = nextOverviewSort(asc, 'compliancePct');
    expect(desc).toEqual({key: 'compliancePct', direction: 'desc'});
    // Two states, never three: no "none" step back to unsorted.
    expect(nextOverviewSort(desc, 'compliancePct')).toEqual({
      key: 'compliancePct',
      direction: 'asc',
    });
  });

  it('starts a new key ascending for the name and descending for the numbers', () => {
    const from = {key: 'compliancePct', direction: 'desc'} as const;
    expect(nextOverviewSort(from, 'propertyName')).toEqual({
      key: 'propertyName',
      direction: 'asc',
    });
    expect(nextOverviewSort(from, 'overdue')).toEqual({key: 'overdue', direction: 'desc'});
    expect(
      nextOverviewSort({key: 'propertyName', direction: 'desc'}, 'compliancePct')
    ).toEqual({key: 'compliancePct', direction: 'desc'});
  });
});

describe('compliance-overview.helper — formatting', () => {
  it('formats percentages and renders null as the en dash', () => {
    expect(formatCompliancePercent(72)).toBe('72%');
    expect(formatCompliancePercent(0)).toBe('0%');
    expect(formatCompliancePercent(100)).toBe('100%');
    expect(formatCompliancePercent(null)).toBe('–');
    expect(formatCompliancePercent(undefined)).toBe('–');
  });

  it('uses the en dash U+2013, not the em dash U+2014 and not a hyphen', () => {
    // The prototype's Rapport view used U+2014 for the same idea; #1160
    // normalises every view on U+2013.
    expect(formatCompliancePercent(null)).not.toBe('—');
    expect(formatCompliancePercent(null)).not.toBe('-');
  });

  it('never renders a null percentage as zero', () => {
    expect(formatCompliancePercent(null)).not.toBe('0');
    expect(formatCompliancePercent(null)).not.toBe('0%');
  });
});

describe('compliance-overview.helper — banding', () => {
  it('classifies compliance into four bands at their boundaries', () => {
    expect(complianceLevel(null)).toBe('none');
    expect(complianceLevel(undefined)).toBe('none');
    expect(complianceLevel(0)).toBe('low');
    expect(complianceLevel(49)).toBe('low');
    expect(complianceLevel(50)).toBe('mid');
    expect(complianceLevel(79)).toBe('mid');
    expect(complianceLevel(80)).toBe('high');
    expect(complianceLevel(100)).toBe('high');
  });

  it('classifies overdue counts into three bands at their boundaries', () => {
    expect(overdueLevel(0)).toBe('calm');
    expect(overdueLevel(null)).toBe('calm');
    expect(overdueLevel(undefined)).toBe('calm');
    expect(overdueLevel(1)).toBe('mild');
    expect(overdueLevel(5)).toBe('mild');
    expect(overdueLevel(6)).toBe('urgent');
    expect(overdueLevel(999)).toBe('urgent');
  });

  it('keeps the three thresholds at the values the prototype pinned', () => {
    expect(COMPLIANCE_MID_MIN).toBe(50);
    expect(COMPLIANCE_HIGH_MIN).toBe(80);
    expect(OVERDUE_URGENT_MIN).toBe(6);
  });
});
