import {ComplianceReportRowModel} from '../../../models';

/**
 * Date and week-grouping maths for the Detaljer view (#1165).
 *
 * Pure functions with no Angular dependency: the locale and the translated
 * word for "week" are passed in, so every rule below is unit-testable without
 * a TestBed (see the sibling `.spec.ts`).
 */

/** The empty-cell glyph, normalised across all three views (#1160). U+2013. */
export const COMPLIANCE_EMPTY_CELL = '–';

export interface ComplianceWeekGroup {
  /** Stable identity for trackBy: the ISO date of the week's Monday. */
  key: string;
  /** `{Month} {Year} Uge {N}`, all three parts taken from the Monday. */
  label: string;
  rows: ComplianceReportRowModel[];
}

/**
 * `taskDate` is `yyyy-MM-dd` on the wire. `new Date('2026-08-11')` parses that
 * as UTC midnight, which renders as the PREVIOUS day everywhere west of
 * Greenwich — a silent off-by-one in the date column, the week header and the
 * overdue test. Build a local-midnight Date from the parts instead.
 *
 * A full ISO timestamp is tolerated (the date half is taken) so a server that
 * ever serialises `DateTime` rather than a date string does not shift rows.
 */
export function parseTaskDate(taskDate: string): Date {
  const datePart = (taskDate ?? '').split('T')[0];
  const [y, m, d] = datePart.split('-').map((part) => parseInt(part, 10));
  const parsed = buildLocalDate(y, m, d);
  if (parsed === null) {
    // Unparseable input: fall back to today rather than an Invalid Date, which
    // would render as "Invalid Date" in every cell it reaches.
    return startOfLocalDay(new Date());
  }
  return parsed;
}

/**
 * `new Date(y, m - 1, d)` ROLLS OVER out-of-range parts instead of rejecting
 * them: `2026-13-05` becomes 5 January 2027 and `2026-02-31` becomes 3 March.
 * A corrupt row would then be filed under a plausible-looking but wrong week
 * rather than taking `parseTaskDate`'s documented unparseable path.
 *
 * So the parts are range-checked AND the constructed date is round-tripped:
 * only a date that reads back as the same y/m/d survives, which is what
 * rejects 31 February without a leap-year table. (The round-trip also rejects
 * NaN parts, and years 0-99, which the `Date` constructor would silently map
 * into the 1900s.)
 *
 * Returns `null` for anything it will not vouch for, so the caller owns the
 * fallback.
 */
function buildLocalDate(y: number, m: number, d: number): Date | null {
  if (!Number.isInteger(y) || !Number.isInteger(m) || !Number.isInteger(d)) {
    return null;
  }
  if (m < 1 || m > 12 || d < 1 || d > 31) {
    return null;
  }
  const date = new Date(y, m - 1, d);
  if (date.getFullYear() !== y || date.getMonth() !== m - 1 || date.getDate() !== d) {
    return null;
  }
  return date;
}

export function startOfLocalDay(date: Date): Date {
  const d = new Date(date);
  d.setHours(0, 0, 0, 0);
  return d;
}

/**
 * The Monday of `date`'s ISO week, at local midnight.
 * `getDay() || 7` maps Sunday (0) to 7, so Sunday belongs to the week that
 * started six days earlier rather than opening a new one.
 */
export function mondayOf(date: Date): Date {
  const d = startOfLocalDay(date);
  const day = d.getDay() || 7;
  d.setDate(d.getDate() - day + 1);
  return d;
}

/**
 * ISO-8601 week number. Carried forward verbatim from
 * `calendar-compliance-view.component.ts`'s `isoWeek()` — a correct
 * implementation that #1170 would otherwise delete along with that component.
 */
export function isoWeekNumber(date: Date): number {
  const d = new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()));
  const dayNum = d.getUTCDay() || 7;
  d.setUTCDate(d.getUTCDate() + 4 - dayNum);
  const year = d.getUTCFullYear();
  const yearStart = new Date(Date.UTC(year, 0, 1));
  return Math.ceil((((d.getTime() - yearStart.getTime()) / 86400000) + 1) / 7);
}

/** Local `yyyy-MM-dd`. */
export function toLocalIsoDate(date: Date): string {
  return `${date.getFullYear()}-${(date.getMonth() + 1).toString().padStart(2, '0')}-${date
    .getDate()
    .toString()
    .padStart(2, '0')}`;
}

