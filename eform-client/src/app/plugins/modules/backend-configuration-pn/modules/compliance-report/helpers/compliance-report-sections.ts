import {
  ComplianceReportCaseModel,
  ComplianceReportColumnModel,
  ComplianceReportHeadlineGroupModel,
  ComplianceReportTemplateTableModel,
} from '../../../models';
import {COMPLIANCE_EMPTY_CELL} from './compliance-week-grouping';

/**
 * Pure section maths for the Rapport view (#1167, regrouped by #1188, split
 * into one table per eForm by #1276).
 *
 * No Angular dependency: the one translated label the mapper needs is passed
 * in, so every rule below is unit-testable without a TestBed (see the sibling
 * `.spec.ts`, which is where the "a missing cell key renders the dash IN
 * PLACE" property is pinned).
 */

/**
 * The two SDK `Constants.FieldTypes` values the answer cells branch on
 * (#1276). Everything else renders its text unchanged.
 */
const COMPLIANCE_FIELD_TYPE_CHECKBOX = 'CheckBox';
const COMPLIANCE_FIELD_TYPE_DATE = 'Date';

/**
 * The canonical checkbox tokens. `ComplianceReportEformProjector.Render` folds
 * the SDK's `checked` / dirty `true` and `unchecked` / `false` into them, so
 * these are the only spellings that reach the client.
 */
const COMPLIANCE_CHECKBOX_CHECKED = 'checked';
const COMPLIANCE_CHECKBOX_UNCHECKED = 'unchecked';

/** A `Date` field's stored value: `yyyy-MM-dd`, not reformatted server-side. */
const COMPLIANCE_ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})$/;

/**
 * Rows rendered per eForm TABLE before the "Vis alle" expander appears.
 *
 * Rapport does not paginate — "hver delrapport vises hel" (compliance.js:1820)
 * — and the endpoint is unpaged with a 5000-row server cap
 * (`BackendConfigurationComplianceReportService.MaxRowsReturned`), so without a
 * ceiling one filter set can put 5000 rows × (6 fixed + n answer + 1 actions)
 * cells into a single DOM. Each table therefore renders its first `N` rows and
 * states its true row count next to a control that reveals the rest; nothing
 * is hidden, and the common table (dozens of rows) still reads as one whole
 * document because the expander never appears for it.
 */
export const COMPLIANCE_REPORT_TABLE_ROW_CAP = 100;

/**
 * Rows revealed across the WHOLE page before further tables render collapsed.
 *
 * The per-table cap alone does not bound the page. A section is one REPORT
 * HEADLINE (#1188) holding one table per eForm answered under it (#1276), and
 * an installation can run many of both — one section per distinct
 * `Rapportoverskrift` in the filtered set (headline-less tasks are excluded,
 * #1301) — most tables far below `COMPLIANCE_REPORT_TABLE_ROW_CAP`. In that shape no
 * table ever caps and the server's whole 5000-row allowance lands in one DOM:
 * the exact outcome the per-table cap was written to prevent. (The
 * prototype's 315 rows / 6 sections is one point, not the worst case.)
 *
 * So the page keeps a cumulative budget as well. Tables render in server
 * order — sections in order, tables in order within each — until it is
 * exhausted; the ones after that render with NO rows but with their headings,
 * their TRUE row count and the same "Vis alle" control, so any one of them
 * can still be opened explicitly. That bounds the initial DOM at this many
 * rows regardless of how the result is split, and it costs the user one click
 * on the table they actually came for.
 */
export const COMPLIANCE_REPORT_PAGE_ROW_BUDGET = 500;

/**
 * One eForm's table inside a section (#1276). A 1:1 image of
 * `ComplianceReportTemplateTableModel` with its label resolved and its key
 * made page-unique.
 */
export interface ComplianceReportTable {
  /**
   * Stable trackBy identity, and the suffix of the table's DOM ids
   * (`complianceReportShowAll-{key}`). `h{headlineTagId|none}-c{checkListId}`:
   * one eForm answered under two headlines is two tables, and the section
   * prefix is what keeps their keys apart.
   */
  key: string;
  checkListId: number;
  /**
   * The sub-heading above the table: the eForm name, or `#{checkListId}` for
   * one sent without a name — the same neutral form an unresolvable headline
   * gets.
   */
  templateLabel: string;
  /**
   * The eForm's schema could not be derived, so `columns` is empty for a
   * reason the view has to state instead of rendering a bare table.
   */
  schemaUnavailable: boolean;
  /** THIS eForm's answer columns only — never another eForm's (#1276). */
  columns: ComplianceReportColumnModel[];
  cases: ComplianceReportCaseModel[];
}

