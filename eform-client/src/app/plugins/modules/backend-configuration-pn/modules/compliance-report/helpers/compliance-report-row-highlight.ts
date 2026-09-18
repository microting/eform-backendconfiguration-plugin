/**
 * The Rapport view's "land the user on a row" mechanism (#1290 delete → next row;
 * #1291 edit → the edited row): which row, identified how, and for how long it is
 * highlighted. Kept framework-free so the rules are unit-testable without TestBed.
 */

/** How long a landed-on row keeps the `row-highlight-flash` class (the fade runs inside it). */
export const COMPLIANCE_REPORT_HIGHLIGHT_MS = 3000;

/** The class the Rapport grid puts on the highlighted `<tr>`. Styled in shared SCSS. */
export const COMPLIANCE_REPORT_HIGHLIGHT_CLASS = 'row-highlight-flash';

/**
 * The identity of a Rapport row across a RE-FETCH — the only thing that survives it,
 * since the whole view model is rebuilt from the response.
 *
 * The SDK case id where there is one: it is the row's own fact and what the case page
 * round-trips (#1291). The compliance id otherwise — a row without a case (never
 * deployed) has nothing else. The two are prefixed so a case id can never collide
 * with a compliance id of the same number.
 */
export type ComplianceReportRowKey = string;

export interface ComplianceReportRowIdentity {
  complianceId: number;
  sdkCaseId: number;
}

export function complianceReportRowKey(row: ComplianceReportRowIdentity): ComplianceReportRowKey {
  return row.sdkCaseId > 0 ? `case:${row.sdkCaseId}` : `compliance:${row.complianceId}`;
}

/**
 * The row to land on after `deleted` is removed: the one AFTER it in display order
 * (`rows` is every row of every table of every section, in the order they render —
 * crossing into the next table/section when `deleted` ends one), or, when `deleted`
 * was the last row of the whole report, the one BEFORE it. `null` when `deleted` was
 * the only row, or is not in `rows` at all.
 */
export function nextComplianceReportRowKey(
  rows: ComplianceReportRowIdentity[],
  deleted: ComplianceReportRowIdentity,
): ComplianceReportRowKey | null {
  const deletedKey = complianceReportRowKey(deleted);
  const index = rows.findIndex((row) => complianceReportRowKey(row) === deletedKey);
  if (index < 0) {
    return null;
  }
  const neighbour = rows[index + 1] ?? rows[index - 1];
  return neighbour ? complianceReportRowKey(neighbour) : null;
}
