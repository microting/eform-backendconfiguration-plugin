import {
  ComplianceReportCaseModel,
  ComplianceReportColumnModel,
  ComplianceReportTagGroupModel,
  ComplianceReportTemplateGroupModel,
} from '../../../models';
import {COMPLIANCE_EMPTY_CELL} from './compliance-week-grouping';

/**
 * Pure section maths for the Rapport view (#1167).
 *
 * No Angular dependency: the two translated labels the flattener needs are
 * passed in, so every rule below is unit-testable without a TestBed (see the
 * sibling `.spec.ts`, which is where the "a missing cell key renders the dash
 * IN PLACE" property is pinned).
 */

/**
 * Rows rendered per sub-report before the "Vis alle" expander appears.
 *
 * Rapport does not paginate — "hver delrapport vises hel" (compliance.js:1820)
 * — and the endpoint is unpaged with a 5000-row server cap
 * (`BackendConfigurationComplianceReportService.MaxRowsReturned`), so without a
 * ceiling one filter set can put 5000 rows × (6 fixed + n answer + 1 actions)
 * cells into a single DOM. Each sub-report therefore renders its first `N` rows
 * and states its true row count next to a control that reveals the rest;
 * nothing is hidden, and the common sub-report (dozens of rows) still reads as
 * one whole document because the expander never appears for it.
 */
export const COMPLIANCE_REPORT_SECTION_ROW_CAP = 100;

/**
 * Rows revealed across the WHOLE page before further sub-reports render
 * collapsed.
 *
 * The per-section cap alone does not bound the page. A section is one
 * (tag group × TEMPLATE) pair, not one tag — a tag whose tasks were answered on
 * four templates is four sections — so a realistic filter set yields dozens of
 * sections, most of them far below `COMPLIANCE_REPORT_SECTION_ROW_CAP`. In that
 * shape no section ever caps and the server's whole 5000-row allowance lands in
 * one DOM: the exact outcome the per-section cap was written to prevent. (The
 * prototype's 315 rows / 6 sections is one point, not the worst case; it
 * predates the per-template split, which can only ever raise the section count.)
 *
 * So the page keeps a cumulative budget as well. Sections render in server
 * order until it is exhausted; the ones after that render with NO rows but with
 * their heading, their TRUE row count and the same "Vis alle" control, so any
 * one of them can still be opened explicitly. That bounds the initial DOM at
 * this many rows regardless of how the result is split, and it costs the user
 * one click on the sub-report they actually came for.
 */
export const COMPLIANCE_REPORT_PAGE_ROW_BUDGET = 500;

/** One tag-group × template-group sub-report. */
export interface ComplianceReportSection {
  /**
   * Stable trackBy identity, and the suffix of the section's DOM ids.
   * `t{tagId|none}-c{checkListId}` — the `t`/`c` prefixes are what keep
   * (tag 75, template 11) from colliding with (tag 7, template 511).
   */
  key: string;
  /** The tag line above the heading. */
  tagLabel: string;
  /** The heading — the TEMPLATE name. The prototype's `Rapportoverskrift` placeholder is gone. */
  templateLabel: string;
  checkListId: number;
  /**
   * The schema could not be derived, so `columns` is empty for a reason that is
   * not "this template has no answerable fields" and not "nobody answered".
   * The view says so instead of rendering a bare table.
   */
  schemaUnavailable: boolean;
  columns: ComplianceReportColumnModel[];
  cases: ComplianceReportCaseModel[];
}

