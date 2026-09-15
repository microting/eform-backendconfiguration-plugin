import {
  ComplianceReportCaseModel,
  ComplianceReportHeadlineGroupModel,
  ComplianceReportTemplateTableModel,
} from '../../../models';
import {
  buildComplianceReportSections,
  complianceAnswerIsChecked,
  complianceAnswerText,
  complianceHeadlineLabel,
  complianceTemplateLabel,
  complianceWorkerNames,
  formatComplianceReportDate,
} from './compliance-report-sections';
import {COMPLIANCE_EMPTY_CELL} from './compliance-week-grouping';

/**
 * The rules of the Rapport view that must not be re-derived wrong (#1167,
 * regrouped by headline in #1188, one table per eForm under it since #1276).
 *
 * The headline one is `complianceAnswerText`: a cell is looked up BY COLUMN
 * KEY, an absent key renders the en dash IN PLACE, and therefore no later
 * column can shift. That is the property that makes the #1160-finding-3 desync
 * bug class inexpressible in this view rather than merely absent from it, so it
 * is asserted against a schema whose middle key is missing from the cell bag —
 * the exact shape that shifts every subsequent column under positional
 * addressing.
 *
 * Eight describes: `complianceAnswerText` (the keyed lookup, and the two on
 * the `Date` values the global DateInterceptor leaves in a bag it has no
 * business walking), its field-type formatting (#1276: CheckBox and Date),
 * `complianceAnswerIsChecked`, the headline label rule, the eForm label rule,
 * the mapper (one section per headline, one table per eForm under it, each
 * with only its own columns and a page-unique key, the schema notice), the
 * worker join and the date format. The GRID rules — the `answer_` prefix, the
 * duplicate-key dedupe, the per-table column-array identity, the field type
 * reaching the grid column and the row's OWN `checkListId` — live on the
 * component and are pinned in `compliance-report-view.component.spec.ts`.
 */

function caseModel(overrides: Partial<ComplianceReportCaseModel> = {}): ComplianceReportCaseModel {
  return {
    complianceId: 1,
    sdkCaseId: 100,
    propertyId: 5,
    propertyName: 'Ejendom A',
    title: 'Område 1',
    taskDate: '2026-08-11',
    completed: true,
    doneAt: '2026-08-11T09:00:00Z',
    workerNames: ['Anna'],
    checkListId: 509,
    tags: ['Miljøtilsyn'],
    cells: {},
    imagesCount: 0,
    images: [],
    ...overrides,
  };
}

