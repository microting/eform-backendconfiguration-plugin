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
  /**
   * The row's EXPLICIT individual assignees — the ARP's non-removed PlanningSites, as
   * a set (not index-aligned with `workerNames`, which since #1232 is wider). The only
   * set the complete-event modal pre-selects a lone completer from.
   */
  workerSiteIds: number[];
  /**
   * The sites assigned via a worker tag ("team") — the live members of the ARP's worker
   * tags (#1236). Optional so a response from an older backend reads as "no team half".
   * The complete-event modal groups on `workerSiteIds ∪ teamAssigneeIds` and
   * pre-selects only from `workerSiteIds`.
   */
  teamAssigneeIds?: number[];
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

// ---------------------------------------------------------------------------
// Rapport — one table per REPORT HEADLINE (#1166 endpoint, #1167 view,
// regrouped by #1188)
// ---------------------------------------------------------------------------

/**
 * One answer column of a headline group. Mirrors the C#
 * `ComplianceReportColumnModel`.
 *
 * `key` — NOT `label`, NOT an array position — is how a cell is addressed. It
 * is `f{fieldId}`, derived from the SDK `Field.Id`, and it does not move when a
 * translation, a label or the display order changes.
 */
export interface ComplianceReportColumnModel {
  key: string;
  fieldId: number;
  /** Translated field label, prefixed with the child checklist's name where they differ. */
  label: string;
  /** The SDK `Constants.FieldTypes` value. */
  fieldType: string;
}

/** A reference to one image answer. References only — no bytes (#1166 §6). */
export interface ComplianceReportImageModel {
  fieldValueId: number;
  uploadedDataId: number;
  /**
   * `{UploadedData.Id}_700_{Checksum}{Extension}`, DERIVED — `null` when the
   * `UploadedData.FileName` existence check failed, i.e. the file cannot be
   * fetched at all.
   */
  fileName: string | null;
  /**
   * `{UploadedData.Id}_300_{Checksum}{Extension}` — the 300px derivative of
   * `fileName`, written to S3 by the same resize pass. DERIVED SERVER-SIDE and
   * `null` under exactly the same condition as `fileName`; the client must
   * never compose it. Use it for thumbnails, falling back to `fileName`.
   */
  thumbnailFileName: string | null;
  geoLink: string | null;
}

/** One answered occurrence inside a headline group. Appears in EXACTLY ONE group (#1188). */
export interface ComplianceReportCaseModel {
  complianceId: number;
  /** The backing SDK case. Always > 0. */
  sdkCaseId: number;
  /**
   * SDK `Case.CheckListId` — the template THIS row was answered against. A
   * headline group spans templates (its `columns` are a union), so the row is
   * the only place `Rediger` can read the template for the case route from.
   * `null` never reaches the view in practice — rows without an answered
   * template are dropped server-side — but the wire type is nullable.
   */
  checkListId: number | null;
  /**
   * The row's OWN tag names, sorted, EXCLUDING the headline tag (a legacy
   * area-rule path pairs the headline into `AreaRulePlanningTags` too, and
   * `Flydelag - Flydelag` is what excluding it prevents). Rendered nowhere on
   * screen — the section's `tagsCaption` is the on-screen line — but carried
   * so the export's `Delrapport` cell and the screen come off one DTO.
   */
  tags: string[];
  propertyId: number;
  propertyName: string;
  /** The task title — the prototype's `Område` column. */
  title: string;
  /** yyyy-MM-dd, occurrence exception NewDate already applied. */
  taskDate: string;
  completed: boolean;
  /**
   * `DoneAtUserModifiable ?? DoneAt` — the prototype's `Udført dato`. CASE
   * METADATA, never an eForm answer (#1160 finding 7).
   *
   * Typed `string` because that is what the wire carries; the app's HTTP layer
   * auto-parses ISO strings into `Date` instances, so a consumer sees whichever
   * of the two the interceptor produced. Both are accepted by Angular's
   * `date` pipe, which is the only reader.
   */
  doneAt: string | null;
  workerNames: string[];
  /**
   * Answers keyed by `ComplianceReportColumnModel.key`. A MISSING key means
   * unanswered — there is no empty-string placeholder and no positional slot,
   * which is what makes the #1160-finding-3 column desync inexpressible.
   * NEVER index into this by column position.
   *
   * `Dictionary<string,string>` on the wire, but the VALUES are not all strings
   * by the time a component sees them: the host frontend's global
   * `DateInterceptor` (app.declarations.ts:120) walks the whole response body
   * recursively and turns every ISO-datetime string into a `Date` — it cannot
   * tell an opaque bag from a DTO, so it descends in here too, and an answer
   * stored as a full timestamp arrives as a `Date` (same reality as e.g.
   * `AdhocTaskHistoryRowModel.completedAt`). Read a cell ONLY through
   * `complianceAnswerText`, which narrows both shapes to display text.
   */
  cells: {[key: string]: string};
  /**
   * Images attached to the case — INCLUDING ones whose file name could not be
   * derived and which therefore cannot be fetched. It is not a count of
   * renderable images.
   */
  imagesCount: number;
  images: ComplianceReportImageModel[];
}