/**
 * The label for a tag group, discriminating on the tag ID and NEVER on the
 * name — the same rule #1169's export applies
 * (`ComplianceExportDocumentBuilder.TagGroupLabel`).
 *
 * `tagId != null` with `tagName == null` is a NAMED group whose name could not
 * be resolved: tag ids live in the BC database and tag names in the
 * items-planning one, with no foreign key between them, and #1166 deliberately
 * keeps the row's real tag rather than dropping it. Filing such a group under
 * "Uden tag" would make it indistinguishable from the genuinely untagged group
 * — two different sections merged under one label, and the screen disagreeing
 * with the file downloaded from it. It gets `#{tagId}` instead: visibly not a
 * tag name, distinct from every other group, and it names the id the tag can be
 * looked up by.
 *
 * The DISCRIMINATION is identical to the export's: the untagged label ONLY for
 * `tagId == null`, `#{tagId}` for a named group whose name is missing or blank
 * (`ComplianceExportDocumentBuilder.TagGroupLabel:398-404` branches on
 * `IsNullOrWhiteSpace`, so a whitespace-only name lands on `#{tagId}` there
 * too). The rendered STRING is not identical: this returns the name TRIMMED,
 * the C# returns `TagGroup.TagName` as stored — so a name saved with
 * surrounding whitespace reads tight on screen and padded in the file.
 * Cosmetic, and the screen has the better of the two.
 */
export function complianceTagGroupLabel(
  group: ComplianceReportTagGroupModel,
  untaggedLabel: string
): string {
  if (group.tagId == null) {
    return untaggedLabel;
  }
  const name = (group.tagName ?? '').trim();
  return name.length > 0 ? name : `#${group.tagId}`;
}

/**
 * The sub-report heading. `#{checkListId}` for a template with no name — the
 * same neutral form, and the same discrimination, as the export
 * (`ComplianceExportDocumentBuilder:266-268`, `IsNullOrWhiteSpace` over
 * `CheckListName`). As with the tag label, this one additionally TRIMS the name
 * it returns and the C# does not.
 */
export function complianceTemplateLabel(group: ComplianceReportTemplateGroupModel): string {
  const name = (group.checkListName ?? '').trim();
  return name.length > 0 ? name : `#${group.checkListId}`;
}

/**
 * Flatten the response into the rendering order: tag group, then template
 * sub-group, in the order the server sent them (it sorts the untagged group
 * last on purpose; nothing here re-orders).
 *
 * Template groups with no cases are dropped — an empty table under a heading
 * says nothing. A `schemaUnavailable` group with cases is KEPT: its cases are
 * real, only its answer columns are missing, and the view has to say so.
 */
export function buildComplianceReportSections(
  groups: ComplianceReportTagGroupModel[] | null | undefined,
  untaggedLabel: string
): ComplianceReportSection[] {
  const sections: ComplianceReportSection[] = [];
  for (const tagGroup of groups ?? []) {
    const tagLabel = complianceTagGroupLabel(tagGroup, untaggedLabel);
    for (const templateGroup of tagGroup.templates ?? []) {
      const cases = templateGroup.cases ?? [];
      if (cases.length === 0) {
        continue;
      }
      sections.push({
        key: `t${tagGroup.tagId ?? 'none'}-c${templateGroup.checkListId}`,
        tagLabel,
        templateLabel: complianceTemplateLabel(templateGroup),
        checkListId: templateGroup.checkListId,
        schemaUnavailable: !!templateGroup.schemaUnavailable,
        columns: templateGroup.columns ?? [],
        cases,
      });
    }
  }
  return sections;
}

/**
 * THE rule of this view: a cell is addressed by its column's `key`, never by
 * its position and never by zipping a header list against a value list.
 *
 * An absent key means UNANSWERED and renders the en dash IN PLACE, so the
 * columns after it do not shift — which is what makes the #1160-finding-3
 * desync bug class inexpressible here rather than merely fixed.
 *
 * An empty string is treated as unanswered too: the projector omits excluded
 * field types entirely, so an empty value can only come from an answer that
 * carries no text, and a blank cell and a dash cell must not both mean "no
 * answer" in the same column.
 *
 * The bag is `Dictionary<string,string>` on the wire but the VALUE is not
 * necessarily a string by the time it gets here: the app-wide `DateInterceptor`
 * (`src/app/common/interceptors/date.interceptor.ts`, registered in
 * `app.declarations.ts:120`) walks every response body recursively and replaces
 * any string CONTAINING an ISO datetime — the match is unanchored — with
 * `parseJSON(value)`, in place. It has no notion of which sub-objects are
 * opaque bags, so it descends into `cells` too.
 * A Date or Text answer stored as a full ISO timestamp therefore arrives as a
 * `Date`, and implicit stringification would print
 * `Fri Jan 02 2026 01:00:00 GMT+0100 (…)` into a table cell. Such a value is
 * rendered through `formatComplianceReportDate` instead, the same `dd.MM.yyyy`
 * the rest of this view uses — and read in UTC, because this Date was
 * manufactured from a UTC ISO string off the wire rather than constructed
 * locally. See the convention note on `formatComplianceReportDate`.
 */