describe('complianceAnswerText', () => {
  const schema = ['f1', 'f2', 'f3'];

  it('renders the dash IN PLACE for a missing key, leaving later columns aligned', () => {
    // f2 is ABSENT from the bag — the case a positional walk would collapse,
    // pulling f3's answer into f2's column and leaving f3 blank.
    const row = caseModel({cells: {f1: 'Ja', f3: '42'}});

    const rendered = schema.map((key) => complianceAnswerText(row, key));

    expect(rendered).toEqual(['Ja', COMPLIANCE_EMPTY_CELL, '42']);
  });

  it('does not shift when EVERY key is missing', () => {
    const row = caseModel({cells: {}});

    expect(schema.map((key) => complianceAnswerText(row, key))).toEqual([
      COMPLIANCE_EMPTY_CELL,
      COMPLIANCE_EMPTY_CELL,
      COMPLIANCE_EMPTY_CELL,
    ]);
  });

  it('ignores cell keys the schema does not name, rather than filling a column with them', () => {
    // An extra key cannot leak into a column: the walk is over the SCHEMA.
    const row = caseModel({cells: {f1: 'Ja', f9: 'orphan'}});

    expect(schema.map((key) => complianceAnswerText(row, key))).toEqual([
      'Ja',
      COMPLIANCE_EMPTY_CELL,
      COMPLIANCE_EMPTY_CELL,
    ]);
  });

  it('treats an empty answer as unanswered', () => {
    expect(complianceAnswerText(caseModel({cells: {f1: ''}}), 'f1')).toBe(COMPLIANCE_EMPTY_CELL);
  });

  it('renders a falsy but real answer', () => {
    expect(complianceAnswerText(caseModel({cells: {f1: '0'}}), 'f1')).toBe('0');
  });

  it('is safe for a null cell bag and a null key', () => {
    expect(complianceAnswerText(caseModel({cells: null as any}), 'f1')).toBe(COMPLIANCE_EMPTY_CELL);
    expect(complianceAnswerText(caseModel(), null)).toBe(COMPLIANCE_EMPTY_CELL);
    expect(complianceAnswerText(null, 'f1')).toBe(COMPLIANCE_EMPTY_CELL);
  });

  it('renders a Date the DateInterceptor left in the bag as dd.MM.yyyy, in UTC', () => {
    // The bag is `Dictionary<string,string>` on the wire, but the app-wide
    // DateInterceptor walks EVERY response body and replaces any string that
    // contains an ISO datetime with a `Date`, in place — `cells` included. An
    // answer stored as a full timestamp therefore arrives here as a Date, and
    // implicit stringification would put
    // `Fri Jan 02 2026 01:00:00 GMT+0100 (…)` in a table cell.
    //
    // The instant is built from a UTC ISO string, so its calendar date is its
    // UTC date — read in local time, this same instant is the 3rd anywhere east
    // of UTC, while the `doneAt` column (mtx-grid, `timezone: 'utc'`) would
    // still say the 2nd. The assertion below is therefore deliberately a
    // near-midnight instant, and holds in every runner timezone.
    const row = caseModel({cells: {f1: new Date('2026-01-02T23:30:00Z') as any}});

    expect(complianceAnswerText(row, 'f1')).toBe('02.01.2026');
  });

  it('renders an Invalid Date as unanswered rather than NaN.NaN.NaN', () => {
    // Invalid Date's UTC getters are NaN just like its local ones, so the guard
    // is unaffected by the UTC convention above.
    const row = caseModel({cells: {f1: new Date('not a date') as any}});

    expect(complianceAnswerText(row, 'f1')).toBe(COMPLIANCE_EMPTY_CELL);
  });

  it('does not resolve inherited Object properties as answers', () => {
    // `cells['constructor']` is truthy on any plain object; a bare truthiness
    // check would render a function body into a column.
    expect(complianceAnswerText(caseModel(), 'constructor')).toBe(COMPLIANCE_EMPTY_CELL);
  });
});

