import {
  ComplianceReportCaseModel,
  ComplianceReportHeadlineGroupModel,
} from '../../../models';
import {
  buildComplianceReportSections,
  complianceAnswerText,
  complianceHeadlineLabel,
  complianceWorkerNames,
  formatComplianceReportDate,
} from './compliance-report-sections';
import {COMPLIANCE_EMPTY_CELL} from './compliance-week-grouping';

/**
 * The rules of the Rapport view that must not be re-derived wrong (#1167,
 * regrouped by headline in #1188).
 *
 * The headline one is `complianceAnswerText`: a cell is looked up BY COLUMN
 * KEY, an absent key renders the en dash IN PLACE, and therefore no later
 * column can shift. That is the property that makes the #1160-finding-3 desync
 * bug class inexpressible in this view rather than merely absent from it, so it
 * is asserted against a schema whose middle key is missing from the cell bag —
 * the exact shape that shifts every subsequent column under positional
 * addressing.
 *
 * Six describes: 9 on `complianceAnswerText` (the keyed lookup, and the two on
 * the `Date` values the global DateInterceptor leaves in a bag it has no
 * business walking), 4 on the headline label rule, 8 on the mapper (one
 * section per headline, caption/heading, the union of columns, the two schema
 * notices), 2 on the worker join and 4 on the date format. The GRID rules —
 * the `answer_` prefix, the duplicate-key dedupe, the per-section column-array
 * identity and the row's OWN `checkListId` — live on the component and are
 * pinned in `compliance-report-view.component.spec.ts`.
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

describe('buildComplianceReportSections', () => {
  const FALLBACK = 'Uden rapportoverskrift';

  const group = (
    overrides: Partial<ComplianceReportHeadlineGroupModel> = {}
  ): ComplianceReportHeadlineGroupModel => ({
    headlineTagId: 7,
    headlineName: 'Brandsikkerhed og beredskab',
    tagsCaption: 'Miljøtilsyn - Brand',
    checkListIds: [509],
    schemaUnavailableCheckListIds: [],
    columns: [{key: 'f1', fieldId: 1, label: 'Note', fieldType: 'Text'}],
    cases: [caseModel()],
    ...overrides,
  });

  /** Server order: by caption, then headline, then id; the fallback group LAST. */
  const groups: ComplianceReportHeadlineGroupModel[] = [
    group(),
    group({
      headlineTagId: 8,
      headlineName: 'Lovpligtig dokumentation',
      tagsCaption: 'Miljøtilsyn - Dokumentation',
      // ONE headline answered on TWO templates: one section, union columns.
      checkListIds: [509, 511],
      schemaUnavailableCheckListIds: [511],
      columns: [
        {key: 'f1', fieldId: 1, label: 'Note', fieldType: 'Text'},
        {key: 'f20', fieldId: 20, label: 'Temperatur', fieldType: 'Number'},
      ],
      cases: [
        caseModel({complianceId: 2, checkListId: 509, cells: {f1: 'Ja'}}),
        caseModel({complianceId: 3, checkListId: 511, cells: {f20: '21'}}),
      ],
    }),
    group({
      headlineTagId: 9,
      headlineName: 'Tom',
      tagsCaption: '',
      checkListIds: [],
      columns: [],
      cases: [],
    }),
    group({
      headlineTagId: null,
      headlineName: null,
      tagsCaption: '',
      checkListIds: [509],
      cases: [caseModel({complianceId: 4, tags: []})],
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

  it('keeps a headline answered on two templates as ONE section with the union of columns', () => {
    // Not two tables under one heading (#1188 decision 1): the section spans
    // both templates and its columns are the server-built union.
    const [, mixed] = buildComplianceReportSections(groups, FALLBACK);

    expect(mixed.checkListIds).toEqual([509, 511]);
    expect(mixed.columns.map((c) => c.key)).toEqual(['f1', 'f20']);
    expect(mixed.cases.map((c) => c.complianceId)).toEqual([2, 3]);
  });

  it('renders the dash IN PLACE under the foreign template\'s fields', () => {
    // The case answered on 509 has no `f20`; the case answered on 511 has no
    // `f1`. Each renders the dash under the other template's column, aligned.
    const [, mixed] = buildComplianceReportSections(groups, FALLBACK);
    const keys = mixed.columns.map((c) => c.key);

    expect(keys.map((k) => complianceAnswerText(mixed.cases[0], k))).toEqual(['Ja', COMPLIANCE_EMPTY_CELL]);
    expect(keys.map((k) => complianceAnswerText(mixed.cases[1], k))).toEqual([COMPLIANCE_EMPTY_CELL, '21']);
  });

  it('flags a PARTIAL schema gap per template, not for the whole section', () => {
    const [, mixed] = buildComplianceReportSections(groups, FALLBACK);

    expect(mixed.schemaUnavailable).toBe(false);
    expect(mixed.schemaUnavailableCheckListIds).toEqual([511]);
  });

  it('flags the WHOLE section only when every template in it lacks a schema', () => {
    const [only] = buildComplianceReportSections(
      [group({checkListIds: [509, 511], schemaUnavailableCheckListIds: [511, 509], columns: []})],
      FALLBACK
    );
    expect(only.schemaUnavailable).toBe(true);
    expect(only.columns).toEqual([]);
    expect(only.cases.length).toBe(1);

    // No gap at all → no notice of either kind.
    const [clean] = buildComplianceReportSections([group()], FALLBACK);
    expect(clean.schemaUnavailable).toBe(false);
    expect(clean.schemaUnavailableCheckListIds).toEqual([]);
  });

  it('drops a group with no cases', () => {
    const sections = buildComplianceReportSections(groups, FALLBACK);

    expect(sections.length).toBe(3);
    expect(sections.some((s) => s.headlineLabel === 'Tom')).toBe(false);
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

  it('is safe for a null response and for a group missing its optional lists', () => {
    expect(buildComplianceReportSections(null, FALLBACK)).toEqual([]);
    expect(buildComplianceReportSections(undefined, FALLBACK)).toEqual([]);

    const [bare] = buildComplianceReportSections(
      [
        {
          headlineTagId: 7,
          headlineName: 'X',
          tagsCaption: null as any,
          checkListIds: null as any,
          schemaUnavailableCheckListIds: null as any,
          columns: null as any,
          cases: [caseModel()],
        },
      ],
      FALLBACK
    );
    expect(bare.captionLabel).toBe('');
    expect(bare.checkListIds).toEqual([]);
    expect(bare.schemaUnavailableCheckListIds).toEqual([]);
    expect(bare.schemaUnavailable).toBe(false);
    expect(bare.columns).toEqual([]);
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
