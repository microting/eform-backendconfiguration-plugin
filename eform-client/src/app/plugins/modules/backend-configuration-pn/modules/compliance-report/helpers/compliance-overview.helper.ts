import {ComplianceReportOverviewRowModel} from '../../../models';
import {COMPLIANCE_EMPTY_CELL} from './compliance-week-grouping';

/**
 * Presentation maths for the Oversigt view (#1164) — sorting, formatting and
 * banding. Pure and DOM-free, so every rule below is unit-testable without a
 * TestBed (see the sibling `.spec.ts`).
 *
 * WHAT IS DELIBERATELY ABSENT: the prototype's `buildCompanySummaries`.
 * `compliancePct`, `overdue`, `dueTotal`/`dueDone` and the WEIGHTED totals are
 * computed server-side by #1162's aggregation and read straight off the
 * response — nothing here recomputes any of them. That is what makes #1169's
 * export render the same numbers as the screen: one implementation of the
 * maths, not two. Its cases live in
 * `BackendConfiguration.Pn.Integration.Test/ComplianceReportOverviewTests.cs`.
 *
 * The thresholds, the `–` and the band names stay on the CLIENT for the
 * opposite reason: they are how the numbers are painted, and the API carries no
 * Danish display strings and no CSS class names.
 */

/** Below this the compliance pill is `low`. Boundary pinned by tests: 49 low, 50 mid. */
export const COMPLIANCE_MID_MIN = 50;
/** At or above this the pill is `high`. Boundary pinned by tests: 79 mid, 80 high. */
export const COMPLIANCE_HIGH_MIN = 80;
/** At or above this the overdue count is `urgent`. 5 mild, 6 urgent. */
export const OVERDUE_URGENT_MIN = 6;

export type ComplianceOverviewSortKey = 'propertyName' | 'overdue' | 'compliancePct';
export type ComplianceSortDirection = 'asc' | 'desc';

export interface ComplianceOverviewSort {
  key: ComplianceOverviewSortKey;
  direction: ComplianceSortDirection;
}

export interface ComplianceOverviewColumn {
  key: ComplianceOverviewSortKey;
  /** i18n key, not a display string. */
  label: string;
  numeric: boolean;
}

/**
 * Exactly three columns, in this order. `total` / `done` / `dueTotal` /
 * `dueDone` exist on the row model and feed the server-side maths, but the
 * `Opgaver i alt` and `Udført` columns were removed on purpose and must not
 * come back (a prototype test pins their absence).
 *
 * `Property` and `Overdue` are EXISTING plugin i18n keys — Danish `Ejendom` and
 * `Overskredet`. `Ejendom` rather than the prototype's `Virksomhed` is a
 * decision carried over from #1169's export (recorded on #1164): the plugin's
 * Danish dictionary already says `Property: 'Ejendom'`, and a user must not see
 * one word on screen and another in the file they download from that screen.
 */
export const OVERVIEW_COLUMNS: ComplianceOverviewColumn[] = [
  {key: 'propertyName', label: 'Property', numeric: false},
  {key: 'overdue', label: 'Overdue', numeric: true},
  {key: 'compliancePct', label: 'Compliance %', numeric: true},
];

/**
 * The landing sort: worst compliance first. Intended, not accidental — the
 * point of Oversigt is to put the properties that need attention at the top.
 */
export function initialOverviewSort(): ComplianceOverviewSort {
  return {key: 'compliancePct', direction: 'asc'};
}

/**
 * The header cycle, carried from `bindOverviewSortButtons`
 * (compliance.js:1465-1480): clicking the ACTIVE column flips direction;
 * clicking a different one starts `asc` for the name and `desc` for the two
 * numeric columns — because "most overdue" and "worst compliance" are what a
 * reader wants first from each.
 *
 * Two states, never three. `mat-sort-header`'s asc → desc → none cycle (which
 * also always starts `asc`) is why the headers here are hand-rolled buttons.
 */