describe('complianceAnswerText — field types (#1276)', () => {
  it('never gives a CheckBox cell the literal checked/unchecked; unanswered keeps the en dash', () => {
    // "Checked skal udskiftes med et flueben. Og unchecked skal ikke vises":
    // the tick is drawn by the template off `complianceAnswerIsChecked`, and
    // `unchecked` is an empty cell rather than the canonical token.
    const row = caseModel({cells: {f1: 'checked', f2: 'unchecked'}});

    expect(complianceAnswerText(row, 'f1', 'CheckBox')).toBe('');
    expect(complianceAnswerText(row, 'f2', 'CheckBox')).toBe('');
    // A box with no stored state is unanswered, like any other unanswered cell.
    expect(complianceAnswerText(row, 'f3', 'CheckBox')).toBe(COMPLIANCE_EMPTY_CELL);
    expect(complianceAnswerText(caseModel({cells: {f1: null as any}}), 'f1', 'CheckBox')).toBe(COMPLIANCE_EMPTY_CELL);
    expect(complianceAnswerText(caseModel({cells: {f1: ''}}), 'f1', 'CheckBox')).toBe(COMPLIANCE_EMPTY_CELL);
  });

  it('renders a CheckBox token that is neither state as the en dash, like the export', () => {
    // The projector never emits one; if one arrives it is not an answer the
    // cell can draw, and the backend's AnswerCell makes it the empty cell too.
    expect(complianceAnswerText(caseModel({cells: {f1: 'maybe'}}), 'f1', 'CheckBox')).toBe(
      COMPLIANCE_EMPTY_CELL
    );
    expect(complianceAnswerText(caseModel(), 'constructor', 'CheckBox')).toBe(COMPLIANCE_EMPTY_CELL);
  });

  it('leaves the token alone in a column that is NOT a CheckBox', () => {
    // A Text answer that happens to read `checked` is text.
    const row = caseModel({cells: {f1: 'checked'}});

    expect(complianceAnswerText(row, 'f1', 'Text')).toBe('checked');
    expect(complianceAnswerText(row, 'f1')).toBe('checked');
  });

  it('formats a Date answer like the Udført dato column: dd.MM.yyyy', () => {
    // Stored `yyyy-MM-dd` and not reformatted server-side; it rendered raw
    // next to the fixed column's `01.12.2025`.
    expect(complianceAnswerText(caseModel({cells: {f1: '2025-12-01'}}), 'f1', 'Date')).toBe(
      '01.12.2025'
    );
    // Read in UTC: the calendar date must not move a day in any runner timezone.
    expect(complianceAnswerText(caseModel({cells: {f1: '2026-01-31'}}), 'f1', 'Date')).toBe(
      '31.01.2026'
    );
  });

  it('shows an unparseable Date answer as it is, never NaN and never a rolled-over day', () => {
    const text = (value: string) => complianceAnswerText(caseModel({cells: {f1: value}}), 'f1', 'Date');

    expect(text('i morgen')).toBe('i morgen');
    expect(text('01.12.2025')).toBe('01.12.2025');
    // Not a real calendar date — `Date.UTC` would silently make it 2 March.
    expect(text('2026-02-30')).toBe('2026-02-30');
    expect(text('2026-13-01')).toBe('2026-13-01');
  });

  it('still renders the dash for an unanswered Date, and a Date instance through the same format', () => {
    expect(complianceAnswerText(caseModel({cells: {}}), 'f1', 'Date')).toBe(COMPLIANCE_EMPTY_CELL);
    expect(
      complianceAnswerText(caseModel({cells: {f1: new Date('2026-01-02T23:30:00Z') as any}}), 'f1', 'Date')
    ).toBe('02.01.2026');
  });

  it('does not reformat a date-shaped answer in a column that is NOT a Date', () => {
    expect(complianceAnswerText(caseModel({cells: {f1: '2025-12-01'}}), 'f1', 'Text')).toBe(
      '2025-12-01'
    );
  });
});

describe('complianceAnswerIsChecked', () => {
  it('is true for the canonical checked token only', () => {
    const row = caseModel({cells: {f1: 'checked', f2: 'unchecked', f3: ''}});

    expect(complianceAnswerIsChecked(row, 'f1', 'CheckBox')).toBe(true);
    expect(complianceAnswerIsChecked(row, 'f2', 'CheckBox')).toBe(false);
    expect(complianceAnswerIsChecked(row, 'f3', 'CheckBox')).toBe(false);
    expect(complianceAnswerIsChecked(row, 'f4', 'CheckBox')).toBe(false);
  });

  it('is false in a column that is NOT a CheckBox, even for the checked token', () => {
    const row = caseModel({cells: {f1: 'checked'}});

    expect(complianceAnswerIsChecked(row, 'f1', 'Text')).toBe(false);
    expect(complianceAnswerIsChecked(row, 'f1')).toBe(false);
  });

  it('is keyed like complianceAnswerText: safe for nulls and inherited properties', () => {
    expect(complianceAnswerIsChecked(null, 'f1', 'CheckBox')).toBe(false);
    expect(complianceAnswerIsChecked(caseModel({cells: null as any}), 'f1', 'CheckBox')).toBe(false);
    expect(complianceAnswerIsChecked(caseModel({cells: {f1: 'checked'}}), null, 'CheckBox')).toBe(false);
    expect(complianceAnswerIsChecked(caseModel(), 'constructor', 'CheckBox')).toBe(false);
  });
});

