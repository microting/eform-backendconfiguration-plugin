/**
 * Wire contract for the standalone Compliance page (#1160 / #1163),
 * mirroring `BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport`
 * as landed by #1161.
 *
 * Deliberately NOT an extension of `CalendarComplianceReportRequestModel`:
 * that model belongs to the calendar view mode #1170 deletes, and this page
 * posts to a different controller
 * (`api/backend-configuration-pn/compliance-report/index`).
 */

/** Ignored by the Oversigt aggregation (#1162), disabled in that mode (#1163 §10.2). */
export type ComplianceReportStatus = 'open' | 'done' | 'all';

/**
 * The sort keys the server accepts. Anything else (including null) falls back
 * to `taskDate` descending server-side without an error, so this union is a
 * compile-time aid, not a validation boundary.
 */
export type ComplianceReportSortKey =
  | 'taskDate'
  | 'title'
  | 'propertyName'
  | 'boardName'
  | 'completed'
  | 'doneAt';

export interface ComplianceReportRequestModel {
  /** null = "Alle ejendomme". */
  propertyId: number | null;
  /** [] = "Alle kalendere". */
  boardIds: number[];
  /** [] = "Alle tags". Multi-select with OR semantics (#1163 §7). */
  tagIds: number[];
  /** [] = "Alle medarbejdere". */
  siteIds: number[];
  status: ComplianceReportStatus;
  /**
   * yyyy-MM-dd. OPTIONAL: an incomplete `Sæt periode` range means "no period
   * filter" (#1163 §6), and the state service OMITS both keys rather than
   * substituting today — a fabricated one-day window is indistinguishable from
   * a real result. The server's `DateTime` is non-nullable, so an absent key
   * lands on `default(DateTime)` and the query returns nothing; an explicit
   * `null` would be a 400 instead. Both keys are always present together.
   */
  dateFrom?: string;
  /** yyyy-MM-dd. Optional for the same reason as `dateFrom`. */
  dateTo?: string;
  /** 0-based. Ignored when pageSize <= 0. */
  pageIndex: number;
  /**
   * Rows per page. <= 0 means unpaged — the server caps that at 5000 rows
   * (BackendConfigurationComplianceReportService.MaxRowsReturned) and logs a
   * warning rather than failing. That cap is what bounds "Vis alle".
   */
  pageSize: number;
  sort: ComplianceReportSortKey | null;
  isSortDsc: boolean;
}

export interface ComplianceReportRowModel {
  complianceId: number;
  /** yyyy-MM-dd, occurrence exception NewDate already applied. */
  taskDate: string;
  startHour: number;
  duration: number;
  isAllDay: boolean;
  title: string;
  propertyId: number;
  propertyName: string;
  boardId: number | null;
  boardName: string;
  tags: string[];
  workerNames: string[];
  completed: boolean;
  doneAt: string | null;
  sdkCaseId: number;
  eformId: number | null;
  planningId: number;
  areaRulePlanningId: number | null;
  /**
   * SDK Case.CheckListId — the template that was actually answered, and the
   * template key #1166/#1167 project answers against. `eformId` is NOT
   * (#1160 finding 1).
   */
  checkListId: number | null;
}

export interface ComplianceReportPagedModel {
  /** Rows matching the filters BEFORE paging. */
  total: number;
  entities: ComplianceReportRowModel[];
}

// ---------------------------------------------------------------------------
// Oversigt — the per-property aggregation (#1162 endpoint, #1164 view)
// ---------------------------------------------------------------------------

/**
 * Body of `POST api/backend-configuration-pn/compliance-report/overview`.
 *
 * This is `ComplianceReportRequestModel`'s filter set MINUS four properties,
 * and every omission mirrors the C# `ComplianceReportOverviewRequestModel`
 * exactly:
 *
 *  - **no `status`** — Oversigt counts done and not-done together, which is why
 *    the shell disables the status control. The property is absent rather than
 *    sent-and-ignored, so a caller cannot come to believe the filter works;
 *  - **no `pageIndex`/`pageSize`** — one row per property, unpaged by decision;
 *  - **no `sort`/`isSortDsc`** — #1164 sorts client-side over a handful of rows.
 *
 * `dateFrom`/`dateTo` are optional for the same reason as on the paged model:
 * an incomplete `Sæt periode` range means "no period filter", the keys are
 * omitted rather than filled with today, and the server's non-nullable
 * `DateTime` lands on `default(DateTime)` so the result is visibly empty.
 */
export interface ComplianceReportOverviewRequestModel {
  propertyId: number | null;
  boardIds: number[];
  tagIds: number[];
  siteIds: number[];
  dateFrom?: string;
  dateTo?: string;
}

/**
 * One property's compliance summary — and, reusing the same shape, the
 * weighted totals row.
 *
 * The server returns NUMBERS and `null` only. Formatting (`–`), banding
 * (`is-low`/`is-mid`/`is-high`) and the thresholds live in
 * `compliance-overview.helper.ts`: they are presentation, not wire contract.
 * Nothing on this model is recomputed client-side.
 */
export interface ComplianceReportOverviewRowModel {
  /** 0 on the totals row. */
  propertyId: number;
  /** `null` on the totals row — the view supplies the "I alt" label. */
  propertyName: string | null;
  /** Every matching row, due or not. Computed server-side, deliberately unrendered. */
  total: number;
  /** Completed rows, due or not. Deliberately unrendered. */
  done: number;
  /**
   * Not completed AND dated STRICTLY BEFORE today. A task due *today* and not
   * done raises `dueTotal` (so lowers the percentage) but is NOT overdue.
   */
  overdue: number;
  /** Rows that have fallen due: `!(startOfDay(taskDate) > today)`. */
  dueTotal: number;
  /** Due rows that are also completed — the numerator of `compliancePct`. */
  dueDone: number;
  /**
   * `round(dueDone / dueTotal * 100)`, away from zero. **`null`** — never `0`,
   * never NaN — when `dueTotal` is 0: a property whose work is simply not due
   * yet has no percentage. Rendered as the en dash `–`.
   */
  compliancePct: number | null;
}

export interface ComplianceReportOverviewModel {
  /**
   * One row per property that has at least one matching compliance row, ordered
   * by `propertyName` ascending. That order is a stable server default, not a
   * contract — #1164 re-sorts client-side (default `compliancePct` ascending,
   * worst first).
   */
  rows: ComplianceReportOverviewRowModel[];
  /**
   * WEIGHTED totals — `totals.dueDone / totals.dueTotal`, never the average of
   * `rows[].compliancePct`. Always present, including for an empty result
   * (all-zero counters, `compliancePct: null`). NOT one of `rows`.
   */
  totals: ComplianceReportOverviewRowModel;
}
