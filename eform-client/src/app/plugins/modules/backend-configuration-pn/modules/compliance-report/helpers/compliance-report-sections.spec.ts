import {
  ComplianceReportCaseModel,
  ComplianceReportTagGroupModel,
} from '../../../models';
import {
  buildComplianceReportSections,
  complianceAnswerText,
  complianceTagGroupLabel,
  complianceTemplateLabel,
  complianceWorkerNames,
  formatComplianceReportDate,
} from './compliance-report-sections';
import {COMPLIANCE_EMPTY_CELL} from './compliance-week-grouping';

/**
 * The rules of the Rapport view that must not be re-derived wrong (#1167).
 *
 * The headline one is `complianceAnswerText`: a cell is looked up BY COLUMN
 * KEY, an absent key renders the en dash IN PLACE, and therefore no later
 * column can shift. That is the property that makes the #1160-finding-3 desync
 * bug class inexpressible in this view rather than merely absent from it, so it
 * is asserted against a schema whose middle key is missing from the cell bag —
 * the exact shape that shifts every subsequent column under positional
 * addressing.
 *
 * 23 cases in six describes: 9 on `complianceAnswerText` (the keyed lookup, and
 * the two on the `Date` values the global DateInterceptor leaves in a bag it
 * has no business walking), 3 + 2 on the two label rules, 5 on the flattener,
 * 2 on the worker join and 2 on the date format. The GRID rules — the `answer_`
 * prefix, the duplicate-key dedupe and the per-section column-array identity —
 * live on the component and are pinned in
 * `compliance-report-view.component.spec.ts`.
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

describe('complianceTagGroupLabel', () => {
  it('labels the genuinely untagged group with the untagged label', () => {
    expect(
      complianceTagGroupLabel({tagId: null, tagName: null, templates: []}, 'Uden tag')
    ).toBe('Uden tag');
  });

  it('labels a NAMED group whose name could not be resolved as #{tagId}, not as untagged', () => {
    expect(
      complianceTagGroupLabel({tagId: 42, tagName: null, templates: []}, 'Uden tag')
    ).toBe('#42');
    expect(
      complianceTagGroupLabel({tagId: 42, tagName: '   ', templates: []}, 'Uden tag')
    ).toBe('#42');
  });

  it('uses the tag name when there is one', () => {
    expect(
      complianceTagGroupLabel({tagId: 7, tagName: 'Miljøtilsyn', templates: []}, 'Uden tag')
    ).toBe('Miljøtilsyn');
  });
});

describe('complianceTemplateLabel', () => {
  it('uses the template name', () => {
    expect(
      complianceTemplateLabel({
        checkListId: 509,
        checkListName: 'Brandtjek',
        mergedCheckListIds: [509],
        columns: [],
        schemaUnavailable: false,
        cases: [],
      })
    ).toBe('Brandtjek');
  });

  it('falls back to #{checkListId} for an unnamed template', () => {
    expect(
      complianceTemplateLabel({
        checkListId: 509,
        checkListName: null,
        mergedCheckListIds: [509],
        columns: [],
        schemaUnavailable: false,
        cases: [],
      })
    ).toBe('#509');
  });
});

describe('buildComplianceReportSections', () => {
  const groups: ComplianceReportTagGroupModel[] = [
    {
      tagId: 7,
      tagName: 'Miljøtilsyn',
      templates: [
        {
          checkListId: 509,
          checkListName: 'Brandtjek',
          mergedCheckListIds: [509],
          columns: [{key: 'f1', fieldId: 1, label: 'Note', fieldType: 'Text'}],
          schemaUnavailable: false,
          cases: [caseModel()],
        },
        {
          // Same tag, a SECOND template — the divergence from the prototype's
          // tag-set-only grouping: two tables, each with its own column set.
          checkListId: 511,
          checkListName: 'Eltjek',
          mergedCheckListIds: [511],
          columns: [],
          schemaUnavailable: true,
          cases: [caseModel({complianceId: 2})],
        },
        {
          checkListId: 555,
          checkListName: 'Ingen svar',
          mergedCheckListIds: [555],
          columns: [],
          schemaUnavailable: false,
          cases: [],
        },
      ],
    },
    {
      tagId: null,
      tagName: null,
      templates: [
        {
          checkListId: 509,
          checkListName: 'Brandtjek',
          mergedCheckListIds: [509],
          columns: [],
          schemaUnavailable: false,
          cases: [caseModel({complianceId: 3})],
        },
      ],
    },
  ];

  it('emits one section per tag group PER template, in server order', () => {
    const sections = buildComplianceReportSections(groups, 'Uden tag');

    expect(sections.map((s) => [s.tagLabel, s.templateLabel])).toEqual([
      ['Miljøtilsyn', 'Brandtjek'],
      ['Miljøtilsyn', 'Eltjek'],
      ['Uden tag', 'Brandtjek'],
    ]);
  });

  it('drops a template group with no cases', () => {
    const sections = buildComplianceReportSections(groups, 'Uden tag');

    expect(sections.some((s) => s.templateLabel === 'Ingen svar')).toBe(false);
  });

  it('keeps a schemaUnavailable group that HAS cases, and flags it', () => {
    const sections = buildComplianceReportSections(groups, 'Uden tag');

    expect(sections[1].schemaUnavailable).toBe(true);
    expect(sections[1].columns).toEqual([]);
    expect(sections[1].cases.length).toBe(1);
  });

  it('gives the same template under two tag groups two DISTINCT keys', () => {
    // `t`/`c` prefixes, so (tag 75, template 11) cannot collide with
    // (tag 7, template 511).
    const sections = buildComplianceReportSections(groups, 'Uden tag');
    const keys = sections.map((s) => s.key);

    expect(new Set(keys).size).toBe(keys.length);
    expect(keys).toEqual(['t7-c509', 't7-c511', 'tnone-c509']);
  });

  it('is safe for a null response', () => {
    expect(buildComplianceReportSections(null, 'Uden tag')).toEqual([]);
    expect(buildComplianceReportSections(undefined, 'Uden tag')).toEqual([]);
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