describe('complianceHeadlineLabel', () => {
  it('labels the genuinely headline-less group with the fallback label', () => {
    expect(
      complianceHeadlineLabel({headlineTagId: null, headlineName: null}, 'Uden rapportoverskrift')
    ).toBe('Uden rapportoverskrift');
  });

  it('labels a NAMED group whose name could not be resolved as #{id}, never as the fallback', () => {
    expect(
      complianceHeadlineLabel({headlineTagId: 42, headlineName: null}, 'Uden rapportoverskrift')
    ).toBe('#42');
    expect(
      complianceHeadlineLabel({headlineTagId: 42, headlineName: '   '}, 'Uden rapportoverskrift')
    ).toBe('#42');
  });

  it('uses the headline name when there is one, trimmed', () => {
    expect(
      complianceHeadlineLabel(
        {headlineTagId: 7, headlineName: ' Brandsikkerhed og beredskab '},
        'Uden rapportoverskrift'
      )
    ).toBe('Brandsikkerhed og beredskab');
  });

  it('ignores the name of the headline-less group — the ID is the discriminator', () => {
    // A server that ever sent a name on the null group must not turn it into
    // a named section; the fallback group is the null ID, full stop.
    expect(
      complianceHeadlineLabel({headlineTagId: null, headlineName: 'Stray'}, 'Uden rapportoverskrift')
    ).toBe('Uden rapportoverskrift');
  });
});

describe('complianceTemplateLabel', () => {
  it('uses the eForm name, trimmed', () => {
    expect(complianceTemplateLabel({checkListId: 509, checkListName: ' Tilsyn '})).toBe('Tilsyn');
  });

  it('labels an eForm sent without a name #{id}', () => {
    expect(complianceTemplateLabel({checkListId: 509, checkListName: null as any})).toBe('#509');
    expect(complianceTemplateLabel({checkListId: 509, checkListName: '  '})).toBe('#509');
  });
});