/**
 * One section of the Rapport view — one table per REPORT HEADLINE (#1188,
 * "Tabel_Rapport": *der skal være en tabel for hver Rapportoverskrift og ikke
 * for hvert tag*). Mirrors the C# `ComplianceReportHeadlineGroupModel`.
 *
 * The headline is `AreaRulePlanning.ItemPlanningTagId` — the calendar modal's
 * `Rapportoverskrift` select — and it is an ordinary `PlanningTag`
 * distinguished only by which column references it. The task's OTHER tags do
 * not group anything any more; they become the small caption above the
 * heading.
 *
 * Discriminate on the ID, never on the name:
 *
 *  - `headlineTagId == null` is the fallback group — tasks with NO headline —
 *    and the ONLY one that gets the "Uden rapportoverskrift" label. The
 *    server always orders it LAST;
 *  - `headlineTagId != null` with `headlineName == null` is a NAMED group whose
 *    name could not be resolved (tag ids live in the BC database and tag names
 *    in the items-planning one, with no foreign key between them). It is
 *    labelled `#{headlineTagId}` and is never merged into the fallback group.
 *
 * Groups arrive ordered by `tagsCaption`, then `headlineName`, then
 * `headlineTagId`, fallback last (#1188 decision 5). Nothing client-side
 * re-orders.
 */
export interface ComplianceReportHeadlineGroupModel {
  headlineTagId: number | null;
  headlineName: string | null;
  /**
   * The distinct tag names across the group's cases, alphabetical, joined
   * `" - "` (hyphen-minus with spaces — the PDF's separator). May be `""` for a
   * group whose tasks carry no tags besides the headline.
   */
  tagsCaption: string;
  /** Every template (SDK `Case.CheckListId`) answered inside the group, distinct. */
  checkListIds: number[];
  /**
   * The subset of `checkListIds` whose schema could NOT be derived, so their
   * fields are missing from `columns` for a reason that is neither "no
   * answerable fields" nor "nobody answered". The view renders a per-template
   * notice when only some templates are listed here and a whole-section notice
   * when all of them are.
   */
  schemaUnavailableCheckListIds: number[];
  /**
   * The ordered UNION of the per-template schemas of every template in
   * `checkListIds` — templates by name then id, fields in template order — keyed
   * `f{fieldId}`, which is collision-free across templates by construction.
   * Already de-duplicated server-side. A case answered on template A renders
   * the en dash under template B's columns, in place.
   */
  columns: ComplianceReportColumnModel[];
  /** The group's cases, each exactly once. */
  cases: ComplianceReportCaseModel[];
}

// ---------------------------------------------------------------------------
// Export — `POST api/backend-configuration-pn/compliance-report/export`
// (#1169 endpoint, #1189 wiring)
// ---------------------------------------------------------------------------

/**
 * Body of the export call. Mirrors the C# `ComplianceReportExportRequestModel`.
 *
 * Deliberately EXTENDS the paged request model rather than re-listing its
 * filter fields: the page posts `state.requestModel` as-is plus the three
 * export fields below, so the export always carries exactly the filter set
 * the visible rows were fetched with. That spread also carries
 * `pageIndex`/`pageSize`/`sort`/`isSortDsc`, which the C# model does not
 * declare — the model binder ignores unknown members, so they are harmless
 * on the wire, and an export is unpaged by definition.
 */
export interface ComplianceReportExportRequestModel extends ComplianceReportRequestModel {
  /** Which of the three views to render — the page's current mode. */
  viewMode: 'overview' | 'details' | 'report';
  /** `pdf` | `csv`. Excel was removed by product request (#1189). */
  format: 'pdf' | 'csv';
  /**
   * Whether the PDF appends the image answers. The Rapport export issue
   * decides what the UI sends; until then the page sends `false`.
   */
  includeImageAppendix: boolean;
}