/**
 * One section: one REPORT HEADLINE (#1188), shown ONCE, with one table per
 * eForm answered under it (#1276). A 1:1 image of
 * `ComplianceReportHeadlineGroupModel` with its labels resolved.
 */
export interface ComplianceReportSection {
  /**
   * Stable trackBy identity, and the `data-section-key` of the section.
   * `h{headlineTagId}`. Also the prefix of every one of its tables' keys.
   */
  key: string;
  /**
   * The small line ABOVE the heading: the group's tags joined `" - "`
   * (`Miljøtilsyn - Brand`). May be empty — a headline whose tasks carry no
   * other tags has nothing to say there, and an empty `<p>` costs nothing.
   */
  captionLabel: string;
  /**
   * The bold heading — the REPORT HEADLINE (the calendar modal's
   * `Rapportoverskrift`), `#{id}` for an unresolvable one. NEVER the template name:
   * a section spans templates, and each of its tables carries its own
   * (`ComplianceReportTable.templateLabel`).
   */
  headlineLabel: string;
  /** In server order (by eForm name). Never empty — see the mapper. */
  tables: ComplianceReportTable[];
}

/**
 * The heading of a headline group — the same rule the export applies to the
 * same group. Only groups WITH a headline id reach it: tasks without a report
 * headline are excluded from the report (#1301), by the server and again by
 * `buildComplianceReportSections`.
 *
 * `headlineName == null` (or blank) is a NAMED group whose name could not be
 * resolved: headline ids live in the BC database
 * (`AreaRulePlanning.ItemPlanningTagId`) and tag names in the items-planning
 * one, with no foreign key between them. It gets `#{headlineTagId}`: visibly
 * not a headline, distinct from every other group, and it names the id the
 * tag can be looked up by.
 *
 * The name is returned TRIMMED, which the C# does not do — a name saved with
 * surrounding whitespace reads tight on screen and padded in the file.
 * Cosmetic, and the screen has the better of the two.
 */
export function complianceHeadlineLabel(group: {
  headlineTagId: number;
  headlineName: string | null;
}): string {
  const name = (group.headlineName ?? '').trim();
  return name.length > 0 ? name : `#${group.headlineTagId}`;
}

/**
 * The sub-heading of an eForm table (#1276): the eForm name, trimmed, or
 * `#{checkListId}` when the server sent none — visibly not a name, and it
 * names the id the template can be looked up by. The same neutral form
 * `complianceHeadlineLabel` uses for an unresolvable headline.
 */
export function complianceTemplateLabel(
  template: Pick<ComplianceReportTemplateTableModel, 'checkListId' | 'checkListName'>
): string {
  const name = (template.checkListName ?? '').trim();
  return name.length > 0 ? name : `#${template.checkListId}`;
}

/**
 * Map the response 1:1 onto sections and their tables, in the order the server
 * sent them (groups by caption, then headline, then id — #1188 decision 5;
 * tables by eForm name — #1276). Nothing here re-orders.
 *
 * A group without a headline id is dropped (#1301): tasks without a report
 * headline are not part of the report. The server already excludes them; the
 * guard keeps an older/other server from surfacing an unlabelled section.
 *
 * Tables with no cases are dropped — an empty table under a sub-heading says
 * nothing — and a group left with no tables is dropped with them. A table
 * whose eForm lacks a schema is KEPT: its cases are real, only its answer
 * columns are missing, and the view has to say so.
 */