describe('buildComplianceReportSections', () => {
  const FALLBACK = 'Uden rapportoverskrift';

  const template = (
    overrides: Partial<ComplianceReportTemplateTableModel> = {}
  ): ComplianceReportTemplateTableModel => ({
    checkListId: 509,
    checkListName: 'Tilsyn',
    schemaUnavailable: false,
    columns: [{key: 'f1', fieldId: 1, label: 'Note', fieldType: 'Text'}],
    cases: [caseModel()],
    ...overrides,
  });

  const group = (
    overrides: Partial<ComplianceReportHeadlineGroupModel> = {}
  ): ComplianceReportHeadlineGroupModel => ({
    headlineTagId: 7,
    headlineName: 'Brandsikkerhed og beredskab',
    tagsCaption: 'Miljøtilsyn - Brand',
    templates: [template()],
    ...overrides,
  });

  /** Server order: by caption, then headline, then id; the fallback group LAST. */
  const groups: ComplianceReportHeadlineGroupModel[] = [
    group(),
    group({
      headlineTagId: 8,
      headlineName: 'Lovpligtig dokumentation',
      tagsCaption: 'Miljøtilsyn - Dokumentation',
      // ONE headline answered on TWO eForms: one section, two tables, each
      // with only its own columns (#1276). The server orders them by name.
      templates: [
        template({
          checkListId: 511,
          checkListName: 'Kontrol',
          columns: [
            {key: 'f20', fieldId: 20, label: 'Temperatur', fieldType: 'Number'},
            {key: 'f21', fieldId: 21, label: 'KOMMENTAR', fieldType: 'Comment'},
          ],
          cases: [caseModel({complianceId: 3, checkListId: 511, cells: {f20: '21'}})],
        }),
        template({
          checkListId: 509,
          checkListName: 'Tilsyn',
          columns: [
            {key: 'f1', fieldId: 1, label: 'Note', fieldType: 'Text'},
            {key: 'f2', fieldId: 2, label: 'KOMMENTAR', fieldType: 'Comment'},
          ],
          cases: [caseModel({complianceId: 2, checkListId: 509, cells: {f1: 'Ja'}})],
        }),
      ],
    }),
    group({
      headlineTagId: 9,
      headlineName: 'Tom',
      tagsCaption: '',
      templates: [],
    }),
    group({
      headlineTagId: null,
      headlineName: null,
      tagsCaption: '',
      templates: [template({cases: [caseModel({complianceId: 4, tags: []})]})],
    }),
  ];

  it('emits ONE section per headline group, in server order, fallback last', () => {
    const sections = buildComplianceReportSections(groups, FALLBACK);

    expect(sections.map((s) => [s.captionLabel, s.headlineLabel])).toEqual([
      ['Miljøtilsyn - Brand', 'Brandsikkerhed og beredskab'],
      ['Miljøtilsyn - Dokumentation', 'Lovpligtig dokumentation'],
      ['', FALLBACK],
    ]);
  });

  it('heads a headline answered on two eForms ONCE, with one table per eForm in server order', () => {
    // #1276 reverses #1188's union: not one merged table under the headline,
    // but one table per eForm, titled with its name.
    const sections = buildComplianceReportSections(groups, FALLBACK);
    const mixed = sections[1];

    expect(sections.filter((s) => s.headlineLabel === 'Lovpligtig dokumentation').length).toBe(1);
    expect(mixed.tables.map((t) => [t.checkListId, t.templateLabel])).toEqual([
      [511, 'Kontrol'],
      [509, 'Tilsyn'],
    ]);
    expect(mixed.tables.map((t) => t.cases.map((c) => c.complianceId))).toEqual([[3], [2]]);
  });

  it('gives each table ONLY its own eForm\'s columns — a shared label is not a shared column', () => {
    // Both eForms have a `KOMMENTAR`; each table has exactly one of them.
    const [, mixed] = buildComplianceReportSections(groups, FALLBACK);

    expect(mixed.tables.map((t) => t.columns.map((c) => c.key))).toEqual([
      ['f20', 'f21'],
      ['f1', 'f2'],
    ]);
    for (const table of mixed.tables) {
      expect(table.columns.filter((c) => c.label === 'KOMMENTAR').length).toBe(1);
    }
  });

  it('keys every table h{headline|none}-c{checkListId}, unique across the whole page', () => {
    // One eForm (509) answered under three headlines is three tables; the
    // section prefix is what keeps their trackBy and DOM ids apart.
    const keys = buildComplianceReportSections(groups, FALLBACK).flatMap((s) =>
      s.tables.map((t) => t.key)
    );

    expect(keys).toEqual(['h7-c509', 'h8-c511', 'h8-c509', 'hnone-c509']);
    expect(new Set(keys).size).toBe(keys.length);
  });

  it('flags a schema gap on the ONE table whose eForm lacks a schema', () => {
    const [only] = buildComplianceReportSections(
      [
        group({
          templates: [
            template({checkListId: 509, schemaUnavailable: true, columns: []}),
            template({checkListId: 511, checkListName: 'Kontrol'}),
          ],
        }),
      ],
      FALLBACK
    );

    expect(only.tables.map((t) => t.schemaUnavailable)).toEqual([true, false]);
    // The cases of the schema-less eForm are KEPT: only its columns are missing.
    expect(only.tables[0].columns).toEqual([]);
    expect(only.tables[0].cases.length).toBe(1);
  });

  it('drops a table with no cases, and a group left with no tables', () => {
    const sections = buildComplianceReportSections(
      [
        ...groups,
        group({
          headlineTagId: 10,
          headlineName: 'Kun tomme',
          templates: [template({cases: []})],
        }),
        group({
          headlineTagId: 11,
          headlineName: 'Blandet',
          templates: [template({checkListId: 509, cases: []}), template({checkListId: 511})],
        }),
      ],
      FALLBACK
    );

    expect(sections.some((s) => s.headlineLabel === 'Tom')).toBe(false);
    expect(sections.some((s) => s.headlineLabel === 'Kun tomme')).toBe(false);
    expect(sections.find((s) => s.headlineLabel === 'Blandet')!.tables.map((t) => t.key)).toEqual([
      'h11-c511',
    ]);
  });

  it('keys sections h{id} and hnone, distinct, and labels an unresolvable headline #{id}', () => {
    const sections = buildComplianceReportSections(
      [...groups, group({headlineTagId: 42, headlineName: null, tagsCaption: 'Zzz'})],
      FALLBACK
    );
    const keys = sections.map((s) => s.key);

    expect(new Set(keys).size).toBe(keys.length);
    expect(keys).toEqual(['h7', 'h8', 'hnone', 'h42']);
    // `#42`, NOT merged into the fallback section.
    expect(sections[3].headlineLabel).toBe('#42');
    expect(sections[3].key).not.toBe('hnone');
  });

  it('is safe for a null response and for a group or table missing its optional lists', () => {
    expect(buildComplianceReportSections(null, FALLBACK)).toEqual([]);
    expect(buildComplianceReportSections(undefined, FALLBACK)).toEqual([]);
    expect(
      buildComplianceReportSections([group({templates: null as any})], FALLBACK)
    ).toEqual([]);

    const [bare] = buildComplianceReportSections(
      [
        {
          headlineTagId: 7,
          headlineName: 'X',
          tagsCaption: null as any,
          templates: [
            null as any,
            {
              checkListId: 509,
              checkListName: null as any,
              schemaUnavailable: null as any,
              columns: null as any,
              cases: [caseModel()],
            },
          ],
        },
      ],
      FALLBACK
    );
    expect(bare.captionLabel).toBe('');
    expect(bare.tables.length).toBe(1);
    expect(bare.tables[0].templateLabel).toBe('#509');
    expect(bare.tables[0].schemaUnavailable).toBe(false);
    expect(bare.tables[0].columns).toEqual([]);
  });
});

