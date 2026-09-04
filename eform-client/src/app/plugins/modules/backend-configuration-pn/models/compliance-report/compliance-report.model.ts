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