export function buildComplianceReportSections(
  groups: ComplianceReportHeadlineGroupModel[] | null | undefined
): ComplianceReportSection[] {
  const sections: ComplianceReportSection[] = [];
  for (const group of groups ?? []) {
    if (group?.headlineTagId == null) {
      continue;
    }
    const headlineTagId = group.headlineTagId;
    const sectionKey = `h${headlineTagId}`;
    const tables: ComplianceReportTable[] = [];
    for (const template of group.templates ?? []) {
      const cases = template?.cases ?? [];
      if (cases.length === 0) {
        continue;
      }
      tables.push({
        key: `${sectionKey}-c${template.checkListId}`,
        checkListId: template.checkListId,
        templateLabel: complianceTemplateLabel(template),
        schemaUnavailable: !!template.schemaUnavailable,
        columns: template.columns ?? [],
        cases,
      });
    }
    if (tables.length === 0) {
      continue;
    }
    sections.push({
      key: sectionKey,
      captionLabel: (group.tagsCaption ?? '').trim(),
      headlineLabel: complianceHeadlineLabel({headlineTagId, headlineName: group.headlineName}),
      tables,
    });
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
 *
 * `fieldType` (the column's SDK field type, #1276) changes two kinds of cell:
 *
 *  - `CheckBox` never shows its token ("Checked skal udskiftes med et flueben.
 *    Og unchecked skal ikke vises"): `checked` and `unchecked` have no text —
 *    a ticked box is drawn by the template as a check icon, off
 *    `complianceAnswerIsChecked` — while an unanswered box, or a token that is
 *    neither state, is the en dash, as in the Word/PDF export.
 *  - `Date` is stored as `yyyy-MM-dd` and not reformatted server-side, so it
 *    printed raw ISO (`2025-12-01`) next to the `Udført dato` column's
 *    `01.12.2025`. It now goes through the same `formatComplianceReportDate`
 *    in UTC; a value that is not a real calendar date is shown as it is.
 *
 * Every other field type renders as before.
 */
export function complianceAnswerText(
  // Structural, not `ComplianceReportCaseModel`: the view renders a flattened
  // row object that carries the same keyed bag, and widening the parameter is
  // cheaper — and more honest — than casting a row back to a case.
  caseModel: {cells?: {[key: string]: string} | null} | null | undefined,
  key: string | null | undefined,
  fieldType?: string | null
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
  if (fieldType === COMPLIANCE_FIELD_TYPE_CHECKBOX) {
    return value === COMPLIANCE_CHECKBOX_CHECKED || value === COMPLIANCE_CHECKBOX_UNCHECKED
      ? ''
      : COMPLIANCE_EMPTY_CELL;
  }
  if (typeof value === 'string') {
    return fieldType === COMPLIANCE_FIELD_TYPE_DATE ? formatComplianceDateAnswer(value) : value;
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
 * Whether a `CheckBox` column's answer is ticked (#1276) — the one state its
 * cell draws, as a check icon; see `complianceAnswerText`, which gives the same
 * cell no text. False for every other field type, so a Text answer reading
 * `checked` stays text. Keyed exactly like `complianceAnswerText`, so a missing
 * key, an inherited Object property and `unchecked` are all "not ticked".
 */
export function complianceAnswerIsChecked(
  caseModel: {cells?: {[key: string]: string} | null} | null | undefined,
  key: string | null | undefined,
  fieldType?: string | null
): boolean {
  const cells = caseModel?.cells;
  return (
    fieldType === COMPLIANCE_FIELD_TYPE_CHECKBOX &&
    !!cells &&
    !!key &&
    Object.prototype.hasOwnProperty.call(cells, key) &&
    cells[key] === COMPLIANCE_CHECKBOX_CHECKED
  );
}

/**
 * A `Date` answer's `yyyy-MM-dd` as `dd.MM.yyyy` (#1276), through the same
 * `formatComplianceReportDate` — in UTC, because the value is a calendar date
 * off the wire and must not move a day in a runner east or west of UTC.
 *
 * Anything else is returned AS IS: a value that is not `yyyy-MM-dd`, or one
 * that is not a real date (`2026-02-30` — `Date.UTC` would silently roll it
 * over to 2 March, so the round-trip is checked). Showing the stored text
 * beats inventing a date or blanking a real answer.
 */
function formatComplianceDateAnswer(value: string): string {
  const match = COMPLIANCE_ISO_DATE.exec(value.trim());
  if (!match) {
    return value;
  }
  const [year, month, day] = match.slice(1).map(Number);
  const date = new Date(Date.UTC(year, month - 1, day));
  const roundTrips =
    date.getUTCFullYear() === year &&
    date.getUTCMonth() === month - 1 &&
    date.getUTCDate() === day;
  return roundTrips ? formatComplianceReportDate(date, true) : value;
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