describe('complianceWorkerNames', () => {
  it('joins the names', () => {
    expect(complianceWorkerNames(['Anna', 'Bo'])).toBe('Anna, Bo');
  });

  it('drops blanks and returns an empty string for nothing', () => {
    expect(complianceWorkerNames(['Anna', '  ', null as any])).toBe('Anna');
    expect(complianceWorkerNames([])).toBe('');
    expect(complianceWorkerNames(null)).toBe('');
  });
});

describe('formatComplianceReportDate', () => {
  it('formats dd.MM.yyyy with zero padding', () => {
    expect(formatComplianceReportDate(new Date(2026, 0, 1))).toBe('01.01.2026');
    expect(formatComplianceReportDate(new Date(2026, 8, 3))).toBe('03.09.2026');
  });

  it('reads locally-constructed calendar dates in local time by default', () => {
    // `periodBounds` builds local midnights; reading those in UTC would move
    // the meta line's period back a day in every timezone east of UTC.
    const localMidnight = new Date(2026, 0, 1);

    expect(formatComplianceReportDate(localMidnight)).toBe('01.01.2026');
    expect(formatComplianceReportDate(localMidnight, false)).toBe('01.01.2026');
  });

  it('reads a wire-derived instant in UTC when asked', () => {
    expect(formatComplianceReportDate(new Date('2026-01-02T23:30:00Z'), true)).toBe(
      '02.01.2026'
    );
    expect(formatComplianceReportDate(new Date('2026-01-02T00:30:00Z'), true)).toBe(
      '02.01.2026'
    );
  });

  it('returns an empty string for no date', () => {
    expect(formatComplianceReportDate(null)).toBe('');
  });
});