export function nextOverviewSort(
  current: ComplianceOverviewSort,
  key: ComplianceOverviewSortKey
): ComplianceOverviewSort {
  if (current.key === key) {
    return {key, direction: current.direction === 'asc' ? 'desc' : 'asc'};
  }
  return {key, direction: key === 'propertyName' ? 'asc' : 'desc'};
}

/**
 * `Ejendom 10` must sort AFTER `Ejendom 9`, so the collation is Danish with
 * `numeric: true`. A plain `<`/`>` compare, or a collator without `numeric`,
 * silently reverses that pair.
 */
function compareNames(a: string | null, b: string | null): number {
  return String(a ?? '').localeCompare(String(b ?? ''), 'da', {numeric: true});
}

/**
 * Returns a COPY — `sortSummaries` never mutates its input, so the response
 * array stays in the order the server sent it and a re-sort is always a pure
 * function of (rows, sort). A test pins the non-mutation.
 *
 * `null` percentages sort LAST in both directions. The null checks run BEFORE
 * the direction multiplier, which is the whole trick: multiplying them would
 * float the nulls to the top on `desc`, and "we have no number for this
 * property yet" is never the most interesting row on the screen.
 *
 * Ties break on `propertyName`, ALWAYS ascending — the tie-break is
 * deliberately not direction-multiplied, so flipping the percentage column does
 * not also scramble the alphabetical order within each percentage.
 */
export function sortSummaries(
  rows: ComplianceReportOverviewRowModel[] | null | undefined,
  key: ComplianceOverviewSortKey,
  direction: ComplianceSortDirection
): ComplianceReportOverviewRowModel[] {
  const sortKey: ComplianceOverviewSortKey = key || 'compliancePct';
  const dir = direction === 'desc' ? -1 : 1;

  return (rows || []).slice().sort((a, b) => {
    const av = a[sortKey];
    const bv = b[sortKey];

    if (av == null && bv == null) {
      return compareNames(a.propertyName, b.propertyName);
    }
    if (av == null) {
      return 1;
    }
    if (bv == null) {
      return -1;
    }

    const cmp =
      typeof av === 'string' || typeof bv === 'string'
        ? compareNames(av as string, bv as string)
        : (av as number) - (bv as number);

    if (cmp !== 0) {
      return cmp * dir;
    }
    return compareNames(a.propertyName, b.propertyName);
  });
}

/**
 * `null` renders the en dash `–` (U+2013) — never `0`, never `0%`, never `NaN`.
 * The distinction is the point: `0%` says "nothing was done", `–` says "nothing
 * has fallen due yet".
 *
 * U+2013 is the glyph normalised across all three views (#1160); it comes from
 * `COMPLIANCE_EMPTY_CELL` so the Detaljer and Oversigt empty cells cannot
 * drift apart.
 */
export function formatCompliancePercent(pct: number | null | undefined): string {
  return pct == null ? COMPLIANCE_EMPTY_CELL : `${pct}%`;
}

export type ComplianceLevel = 'none' | 'low' | 'mid' | 'high';
export type OverdueLevel = 'calm' | 'mild' | 'urgent';

export function complianceLevel(pct: number | null | undefined): ComplianceLevel {
  if (pct == null) {
    return 'none';
  }
  if (pct < COMPLIANCE_MID_MIN) {
    return 'low';
  }
  if (pct < COMPLIANCE_HIGH_MIN) {
    return 'mid';
  }
  return 'high';
}

/**
 * `!count` rather than `count === 0` deliberately: a missing or null counter is
 * `calm`, not `mild`. Carried verbatim from the prototype.
 */
export function overdueLevel(count: number | null | undefined): OverdueLevel {
  if (!count) {
    return 'calm';
  }
  if (count < OVERDUE_URGENT_MIN) {
    return 'mild';
  }
  return 'urgent';
}