/**
 * The week block's identity.
 *
 * #1165 quotes the prototype's key as `monday.getFullYear() + '-W' + weekNum`.
 * That form collides: Monday 1 Jan 2024 is ISO week 1 of 2024 and Monday
 * 30 Dec 2024 is ISO week 1 of 2025, and both render the key `2024-W1`. The
 * Monday's own ISO date carries the same information — it IS the week — and
 * cannot collide, so it is used instead. The rule the issue actually protects
 * ("keep the Monday's year, never the row's") is preserved: nothing here reads
 * the row's own year.
 */
export function complianceWeekKey(monday: Date): string {
  return toLocalIsoDate(monday);
}

/**
 * `{Month} {Year} {Week} {N}` — e.g. `August 2026 Uge 36`.
 *
 * Month, year AND week number all come from the week's Monday, so the week
 * straddling 31 August–6 September 2026 is headed `August 2026 Uge 36` for
 * every row in it. Deriving the month from the row's own date (what the
 * component being replaced does) lets whichever row happens to sort first
 * decide the header.
 */
export function formatComplianceWeekTitle(
  monday: Date,
  locale: string,
  weekWord: string
): string {
  const monthYear = monday.toLocaleDateString(locale, {month: 'long', year: 'numeric'});
  const capitalised = monthYear.charAt(0).toUpperCase() + monthYear.slice(1);
  return `${capitalised} ${weekWord} ${isoWeekNumber(monday)}`;
}

/** `Tirsdag 11. august` — weekday capitalised, month lowercase, no year. */
export function formatComplianceDayLabel(taskDate: string, locale: string): string {
  const label = parseTaskDate(taskDate).toLocaleDateString(locale, {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
  });
  return label.charAt(0).toUpperCase() + label.slice(1);
}

/** `08:00 - 09:00`, or `''` for an all-day row (which suppresses the cell). */
export function formatComplianceTimeRange(row: ComplianceReportRowModel): string {
  if (row.isAllDay) {
    return '';
  }
  const toHm = (hours: number) => {
    const hh = Math.floor(hours);
    const mm = Math.round((hours - hh) * 60);
    return `${hh.toString().padStart(2, '0')}:${mm.toString().padStart(2, '0')}`;
  };
  return `${toHm(row.startHour)} - ${toHm(row.startHour + row.duration)}`;
}

/**
 * Group a PAGE of rows into week blocks.
 *
 * Blocks open on a CHANGE of week key while walking the rows in the order the
 * server returned them (`taskDate` DESC, `startHour` ASC — #1161 owns that
 * ordering; nothing here re-sorts). Grouping is applied per page, so a week
 * straddling a page boundary yields one block at the bottom of page N and
 * another with the same header at the top of page N+1. That is intended:
 * with server-side paging the client only ever holds one page, and a repeated
 * header at a page break reads as correct rather than as a bug (#1165).
 */
export function groupRowsByWeek(
  rows: ComplianceReportRowModel[],
  locale: string,
  weekWord: string
): ComplianceWeekGroup[] {
  const groups: ComplianceWeekGroup[] = [];
  let lastKey: string | null = null;
  for (const row of rows ?? []) {
    const monday = mondayOf(parseTaskDate(row.taskDate));
    const key = complianceWeekKey(monday);
    if (key !== lastKey) {
      groups.push({
        key,
        label: formatComplianceWeekTitle(monday, locale, weekWord),
        rows: [],
      });
      lastKey = key;
    }
    groups[groups.length - 1].rows.push(row);
  }
  return groups;
}

/**
 * The shared #1160 overdue rule: not completed AND due STRICTLY before today.
 * A task due today and not done lowers the compliance percentage but is not
 * overdue.
 *
 * #1165's row anatomy defines no overdue affordance, so the Detaljer view
 * renders nothing from this — it is here because the rule belongs with the
 * other date maths of this page, and #1164's `Overskredet` column is the
 * consumer. Kept tested so it cannot be re-derived wrong.
 */
export function isTaskOverdue(row: ComplianceReportRowModel, today: Date = new Date()): boolean {
  if (row.completed) {
    return false;
  }
  return parseTaskDate(row.taskDate).getTime() < startOfLocalDay(today).getTime();
}