export function complianceAnswerText(
  // Structural, not `ComplianceReportCaseModel`: the view renders a flattened
  // row object that carries the same keyed bag, and widening the parameter is
  // cheaper — and more honest — than casting a row back to a case.
  caseModel: {cells?: {[key: string]: string} | null} | null | undefined,
  key: string | null | undefined
): string {
  if (!caseModel || !key) {
    return COMPLIANCE_EMPTY_CELL;
  }
  const cells = caseModel.cells;
  if (!cells || !Object.prototype.hasOwnProperty.call(cells, key)) {
    return COMPLIANCE_EMPTY_CELL;
  }
  // `unknown`, deliberately: the declared type says `string`, the interceptor
  // says otherwise, and the narrowing below is what makes both true.
  const value: unknown = cells[key];
  if (value == null || value === '') {
    return COMPLIANCE_EMPTY_CELL;
  }
  if (typeof value === 'string') {
    return value;
  }
  if (value instanceof Date) {
    // An unparseable timestamp yields an Invalid Date, whose getters are all
    // NaN — `NaN.NaN.NaN` is worse than saying nothing, and there is no answer
    // text left to fall back to.
    return Number.isNaN(value.getTime())
      ? COMPLIANCE_EMPTY_CELL
      : formatComplianceReportDate(value, true);
  }
  // Nothing else should ever reach a cell; if something does, show it rather
  // than blanking a column that genuinely holds an answer.
  return String(value);
}

/**
 * `Udført af`. The row DTO carries NAMES, not ids (#1165 hit the same wall);
 * they are joined the way the export joins them.
 */
export function complianceWorkerNames(names: string[] | null | undefined): string {
  const cleaned = (names ?? []).map((n) => (n ?? '').trim()).filter((n) => n.length > 0);
  return cleaned.length > 0 ? cleaned.join(', ') : '';
}

/**
 * `dd.MM.yyyy` — the meta line's date format (`formatReportDate`,
 * compliance.js:1572-1580). Deliberately NOT locale-dependent: the prototype
 * pins this one format, and the meta line is repeated verbatim on every
 * exported page.
 *
 * WHICH CLOCK — the convention, because a `Date` here can come from either of
 * two places and they do NOT agree near midnight:
 *
 * - Constructed locally by this app (`periodBounds`, i.e. `new Date(y, m, d)`
 *   at local midnight). Its calendar fields are only meaningful in LOCAL time;
 *   read in UTC, a GMT+1 local midnight is the previous day. `utc = false`.
 * - Manufactured by the app-wide `DateInterceptor` from a UTC ISO timestamp on
 *   a response body (an answer cell). The instant is UTC and the calendar date
 *   the device recorded is its UTC date, so it must be read in UTC —
 *   `utc = true`. This is also what the `doneAt` column does: it renders
 *   through mtx-grid with `typeParameter.timezone: 'utc'`
 *   (`compliance-report-view.component.ts`), and two columns of one row must
 *   not disagree by a day over the same instant.
 *
 * So: wire-derived Dates are formatted in UTC, locally-constructed calendar
 * Dates in local time. The default is local because the meta line's bounds are
 * the locally-constructed kind; every caller holding an interceptor Date passes
 * `utc = true`.
 */
export function formatComplianceReportDate(
  date: Date | null | undefined,
  utc = false
): string {
  if (!date) {
    return '';
  }
  const dd = (utc ? date.getUTCDate() : date.getDate()).toString().padStart(2, '0');
  const mm = ((utc ? date.getUTCMonth() : date.getMonth()) + 1).toString().padStart(2, '0');
  const yyyy = utc ? date.getUTCFullYear() : date.getFullYear();
  return `${dd}.${mm}.${yyyy}`;
}
