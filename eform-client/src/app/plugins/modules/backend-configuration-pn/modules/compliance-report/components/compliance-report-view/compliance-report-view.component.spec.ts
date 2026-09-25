import {ComponentFixture, TestBed} from '@angular/core/testing';
import {NO_ERRORS_SCHEMA} from '@angular/core';
import {MatDialog} from '@angular/material/dialog';
import {Router} from '@angular/router';
import {TranslateModule} from '@ngx-translate/core';
import {Subject, of} from 'rxjs';
import {
  ComplianceReportCaseModel,
  ComplianceReportColumnModel,
  ComplianceReportImageModel,
  ComplianceReportHeadlineGroupModel,
} from '../../../../models';
import {
  BackendConfigurationPnCalendarService,
  BackendConfigurationPnComplianceReportService,
  BackendConfigurationPnCompliancesService,
  BackendConfigurationPnPropertiesService,
} from '../../../../services';
import {COMPLIANCE_EMPTY_CELL, ComplianceReportTable} from '../../helpers';
import {ComplianceReportStateService} from '../../store';
import {ComplianceReportViewComponent} from './compliance-report-view.component';

/**
 * The meta line's reference data (#1329) is loaded on mount — the properties
 * dictionary always, the calendars and employees for the current property — so
 * every fixture's stubs answer with an empty list rather than `undefined`.
 */
const emptyList = () => of({success: true, model: []});
const referenceDataPropertiesService = () => ({
  getAllPropertiesDictionary: jest.fn().mockReturnValue(emptyList()),
  getDeviceUsersFiltered: jest.fn().mockReturnValue(emptyList()),
});
const referenceDataCalendarService = () => ({
  getBoards: jest.fn().mockReturnValue(emptyList()),
});

/**
 * The grid rules of the Rapport view, none of which the helper spec can
 * reach: `buildGridColumns` lives on the component because it needs the
 * translate stream and the cell TemplateRefs. (A grid is one eForm's TABLE
 * since #1276 — under a section that is one REPORT HEADLINE since #1188 — and
 * its columns are that eForm's only; the rules below did not change, the
 * fixtures did.)
 *
 * Each of the first three blanks a whole table when it regresses, and none of
 * them fails loudly in a way a reviewer would spot:
 *
 *  1. **the `answer_` prefix** — `field` goes straight into MatTable's
 *     `displayedColumns` and becomes a `mat-column-{field}` class. A bare key
 *     could also collide with one of the six fixed metadata fields;
 *  2. **the duplicate-key dedupe** — two identical `displayedColumns` entries
 *     make MatTable throw `getTableDuplicateColumnNameError`, which takes the
 *     ENTIRE grid down, not the one column;
 *  3. **per-table column-array IDENTITY** — mtx-grid's `_countPinnedPosition`
 *     MUTATES `left`/`right` onto the column objects it is given, so two
 *     tables sharing one array (or one column object) would have the wider
 *     one's pin offsets leak into the narrower one and its frozen block overlap
 *     itself — now also two tables under ONE headline.
 *
 * The fourth is #1276's: the column's `fieldType` must reach the grid column,
 * or the answer cell cannot draw a CheckBox tick or format a Date.
 *
 * TestBed rather than `new`, unlike the Oversigt spec next door: this component
 * takes eight injectables and `buildGridColumns` reads three `@ViewChild`
 * TemplateRefs, which only exist once the template has been created.
 * `NO_ERRORS_SCHEMA` keeps mtx-grid and the Material elements out of it — this
 * asserts the COLUMN MODEL, never the DOM.
 */
describe('ComplianceReportViewComponent — buildGridColumns', () => {
  let fixture: ComponentFixture<ComplianceReportViewComponent>;
  let component: ComplianceReportViewComponent;

  /** The six pinned metadata fields, in the prototype's order. */
  const FIXED_FIELDS = [
    'sdkCaseId',
    'propertyName',
    'doneBy',
    'doneAt',
    'title',
    'imagesCount',
  ];

  const column = (key: string, label = key): ComplianceReportColumnModel => ({
    key,
    fieldId: Number(key.replace(/\D/g, '')) || 0,
    label,
    fieldType: 'Text',
  });

  const caseModel = (complianceId: number): ComplianceReportCaseModel => ({
    complianceId,
    sdkCaseId: 100 + complianceId,
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
  });

  const table = (columns: ComplianceReportColumnModel[]): ComplianceReportTable => ({
    key: 'h7-c509',
    checkListId: 509,
    templateLabel: 'Tilsyn',
    schemaUnavailable: false,
    columns,
    cases: [caseModel(1)],
  });

  /** `buildGridColumns` is private; the guarantees it owns are not. */
  const build = (columns: ComplianceReportColumnModel[]) =>
    (component as any).buildGridColumns(table(columns)) as {
      field: string;
      answerKey?: string;
      answerFieldType?: string;
    }[];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [ComplianceReportViewComponent],
      imports: [TranslateModule.forRoot()],
      providers: [
        // The REAL state service (it is `@Injectable()` with no providedIn and
        // no dependencies of its own), so nothing here can drift from it.
        // Nothing fetches: `fetchRequested$` is gated on `reportVisible`, which
        // a fresh instance leaves false.
        ComplianceReportStateService,
        {provide: BackendConfigurationPnComplianceReportService, useValue: {eformColumns: jest.fn()}},
        {provide: BackendConfigurationPnCompliancesService, useValue: {deleteCompliance: jest.fn()}},
        {provide: BackendConfigurationPnPropertiesService, useValue: referenceDataPropertiesService()},
        {provide: BackendConfigurationPnCalendarService, useValue: referenceDataCalendarService()},
        {provide: MatDialog, useValue: {open: jest.fn()}},
        {provide: Router, useValue: {navigate: jest.fn(), url: '/plugins/backend-configuration-pn/compliance-report'}},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    }).compileComponents();

    fixture = TestBed.createComponent(ComplianceReportViewComponent);
    component = fixture.componentInstance;
    // Required, not incidental: the `static: true` ViewChild TemplateRefs the
    // answer/images/actions cells bind to are resolved by the first creation
    // pass.
    fixture.detectChanges();
  });

  it('prefixes every answer field with answer_ and keeps the BARE key as answerKey', () => {
    const columns = build([column('f1', 'Note'), column('f2', 'Temperatur')]);
    const answers = columns.filter((c) => c.answerKey !== undefined);

    expect(answers.map((c) => c.field)).toEqual(['answer_f1', 'answer_f2']);
    // The lookup key is the wire key, unprefixed — `complianceAnswerText` reads
    // the cell bag with it.
    expect(answers.map((c) => c.answerKey)).toEqual(['f1', 'f2']);
  });

  it('cannot collide with a fixed metadata field, even for a key named after one', () => {
    // `title` and `doneAt` are real fixed fields. Without the prefix these two
    // answer columns would duplicate them and MatTable would throw.
    const columns = build([column('title'), column('doneAt')]);

    expect(columns.map((c) => c.field)).toEqual([
      ...FIXED_FIELDS,
      'answer_title',
      'answer_doneAt',
      'actions',
    ]);
    expect(new Set(columns.map((c) => c.field)).size).toBe(columns.length);
  });

  it('emits a duplicated key ONCE, keeping the first occurrence', () => {
    const columns = build([
      column('f1', 'Note'),
      column('f1', 'Note (igen)'),
      column('f2', 'Temperatur'),
    ]);
    const answers = columns.filter((c) => c.answerKey !== undefined);

    expect(answers.map((c) => c.field)).toEqual(['answer_f1', 'answer_f2']);
    expect((answers[0] as any).header).toBe('Note');
  });

  it('never produces two identical fields, whatever the projection sends', () => {
    // The property that matters is not "dedupe happened" but "displayedColumns
    // is unique" — that is the input MatTable actually rejects.
    const columns = build([
      column('f1'),
      column('f1'),
      column('f1'),
      column('f2'),
      column('f2'),
    ]);

    expect(new Set(columns.map((c) => c.field)).size).toBe(columns.length);
  });

  it('skips a column with no key rather than emitting a bare answer_ field', () => {
    const columns = build([
      {key: '', fieldId: 0, label: 'Tom', fieldType: 'Text'},
      column('f1'),
      null as any,
    ]);

    expect(columns.map((c) => c.field)).toEqual([...FIXED_FIELDS, 'answer_f1', 'actions']);
  });

  it('carries each answer column\'s fieldType onto its grid column (#1276)', () => {
    const columns = build([
      {key: 'f1', fieldId: 1, label: 'Udført', fieldType: 'CheckBox'},
      {key: 'f2', fieldId: 2, label: 'Dato', fieldType: 'Date'},
      column('f3'),
    ]);

    expect(
      columns.filter((c) => c.answerKey !== undefined).map((c) => [c.answerKey, c.answerFieldType])
    ).toEqual([
      ['f1', 'CheckBox'],
      ['f2', 'Date'],
      ['f3', 'Text'],
    ]);
    // The fixed metadata columns are not answers and carry no field type.
    expect(columns.filter((c) => c.answerFieldType !== undefined).length).toBe(3);
  });

  it('gives every table its OWN column array and its OWN column objects', () => {
    // Through the real response path, because that is where the sharing bug
    // would be introduced — across sections AND between two tables of one.
    const groups: ComplianceReportHeadlineGroupModel[] = [
      {
        headlineTagId: 7,
        headlineName: 'Brandsikkerhed og beredskab',
        tagsCaption: 'Miljøtilsyn - Brand',
        templates: [
          {checkListId: 509, checkListName: 'Tilsyn', schemaUnavailable: false, columns: [column('f1')], cases: [caseModel(1)]},
          {checkListId: 511, checkListName: 'Kontrol', schemaUnavailable: false, columns: [column('f2')], cases: [caseModel(2)]},
        ],
      },
      {
        headlineTagId: 8,
        headlineName: 'Elinstallationer og eftersyn',
        tagsCaption: 'Miljøtilsyn - EL',
        templates: [
          {checkListId: 509, checkListName: 'Tilsyn', schemaUnavailable: false, columns: [column('f1')], cases: [caseModel(3)]},
        ],
      },
    ];

    (component as any).applyResponse(groups);
    const tables = component.sections.flatMap((s) => s.tables);

    expect(tables.length).toBe(3);
    for (let i = 0; i < tables.length; i++) {
      for (let j = i + 1; j < tables.length; j++) {
        expect(tables[i].gridColumns).not.toBe(tables[j].gridColumns);
        // Object identity too, not just the array: mtx-grid writes
        // `left`/`right` onto the COLUMN, so one shared object is enough to
        // leak an offset.
        for (const col of tables[i].gridColumns) {
          expect(tables[j].gridColumns).not.toContain(col);
        }
      }
    }

    // The mutation mtx-grid performs, simulated: it must not be visible from
    // any other table.
    (tables[0].gridColumns[0] as any).left = '999px';
    expect((tables[1].gridColumns[0] as any).left).toBeUndefined();
    expect((tables[2].gridColumns[0] as any).left).toBeUndefined();
  });
});

/**
 * The two ceilings on the initial DOM. The per-table cap alone bounds
 * nothing: a section is one REPORT HEADLINE with one table per eForm under it
 * (#1276), so a realistic filter set can yield dozens of small tables, none of
 * which reaches the cap, and the server's whole 5000-row allowance lands on
 * the page at once.
 *
 * Both ceilings must leave the table REVEALABLE — headings, true row count and
 * the `Vis alle` control — or a user cannot reach the table they came for.
 */
describe('ComplianceReportViewComponent — the row ceilings', () => {
  let component: ComplianceReportViewComponent;

  const caseModel = (complianceId: number): ComplianceReportCaseModel => ({
    complianceId,
    sdkCaseId: 100 + complianceId,
    propertyId: 5,
    propertyName: 'Ejendom A',
    title: 'Område 1',
    taskDate: '2026-08-11',
    completed: true,
    doneAt: null,
    workerNames: [],
    checkListId: 509,
    tags: [],
    cells: {},
    imagesCount: 0,
    images: [],
  });

  /**
   * `n` headline groups of `eForms` tables of `rows` cases each — one table
   * per group unless asked otherwise.
   */
  const groups = (n: number, rows: number, eForms = 1): ComplianceReportHeadlineGroupModel[] => {
    let complianceId = 0;
    return Array.from({length: n}, (_, i) => ({
      headlineTagId: 100 + i,
      headlineName: `Overskrift ${i}`,
      tagsCaption: 'Miljøtilsyn',
      templates: Array.from({length: eForms}, (__, t) => ({
        checkListId: 509 + t,
        checkListName: `eForm ${t}`,
        schemaUnavailable: false,
        columns: [],
        cases: Array.from({length: rows}, () => caseModel(++complianceId)),
      })),
    }));
  };

  /** Every rendered table on the page, in page order. */
  const tables = () => component.sections.flatMap((s) => s.tables);

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [ComplianceReportViewComponent],
      imports: [TranslateModule.forRoot()],
      providers: [
        ComplianceReportStateService,
        {provide: BackendConfigurationPnComplianceReportService, useValue: {eformColumns: jest.fn()}},
        {provide: BackendConfigurationPnCompliancesService, useValue: {deleteCompliance: jest.fn()}},
        {provide: BackendConfigurationPnPropertiesService, useValue: referenceDataPropertiesService()},
        {provide: BackendConfigurationPnCalendarService, useValue: referenceDataCalendarService()},
        {provide: MatDialog, useValue: {open: jest.fn()}},
        {provide: Router, useValue: {navigate: jest.fn(), url: '/x'}},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    }).compileComponents();

    const fixture = TestBed.createComponent(ComplianceReportViewComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('caps ONE big table at the per-table cap', () => {
    (component as any).applyResponse(groups(1, 250));
    const [only] = tables();

    expect(only.rows.length).toBe(100);
    expect(only.allRows.length).toBe(250);
    expect(only.expanded).toBe(false);
  });

  it('bounds the whole page when many small tables each stay under that cap', () => {
    // 40 tables × 20 rows = 800 rows, and no single table ever caps — the
    // shape the per-table cap does not bound at all.
    (component as any).applyResponse(groups(40, 20));

    const revealed = tables().reduce((sum, t) => sum + t.rows.length, 0);
    expect(revealed).toBe(500);
    // Nothing is dropped: every table is still on the page, and the count it
    // reports back is the TRUE one (the filter bar's Download gate reads it).
    expect(tables().length).toBe(40);
    expect(tables().reduce((sum, t) => sum + t.allRows.length, 0)).toBe(800);
  });

  it('leaves a budget-collapsed table revealable, with its true row count', () => {
    (component as any).applyResponse(groups(40, 20));
    const collapsed = tables()[39];

    expect(collapsed.rows.length).toBe(0);
    expect(collapsed.allRows.length).toBe(20);
    // `expanded` false is what renders the "Viser 0 af 20" footer AND its
    // `Vis alle` button — the same control the per-table cap uses.
    expect(collapsed.expanded).toBe(false);

    component.expandTable(collapsed);

    expect(collapsed.rows.length).toBe(20);
    expect(collapsed.expanded).toBe(true);
  });

  it('spends the budget in server order, so the first tables render whole', () => {
    (component as any).applyResponse(groups(40, 20));

    // 25 tables × 20 = the 500-row budget exactly; the 26th gets nothing.
    expect(tables().slice(0, 25).every((t) => t.rows.length === 20)).toBe(true);
    expect(tables().slice(0, 25).every((t) => t.expanded)).toBe(true);
    expect(tables().slice(25).every((t) => t.rows.length === 0)).toBe(true);
  });

  it('spends the budget across the tables of ONE section too, not per section', () => {
    // 10 headlines × 3 eForms × 20 rows = 600 rows in 30 tables: a budget
    // spent per SECTION would reveal all 600.
    (component as any).applyResponse(groups(10, 20, 3));

    expect(component.sections.length).toBe(10);
    expect(tables().length).toBe(30);
    expect(tables().reduce((sum, t) => sum + t.rows.length, 0)).toBe(500);
    // 25 whole tables = 8 whole sections plus the first table of the 9th.
    expect(component.sections[8].tables.map((t) => t.rows.length)).toEqual([20, 0, 0]);
  });

  it('leaves a result that fits under both ceilings fully expanded', () => {
    (component as any).applyResponse(groups(3, 20, 2));

    expect(tables().every((t) => t.expanded)).toBe(true);
    expect(tables().every((t) => t.rows.length === t.allRows.length)).toBe(true);
  });

  it('never spends the page budget on a headline-less group (#1301)', () => {
    // A headline-less group whose five 100-row tables alone would spend the
    // whole 500-row budget, placed FIRST: if it were mapped, the headlined
    // table behind it would render collapsed with 0 rows.
    const headlineLess: ComplianceReportHeadlineGroupModel = {
      headlineTagId: null,
      headlineName: null,
      tagsCaption: 'Aa tag',
      templates: Array.from({length: 5}, (_, t) => ({
        checkListId: 700 + t,
        checkListName: `Uden ${t}`,
        schemaUnavailable: false,
        columns: [],
        cases: Array.from({length: 100}, (__, i) => caseModel(10_000 + t * 100 + i)),
      })),
    };

    (component as any).applyResponse([headlineLess, ...groups(1, 20)]);

    expect(component.sections.map((s) => s.key)).toEqual(['h100']);
    expect(tables().length).toBe(1);
    expect(tables()[0].rows.length).toBe(20);
    expect(tables()[0].expanded).toBe(true);
    // The Download gate / "n rows" count excludes them too.
    expect(TestBed.inject(ComplianceReportStateService).total).toBe(20);
  });
});


/**
 * The Billeder cell and the gallery behind it (#1168).
 *
 * Nothing else covers these three. The Playwright suite for the gallery lives
 * in shard `s`, which seeds NO SQL, so the report renders no rows and every
 * data-dependent test in it skips itself — CI currently proves nothing about
 * this feature. The two describes above never reach it either: both build their
 * fixtures with `images: []`.
 *
 * All three functions are pure, so this needs no DOM. `toRowVm` is private, and
 * `NO_ERRORS_SCHEMA` again keeps mtx-grid and Material out of it.
 */
describe('ComplianceReportViewComponent — the Billeder cell', () => {
  let component: ComplianceReportViewComponent;

  /**
   * `fileName` and `thumbnailFileName` are the SERVER's `_700_`/`_300_` pair.
   * Encoding the two as derivatives of one seed is what lets the alignment
   * assertions below check a RELATION between the two output arrays instead of
   * re-stating the fixture: `n_300_x.jpg` must land opposite `n_700_x.jpg`.
   */
  const image = (
    seed: number,
    overrides: Partial<ComplianceReportImageModel> = {},
  ): ComplianceReportImageModel => ({
    fieldValueId: 1000 + seed,
    uploadedDataId: seed,
    fileName: `${seed}_700_abc.jpg`,
    thumbnailFileName: `${seed}_300_abc.jpg`,
    geoLink: null,
    ...overrides,
  });

  /** The `_300_` name that must sit opposite a given `_700_` name. */
  const thumbnailOf = (fileName: string) => fileName.replace('_700_', '_300_');

  const caseModel = (
    images: ComplianceReportImageModel[],
    imagesCount = images.length,
  ): ComplianceReportCaseModel => ({
    complianceId: 1,
    sdkCaseId: 101,
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
    imagesCount,
    images,
  });

  /** `toRowVm` is private; the alignment invariant it owns is not. */
  const rowVm = (caseModel_: ComplianceReportCaseModel) =>
    (component as any).toRowVm(caseModel_) as {
      imagesCount: number;
      imageNames: string[];
      imageThumbnailNames: (string | null)[];
    };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [ComplianceReportViewComponent],
      imports: [TranslateModule.forRoot()],
      providers: [
        ComplianceReportStateService,
        {provide: BackendConfigurationPnComplianceReportService, useValue: {eformColumns: jest.fn()}},
        {provide: BackendConfigurationPnCompliancesService, useValue: {deleteCompliance: jest.fn()}},
        {provide: BackendConfigurationPnPropertiesService, useValue: referenceDataPropertiesService()},
        {provide: BackendConfigurationPnCalendarService, useValue: referenceDataCalendarService()},
        {provide: MatDialog, useValue: {open: jest.fn()}},
        {provide: Router, useValue: {navigate: jest.fn(), url: '/x'}},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    }).compileComponents();

    const fixture = TestBed.createComponent(ComplianceReportViewComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  // -------------------------------------------------------------------
  // toRowVm — the index-alignment invariant
  // -------------------------------------------------------------------
  //
  // The lightbox reads `thumbnails[i]` for `images[i]` and NEVER re-derives a
  // name. One array shorter than the other, or one entry out of step, therefore
  // shows image i under thumbnail j — silently, because both urls resolve and
  // both render.

  it('carries a clean list through in order, one thumbnail per image', () => {
    const row = rowVm(caseModel([image(1), image(2), image(3)]));

    expect(row.imageNames).toEqual(['1_700_abc.jpg', '2_700_abc.jpg', '3_700_abc.jpg']);
    expect(row.imageThumbnailNames.length).toBe(row.imageNames.length);
    row.imageNames.forEach((name, i) => {
      expect(row.imageThumbnailNames[i]).toBe(thumbnailOf(name));
    });
  });

  it('drops an image with no usable fileName from BOTH arrays, keeping them aligned', () => {
    // The bug this guards is not "the null survived" but "one array lost an
    // entry and the other did not", which shifts every later thumbnail by one.
    const row = rowVm(
      caseModel([
        image(1),
        image(2, {fileName: null}),
        image(3),
        image(4, {fileName: ''}),
        image(5),
      ]),
    );

    expect(row.imageNames).toEqual(['1_700_abc.jpg', '3_700_abc.jpg', '5_700_abc.jpg']);
    expect(row.imageThumbnailNames.length).toBe(row.imageNames.length);
    // The relation, not the literal: thumbnail i belongs to image i.
    row.imageNames.forEach((name, i) => {
      expect(row.imageThumbnailNames[i]).toBe(thumbnailOf(name));
    });
    // And the dropped images' derivatives are gone with them — a `_300_` name
    // that outlived its `_700_` twin is exactly how the two get out of step.
    expect(row.imageThumbnailNames).not.toContain('2_300_abc.jpg');
    expect(row.imageThumbnailNames).not.toContain('4_300_abc.jpg');
  });

  it('KEEPS an image whose thumbnail name is missing, with a null in that slot', () => {
    // The opposite rule: a missing `_300_` is not a reason to drop the image.
    // The lightbox falls back to the full-size url for that one entry, which it
    // can only do if the slot is still there.
    const row = rowVm(
      caseModel([
        image(1, {thumbnailFileName: null}),
        image(2),
        image(3, {thumbnailFileName: ''}),
      ]),
    );

    expect(row.imageNames).toEqual(['1_700_abc.jpg', '2_700_abc.jpg', '3_700_abc.jpg']);
    expect(row.imageThumbnailNames).toEqual([null, '2_300_abc.jpg', null]);
    expect(row.imageThumbnailNames.length).toBe(row.imageNames.length);
    // '' is normalised to null: the lightbox's fallback tests truthiness, and an
    // empty string would otherwise be handed to it as a name.
    expect(row.imageThumbnailNames[2]).toBeNull();
  });

  it('stays aligned when BOTH kinds of gap occur in the same case', () => {
    const row = rowVm(
      caseModel([
        image(1, {thumbnailFileName: null}),
        image(2, {fileName: null}),
        image(3),
        image(4, {fileName: '', thumbnailFileName: '4_300_abc.jpg'}),
        image(5, {thumbnailFileName: ''}),
      ]),
    );

    expect(row.imageNames).toEqual(['1_700_abc.jpg', '3_700_abc.jpg', '5_700_abc.jpg']);
    expect(row.imageThumbnailNames).toEqual([null, '3_300_abc.jpg', null]);
    expect(row.imageThumbnailNames.length).toBe(row.imageNames.length);
  });

  it('survives a case with no images at all, however the server spelt it', () => {
    expect(rowVm(caseModel([])).imageNames).toEqual([]);
    expect(rowVm(caseModel([])).imageThumbnailNames).toEqual([]);

    const missing = rowVm({...caseModel([]), images: undefined as any});
    expect(missing.imageNames).toEqual([]);
    expect(missing.imageThumbnailNames).toEqual([]);
    expect(missing.imagesCount).toBe(0);
  });

  it('reports the ATTACHMENT count in the cell, not the renderable one', () => {
    // The documented divergence: `imagesCount` counts every attachment, the
    // arrays hold only the fetchable ones. Collapsing the two would either
    // under-report the case or promise a gallery that cannot open.
    const row = rowVm(caseModel([image(1), image(2, {fileName: null})], 2));

    expect(row.imagesCount).toBe(2);
    expect(row.imageNames.length).toBe(1);
  });

  // -------------------------------------------------------------------
  // canOpenGallery
  // -------------------------------------------------------------------

  it('opens the gallery only when there is something fetchable to show', () => {
    expect(component.canOpenGallery(rowVm(caseModel([image(1)])) as any)).toBe(true);
    expect(component.canOpenGallery(rowVm(caseModel([])) as any)).toBe(false);
  });

  it('refuses the gallery for a case that HAS attachments but no usable names', () => {
    // The case the guard exists for: `imagesCount` is 3, so the cell shows 3,
    // but every `_700_` name failed its existence check server-side. The cell
    // must stay the plain non-interactive count rather than become a button
    // that opens an empty lightbox.
    const row = rowVm(
      caseModel([image(1, {fileName: null}), image(2, {fileName: null}), image(3, {fileName: ''})], 3),
    );

    expect(row.imagesCount).toBe(3);
    expect(component.canOpenGallery(row as any)).toBe(false);
  });

  // -------------------------------------------------------------------
  // imagesLabelKey / imagesLabelParams
  // -------------------------------------------------------------------

  it('picks the singular key at exactly one and the plural key everywhere else', () => {
    expect(component.imagesLabelKey(1)).toBe('1 image');
    // Both sides of the boundary, and the zero the cell never renders but the
    // function must still answer for.
    expect(component.imagesLabelKey(0)).toBe('{{count}} images');
    expect(component.imagesLabelKey(2)).toBe('{{count}} images');
    expect(component.imagesLabelKey(17)).toBe('{{count}} images');
  });

  it('interpolates the count the plural key asks for', () => {
    // The key carries `{{count}}`; a params object under any other name renders
    // the placeholder verbatim in the tooltip.
    expect(component.imagesLabelParams(4)).toEqual({count: 4});
    expect(component.imagesLabelKey(4)).toContain('{{count}}');
  });

  it('labels the cell from the ATTACHMENT count, not from what the gallery can open', () => {
    // The same divergence, at the one place a user sees it: 2 attachments of
    // which 1 is fetchable reads "2 billeder" and opens a one-image gallery.
    const row = rowVm(caseModel([image(1), image(2, {fileName: null})], 2));

    expect(component.imagesLabelKey(row.imagesCount)).toBe('{{count}} images');
    expect(component.imagesLabelParams(row.imagesCount)).toEqual({count: 2});
    expect(component.canOpenGallery(row as any)).toBe(true);
    expect(row.imageNames.length).toBe(1);
  });

  it('labels a single-attachment case in the singular', () => {
    const row = rowVm(caseModel([image(1)]));

    expect(component.imagesLabelKey(row.imagesCount)).toBe('1 image');
  });
});

/**
 * One headline answered on two eForms (#1276): ONE section — the headline and
 * its tags caption shown once — holding one table per eForm, each with only
 * its own columns and a page-unique key. That reverses #1188's one merged
 * table with the union of both schemas, which is also why the old premise
 * "two rows of one table can carry different templates" no longer holds.
 *
 * `Rediger` still reads `checkListId` off the CASE — the case's fact — and
 * the total the filter bar's Download gate reads is still the plain sum of
 * cases: every case is in exactly one table of exactly one section.
 *
 * The layout half renders the component's own template; mtx-grid stays out of
 * it under `NO_ERRORS_SCHEMA`, so what is asserted is the section/table
 * structure AROUND the grids — the headings, the table keys and the `Vis alle`
 * ids — never a cell.
 */
describe('ComplianceReportViewComponent — one table per eForm under a headline', () => {
  let fixture: ComponentFixture<ComplianceReportViewComponent>;
  let component: ComplianceReportViewComponent;
  let router: {navigate: jest.Mock; url: string};
  let state: ComplianceReportStateService;

  const caseModel = (
    complianceId: number,
    checkListId: number | null,
    completed = true,
  ): ComplianceReportCaseModel => ({
    complianceId,
    sdkCaseId: 100 + complianceId,
    propertyId: 5,
    propertyName: 'Ejendom A',
    title: 'Område 1',
    taskDate: '2026-08-11',
    completed,
    doneAt: completed ? '2026-08-11T09:00:00Z' : null,
    workerNames: ['Anna'],
    checkListId,
    tags: [],
    cells: {},
    imagesCount: 0,
    images: [],
  });

  const column = (key: string, label: string, fieldType = 'Text'): ComplianceReportColumnModel => ({
    key,
    fieldId: Number(key.replace(/\D/g, '')) || 0,
    label,
    fieldType,
  });

  /**
   * ONE headline answered on TWO eForms, in the server's (name) order. Both
   * have a `KOMMENTAR`, which #1188's union put into one grid twice.
   */
  const mixedGroup = (): ComplianceReportHeadlineGroupModel[] => [
    {
      headlineTagId: 8,
      headlineName: 'Lovpligtig dokumentation',
      tagsCaption: 'Miljøtilsyn - Dokumentation',
      templates: [
        {
          checkListId: 511,
          checkListName: 'Kontrol',
          schemaUnavailable: false,
          columns: [column('f20', 'Temperatur', 'Number'), column('f21', 'KOMMENTAR', 'Comment')],
          cases: [caseModel(2, 511), caseModel(4, 511, false)],
        },
        {
          checkListId: 509,
          checkListName: 'Tilsyn',
          schemaUnavailable: false,
          columns: [column('f1', 'Udført', 'CheckBox'), column('f2', 'KOMMENTAR', 'Comment')],
          cases: [caseModel(1, 509), caseModel(3, null)],
        },
      ],
    },
  ];

  beforeEach(async () => {
    router = {navigate: jest.fn().mockResolvedValue(true), url: '/plugins/backend-configuration-pn/compliance-report'};
    await TestBed.configureTestingModule({
      declarations: [ComplianceReportViewComponent],
      imports: [TranslateModule.forRoot()],
      providers: [
        ComplianceReportStateService,
        {provide: BackendConfigurationPnComplianceReportService, useValue: {eformColumns: jest.fn()}},
        {provide: BackendConfigurationPnCompliancesService, useValue: {deleteCompliance: jest.fn()}},
        {provide: BackendConfigurationPnPropertiesService, useValue: referenceDataPropertiesService()},
        {provide: BackendConfigurationPnCalendarService, useValue: referenceDataCalendarService()},
        {provide: MatDialog, useValue: {open: jest.fn()}},
        {provide: Router, useValue: router},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    }).compileComponents();

    fixture = TestBed.createComponent(ComplianceReportViewComponent);
    component = fixture.componentInstance;
    state = TestBed.inject(ComplianceReportStateService);
    fixture.detectChanges();
  });

  it('renders ONE section with TWO tables, in server order, each with only its own columns', () => {
    (component as any).applyResponse(mixedGroup());
    const [only] = component.sections;

    expect(component.sections.length).toBe(1);
    expect(only.tables.map((t) => [t.key, t.templateLabel])).toEqual([
      ['h8-c511', 'Kontrol'],
      ['h8-c509', 'Tilsyn'],
    ]);
    // Each grid: the six fixed columns, THIS eForm's answers, the actions —
    // one `KOMMENTAR` per grid, never two.
    const answerHeaders = only.tables.map((t) =>
      t.gridColumns.filter((c) => c.answerKey !== undefined).map((c) => c.header),
    );
    expect(answerHeaders).toEqual([
      ['Temperatur', 'KOMMENTAR'],
      ['Udført', 'KOMMENTAR'],
    ]);
    expect(only.tables.map((t) => t.allRows.map((r) => r.complianceId))).toEqual([
      [2, 4],
      [1, 3],
    ]);
  });

  it('shows the headline and caption ONCE, and an eForm sub-heading per table', () => {
    (component as any).applyResponse(mixedGroup());
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;

    const sections = root.querySelectorAll('section.compliance-report__section');
    expect(sections.length).toBe(1);
    const section = sections[0];
    expect(section.getAttribute('data-section-key')).toBe('h8');
    expect(section.querySelectorAll('.compliance-report__heading').length).toBe(1);
    expect(section.querySelector('.compliance-report__heading')!.textContent!.trim()).toBe(
      'Lovpligtig dokumentation',
    );
    expect(section.querySelectorAll('.compliance-report__tag').length).toBe(1);
    expect(section.querySelector('.compliance-report__tag')!.textContent!.trim()).toBe(
      'Miljøtilsyn - Dokumentation',
    );

    const tableEls = Array.from(section.querySelectorAll('[data-table-key]'));
    expect(tableEls.map((el) => el.getAttribute('data-table-key'))).toEqual(['h8-c511', 'h8-c509']);
    expect(
      tableEls.map((el) => el.querySelector('.compliance-report__subheading')!.textContent!.trim()),
    ).toEqual(['Kontrol', 'Tilsyn']);
  });

  it('gives each collapsed table its OWN Vis alle id — unique on the page', () => {
    // Force both tables collapsed, the one state that renders the footer.
    (component as any).applyResponse(mixedGroup());
    for (const table of component.sections[0].tables) {
      table.rows = [];
      table.expanded = false;
    }
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;

    const ids = Array.from(root.querySelectorAll('[id^="complianceReportShowAll-"]')).map((el) => el.id);
    expect(ids).toEqual(['complianceReportShowAll-h8-c511', 'complianceReportShowAll-h8-c509']);
  });

  it('states a missing schema on the ONE table whose eForm lacks it', () => {
    const groups = mixedGroup();
    groups[0].templates[1] = {...groups[0].templates[1], schemaUnavailable: true, columns: []};

    (component as any).applyResponse(groups);
    fixture.detectChanges();
    const tableEls = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('[data-table-key]'),
    );

    expect(tableEls.map((el) => el.querySelectorAll('.compliance-report__notice').length)).toEqual([0, 1]);
  });

  it('still takes checkListId from the CASE, and routes Rediger with it', () => {
    (component as any).applyResponse(mixedGroup());
    const [kontrol, tilsyn] = component.sections[0].tables;

    expect(kontrol.allRows.map((r) => r.checkListId)).toEqual([511, 511]);
    // A case the server sent without a template stays `0` — `canEdit` rejects it.
    expect(tilsyn.allRows.map((r) => r.checkListId)).toEqual([509, 0]);

    component.onEdit(kontrol.allRows[0] as any);
    expect(router.navigate).toHaveBeenCalledWith(
      ['/plugins/backend-configuration-pn/case', 102, 511, 2],
      {queryParams: {reverseRoute: router.url}},
    );

    component.onEdit(tilsyn.allRows[0] as any);
    expect(router.navigate).toHaveBeenLastCalledWith(
      ['/plugins/backend-configuration-pn/case', 101, 509, 1],
      {queryParams: {reverseRoute: router.url}},
    );
  });

  it('gates Rediger on completed AND a real checkListId, per row', () => {
    (component as any).applyResponse(mixedGroup());
    const [kontrol, tilsyn] = component.sections[0].tables;
    const [answered511, notCompleted] = kontrol.allRows;
    const [answered509, noTemplate] = tilsyn.allRows;

    expect(component.canEdit(answered509 as any)).toBe(true);
    expect(component.canEdit(answered511 as any)).toBe(true);
    // A case the server sent without a template cannot be routed anywhere.
    expect(component.canEdit(noTemplate as any)).toBe(false);
    expect(component.canEdit(notCompleted as any)).toBe(false);

    component.onEdit(noTemplate as any);
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('reports the plain sum of cases over EVERY table as the total', () => {
    const groups: ComplianceReportHeadlineGroupModel[] = [
      ...mixedGroup(),
      {
        headlineTagId: 9,
        headlineName: 'Anden overskrift',
        tagsCaption: '',
        templates: [
          {checkListId: 509, checkListName: 'Tilsyn', schemaUnavailable: false, columns: [], cases: [caseModel(5, 509)]},
        ],
      },
    ];

    (component as any).applyResponse(groups);

    expect(state.total).toBe(5);
    expect(component.sections.map((s) => s.headlineLabel)[1]).toBe('Anden overskrift');
    // The same eForm under another headline is its own, distinctly keyed table.
    expect(component.sections[1].tables.map((t) => t.key)).toEqual(['h9-c509']);
  });

  it('shows no "Without report headline" section and does not count its cases (#1301)', () => {
    const groups: ComplianceReportHeadlineGroupModel[] = [
      ...mixedGroup(),
      {
        headlineTagId: null,
        headlineName: null,
        tagsCaption: '',
        templates: [
          {checkListId: 509, checkListName: 'Tilsyn', schemaUnavailable: false, columns: [], cases: [caseModel(5, 509)]},
        ],
      },
    ];

    (component as any).applyResponse(groups);

    expect(component.sections.length).toBe(1);
    expect(component.sections.map((s) => s.key)).toEqual(['h8']);
    expect(state.total).toBe(4);
  });
});

/**
 * The answer cell's field types (#1276): a ticked `CheckBox` is a check icon,
 * an unticked one an empty cell and an unanswered one the en dash — never the
 * literal `checked` / `unchecked` — and a `Date` answer reads `dd.MM.yyyy`
 * like the `Udført dato` column beside it.
 *
 * The first two go through the grid column `buildGridColumns` really emits, so
 * a regression that stops `fieldType` reaching the cell fails here. The last
 * renders the component's own `answerTpl` — the one place an answer is drawn —
 * as a detached embedded view, since mtx-grid (which normally stamps it) is
 * kept out of this TestBed.
 */
describe('ComplianceReportViewComponent — the answer cell', () => {
  let component: ComplianceReportViewComponent;

  const caseModel = (cells: {[key: string]: string}): ComplianceReportCaseModel => ({
    complianceId: 1,
    sdkCaseId: 101,
    propertyId: 5,
    propertyName: 'Ejendom A',
    title: 'Område 1',
    taskDate: '2026-08-11',
    completed: true,
    doneAt: '2026-08-11T09:00:00Z',
    workerNames: ['Anna'],
    checkListId: 509,
    tags: [],
    cells,
    imagesCount: 0,
    images: [],
  });

  /** The answer grid columns for a CheckBox `f1`, a Date `f2` and a Text `f3`. */
  const answerColumns = () =>
    ((component as any).buildGridColumns({
      key: 'h7-c509',
      checkListId: 509,
      templateLabel: 'Tilsyn',
      schemaUnavailable: false,
      columns: [
        {key: 'f1', fieldId: 1, label: 'Udført', fieldType: 'CheckBox'},
        {key: 'f2', fieldId: 2, label: 'Dato', fieldType: 'Date'},
        {key: 'f3', fieldId: 3, label: 'Note', fieldType: 'Text'},
      ],
      cases: [],
    } as ComplianceReportTable) as any[]).filter((c) => c.answerKey !== undefined);

  const rowVm = (cells: {[key: string]: string}) => (component as any).toRowVm(caseModel(cells));

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [ComplianceReportViewComponent],
      imports: [TranslateModule.forRoot()],
      providers: [
        ComplianceReportStateService,
        {provide: BackendConfigurationPnComplianceReportService, useValue: {eformColumns: jest.fn()}},
        {provide: BackendConfigurationPnCompliancesService, useValue: {deleteCompliance: jest.fn()}},
        {provide: BackendConfigurationPnPropertiesService, useValue: referenceDataPropertiesService()},
        {provide: BackendConfigurationPnCalendarService, useValue: referenceDataCalendarService()},
        {provide: MatDialog, useValue: {open: jest.fn()}},
        {provide: Router, useValue: {navigate: jest.fn(), url: '/x'}},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    }).compileComponents();

    const fixture = TestBed.createComponent(ComplianceReportViewComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('ticks a checked CheckBox and gives it no text; unchecked is empty, unanswered the en dash', () => {
    const [checkBox] = answerColumns();
    const checked = rowVm({f1: 'checked'});
    const unchecked = rowVm({f1: 'unchecked'});
    const unanswered = rowVm({});

    expect(component.answerIsChecked(checked, checkBox)).toBe(true);
    expect(component.answerText(checked, checkBox)).toBe('');

    expect(component.answerIsChecked(unchecked, checkBox)).toBe(false);
    expect(component.answerText(unchecked, checkBox)).toBe('');
    expect(component.answerIsChecked(unanswered, checkBox)).toBe(false);
    expect(component.answerText(unanswered, checkBox)).toBe(COMPLIANCE_EMPTY_CELL);

    // A token that is neither state draws nothing and reads as unanswered.
    const unknown = rowVm({f1: 'maybe'});
    expect(component.answerIsChecked(unknown, checkBox)).toBe(false);
    expect(component.answerText(unknown, checkBox)).toBe(COMPLIANCE_EMPTY_CELL);
  });

  it('formats a Date answer as dd.MM.yyyy and leaves every other type alone', () => {
    const [checkBox, date, text] = answerColumns();
    const row = rowVm({f2: '2025-12-01', f3: 'checked'});

    expect(component.answerText(row, date)).toBe('01.12.2025');
    // A Text answer reading `checked` is text, and draws no tick.
    expect(component.answerText(row, text)).toBe('checked');
    expect(component.answerIsChecked(row, text)).toBe(false);
    expect(component.answerIsChecked(rowVm({f1: 'checked'}), date)).toBe(false);
    expect(checkBox.answerFieldType).toBe('CheckBox');
  });

  it('draws the check icon — with an accessible name — for checked, and no token text at all', () => {
    const [checkBox, date] = answerColumns();
    /** The answer cell for one row and column, as mtx-grid would stamp it. */
    const render = (row: unknown, col: unknown): HTMLElement => {
      const view = component.answerTpl.createEmbeddedView({$implicit: row, colDef: col});
      view.detectChanges();
      const host = document.createElement('div');
      view.rootNodes.forEach((node: Node) => host.appendChild(node));
      return host;
    };

    const checkedCell = render(rowVm({f1: 'checked'}), checkBox);
    const tick = checkedCell.querySelector('[role="img"]');
    expect(tick).not.toBeNull();
    // `Yes` is the i18n key (`Ja`); TranslateModule without a loader echoes it.
    expect(tick!.getAttribute('aria-label')).toBe('Yes');
    expect(tick!.querySelector('mat-icon')!.textContent!.trim()).toBe('check');
    // Centred like the Billeder icon, at the same shared icon metrics.
    expect(tick!.classList).toContain('compliance-report__tick');
    expect(tick!.querySelector('mat-icon')!.classList).toContain('compliance-report__cell-icon');
    expect(checkedCell.textContent).not.toContain('checked');

    const uncheckedCell = render(rowVm({f1: 'unchecked'}), checkBox);
    expect(uncheckedCell.querySelector('mat-icon')).toBeNull();
    expect(uncheckedCell.textContent!.trim()).toBe('');

    const dateCell = render(rowVm({f2: '2025-12-01'}), date);
    expect(dateCell.textContent!.trim()).toBe('01.12.2025');
  });
});

/**
 * The view-mode guard at the head of the fetch pipeline (#1185, PR #1202):
 * `rxFilter(() => this.state.mode === 'report')`, the Rapport half of the same
 * guard the Oversigt spec pins next door.
 *
 * `fetchRequested$` is ONE stream shared by all three children, and the shell
 * swaps them with an `ngSwitch` — on the change-detection pass AFTER the click
 * handler that called `setMode()`. `resetToOverview()` (pressing `Oversigt`
 * from Rapport) therefore emits its trigger while THIS child is still
 * subscribed, and without the guard this child issues an `eform-columns` query
 * for a screen the user has just left.
 *
 * Deleting the guard is SILENT: `takeUntil` cancels the stray request on
 * destroy and the only trace is a briefly-set `loading` flag. Mis-writing it is
 * loud — Rapport then never fetches at all. The silent direction is what the
 * first and last tests below pin; the second only proves the guard is not stuck
 * closed.
 *
 * `fixture.detectChanges()` is what runs `ngOnInit` — hence the mode is set
 * BEFORE it — and the same `NO_ERRORS_SCHEMA` TestBed as the suites above keeps
 * mtx-grid and Material out of it.
 */
describe('ComplianceReportViewComponent — the view-mode guard', () => {
  let fixture: ComponentFixture<ComplianceReportViewComponent>;
  let state: ComplianceReportStateService;
  let eformColumns: jest.Mock;

  beforeEach(async () => {
    eformColumns = jest.fn().mockReturnValue(of({success: true, model: []}));

    await TestBed.configureTestingModule({
      declarations: [ComplianceReportViewComponent],
      imports: [TranslateModule.forRoot()],
      providers: [
        ComplianceReportStateService,
        {provide: BackendConfigurationPnComplianceReportService, useValue: {eformColumns}},
        {provide: BackendConfigurationPnCompliancesService, useValue: {deleteCompliance: jest.fn()}},
        {provide: BackendConfigurationPnPropertiesService, useValue: referenceDataPropertiesService()},
        {provide: BackendConfigurationPnCalendarService, useValue: referenceDataCalendarService()},
        {provide: MatDialog, useValue: {open: jest.fn()}},
        {provide: Router, useValue: {navigate: jest.fn(), url: '/x'}},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    }).compileComponents();

    fixture = TestBed.createComponent(ComplianceReportViewComponent);
    // The SAME instance the component injects — it is provided on the TestBed,
    // not `providedIn: 'root'`.
    state = TestBed.inject(ComplianceReportStateService);
  });

  it('drops a trigger emitted while another view owns the mode', () => {
    state.setMode('details');
    fixture.detectChanges();

    // `requestFetch()` un-hides the report and emits, so the trigger really
    // does reach the subscription — it is the guard, not the `reportVisible`
    // gate on `fetchRequested$`, that stops it here.
    state.requestFetch();

    expect(eformColumns).not.toHaveBeenCalled();
  });

  it('is not stuck closed: it queries as soon as Rapport owns the mode', () => {
    state.setMode('details');
    fixture.detectChanges();
    state.requestFetch();

    state.setMode('report');
    state.requestFetch();

    // Once, not twice: the trigger dropped above must not be replayed.
    expect(eformColumns).toHaveBeenCalledTimes(1);
  });

  it('drops the reset trigger `Oversigt` fires while it is still mounted', () => {
    // The scenario the guard was written for, end to end.
    state.setMode('report');
    fixture.detectChanges();
    state.requestFetch();
    expect(eformColumns).toHaveBeenCalledTimes(1);

    // `resetToOverview()` switches the mode and fetches in ONE synchronous
    // gesture; this child is not torn down until the next change-detection
    // pass, so it sees the emission with `mode` already 'overview'.
    state.resetToOverview();

    expect(eformColumns).toHaveBeenCalledTimes(1);
  });

  it('never touches the shell loading flag for a trigger it drops', () => {
    // A request that never settles, so the `tap` that sets `loading` true is
    // observable. With a synchronously completing stub the subscribe callback
    // would clear it again in the same tick and this could not fail.
    eformColumns.mockReturnValue(new Subject<any>());
    state.setMode('details');
    fixture.detectChanges();

    state.requestFetch();

    // `loading` is the SHELL's flag and it gates `Opdater periode`.
    expect(state.loading).toBe(false);
  });
});

/**
 * #1290 — "Slet log" must return the user to the NEXT log: the key of the row
 * that followed the deleted one is taken from the page BEFORE the refresh
 * replaces it, handed to the state service, and applied — scroll + a ~3 s
 * `row-highlight-flash` — once the refreshed response has rendered. The same
 * mechanism is what #1291 reuses for the edited row.
 */
describe('ComplianceReportViewComponent — delete returns to the next row', () => {
  let fixture: ComponentFixture<ComplianceReportViewComponent>;
  let component: ComplianceReportViewComponent;
  let state: ComplianceReportStateService;
  let eformColumns: jest.Mock;
  let deleteCompliance: jest.Mock;
  let afterClosed$: Subject<unknown>;

  const caseModel = (complianceId: number, sdkCaseId = 100 + complianceId): ComplianceReportCaseModel => ({
    complianceId,
    sdkCaseId,
    propertyId: 5,
    propertyName: 'Ejendom A',
    title: 'Område 1',
    taskDate: '2026-08-11',
    completed: sdkCaseId > 0,
    doneAt: null,
    workerNames: [],
    checkListId: 509,
    tags: [],
    cells: {},
    imagesCount: 0,
    images: [],
  });

  /** One headline, one table per inner array, in that order. */
  const response = (...tables: ComplianceReportCaseModel[][]): ComplianceReportHeadlineGroupModel[] => [
    {
      headlineTagId: 1,
      headlineName: 'Overskrift',
      tagsCaption: '',
      templates: tables.map((cases, t) => ({
        checkListId: 509 + t,
        checkListName: `eForm ${t}`,
        schemaUnavailable: false,
        columns: [],
        cases,
      })),
    },
  ];

  const rowById = (complianceId: number) =>
    component.sections
      .flatMap((s) => s.tables)
      .flatMap((t) => t.allRows)
      .find((r) => r.complianceId === complianceId)!;

  const isHighlighted = (complianceId: number) =>
    component.rowClassFormatter['row-highlight-flash'](rowById(complianceId), 0);

  /** Render `before`, delete `deletedId`, answer the refresh with `after`. */
  const deleteAndRefresh = (
    before: ComplianceReportHeadlineGroupModel[],
    deletedId: number,
    after: ComplianceReportHeadlineGroupModel[],
    deleteSucceeds = true,
  ) => {
    eformColumns.mockReturnValue(of({success: true, model: before}));
    state.requestFetch();
    eformColumns.mockReturnValue(of({success: true, model: after}));
    deleteCompliance.mockReturnValue(of({success: deleteSucceeds}));

    component.openDeleteConfirm(rowById(deletedId));
    component.confirmDelete();
  };

  beforeEach(async () => {
    jest.useFakeTimers();
    eformColumns = jest.fn().mockReturnValue(of({success: true, model: []}));
    deleteCompliance = jest.fn();
    afterClosed$ = new Subject<unknown>();

    await TestBed.configureTestingModule({
      declarations: [ComplianceReportViewComponent],
      imports: [TranslateModule.forRoot()],
      providers: [
        ComplianceReportStateService,
        {provide: BackendConfigurationPnComplianceReportService, useValue: {eformColumns}},
        {provide: BackendConfigurationPnCompliancesService, useValue: {deleteCompliance}},
        {provide: BackendConfigurationPnPropertiesService, useValue: referenceDataPropertiesService()},
        {provide: BackendConfigurationPnCalendarService, useValue: referenceDataCalendarService()},
        {
          provide: MatDialog,
          useValue: {open: jest.fn(() => ({afterClosed: () => afterClosed$, close: jest.fn()}))},
        },
        {provide: Router, useValue: {navigate: jest.fn(), url: '/x'}},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    }).compileComponents();

    fixture = TestBed.createComponent(ComplianceReportViewComponent);
    component = fixture.componentInstance;
    state = TestBed.inject(ComplianceReportStateService);
    state.setMode('report');
    fixture.detectChanges();
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  it('deletes by the compliance id of the row the dialog was opened from', () => {
    deleteAndRefresh(response([caseModel(1), caseModel(2)]), 1, response([caseModel(2)]));

    expect(deleteCompliance).toHaveBeenCalledWith(1);
  });

  it('highlights the row that FOLLOWED the deleted one after the refresh', () => {
    deleteAndRefresh(
      response([caseModel(1), caseModel(2), caseModel(3)]),
      2,
      response([caseModel(1), caseModel(3)]),
    );

    expect(component.highlightedRowKey).toBe('case:103');
    expect(isHighlighted(3)).toBe(true);
    expect(isHighlighted(1)).toBe(false);
  });

  it('crosses into the next table when the deleted row ended its table', () => {
    deleteAndRefresh(
      response([caseModel(1), caseModel(2)], [caseModel(3)]),
      2,
      response([caseModel(1)], [caseModel(3)]),
    );

    expect(component.highlightedRowKey).toBe('case:103');
  });

  it('falls back to the row BEFORE when the last row was deleted', () => {
    deleteAndRefresh(response([caseModel(1), caseModel(2)]), 2, response([caseModel(1)]));

    expect(component.highlightedRowKey).toBe('case:101');
  });

  it('keys a row without an SDK case by its compliance id', () => {
    deleteAndRefresh(
      response([caseModel(1), caseModel(2, 0)]),
      1,
      response([caseModel(2, 0)]),
    );

    expect(component.highlightedRowKey).toBe('compliance:2');
  });

  it('highlights nothing when the deleted row was the only one', () => {
    deleteAndRefresh(response([caseModel(1)]), 1, response());

    expect(component.highlightedRowKey).toBeNull();
  });

  it('drops the highlight after ~3 s', () => {
    deleteAndRefresh(response([caseModel(1), caseModel(2)]), 1, response([caseModel(2)]));
    expect(component.highlightedRowKey).toBe('case:102');

    jest.advanceTimersByTime(2999);
    expect(component.highlightedRowKey).toBe('case:102');

    jest.advanceTimersByTime(1);
    expect(component.highlightedRowKey).toBeNull();
  });

  /**
   * CI regression (#1290, e2e s/compliance-report-view.spec.ts): mtx-grid applies
   * the formatter through `row | rowClass: index: dataIndex: rowClassFormatter`,
   * a PURE pipe in an OnPush component. When the highlight drops, the rows and
   * indexes are unchanged, so the pipe re-runs only if the BOUND formatter is a
   * new reference — a formatter that merely read `highlightedRowKey` left
   * `row-highlight-flash` on the row in a real browser forever.
   */
  it('hands mtx-grid a NEW formatter when the highlight drops (its rowClass pipe is pure)', () => {
    const boundFormatter = () => {
      fixture.detectChanges();
      const grid = fixture.nativeElement.querySelector('mtx-grid') as
        | (HTMLElement & {rowClassFormatter?: Record<string, (row: unknown, index: number) => boolean>})
        | null;
      expect(grid).not.toBeNull();
      return grid!.rowClassFormatter!;
    };

    deleteAndRefresh(response([caseModel(1), caseModel(2)]), 1, response([caseModel(2)]));
    const landed = boundFormatter();
    expect(landed['row-highlight-flash'](rowById(2), 0)).toBe(true);

    jest.advanceTimersByTime(3000);
    const dropped = boundFormatter();

    // Same reference = the pure pipe returns its memoised "highlighted" string.
    expect(dropped).not.toBe(landed);
    expect(dropped['row-highlight-flash'](rowById(2), 0)).toBe(false);
  });

  it('scrolls the highlighted row into view one frame after the render', () => {
    const tr = document.createElement('tr');
    tr.className = 'row-highlight-flash';
    const scrollIntoView = jest.fn();
    (tr as any).scrollIntoView = scrollIntoView;
    fixture.nativeElement.appendChild(tr);

    deleteAndRefresh(response([caseModel(1), caseModel(2)]), 1, response([caseModel(2)]));
    expect(scrollIntoView).not.toHaveBeenCalled();

    jest.advanceTimersByTime(0);
    expect(scrollIntoView).toHaveBeenCalledWith({behavior: 'smooth', block: 'center'});
  });

  it('expands a table the row budget left collapsed so the next row is in the DOM', () => {
    // The refreshed page: five full tables spend the whole 500-row page budget,
    // so the sixth — the one holding the row to land on — renders 0 rows.
    let id = 1000;
    const big = () => Array.from({length: 100}, () => caseModel(++id));
    const after = response(big(), big(), big(), big(), big(), [caseModel(2), caseModel(3)]);

    deleteAndRefresh(response([caseModel(1), caseModel(2)]), 1, after);

    const sixth = component.sections[0].tables[5];
    expect(component.highlightedRowKey).toBe('case:102');
    expect(sixth.expanded).toBe(true);
    expect(sixth.rows.map((r) => r.complianceId)).toEqual([2, 3]);
  });

  it('a failed delete refreshes nothing and highlights nothing', () => {
    deleteAndRefresh(response([caseModel(1), caseModel(2)]), 1, response([caseModel(2)]), false);

    expect(eformColumns).toHaveBeenCalledTimes(1);
    expect(component.highlightedRowKey).toBeNull();
    expect(state.takePendingRowHighlight()).toBeNull();
  });

  it('consumes the highlight even when the refresh fails, so it cannot leak into a later fetch', () => {
    eformColumns.mockReturnValue(of({success: true, model: response([caseModel(1), caseModel(2)])}));
    state.requestFetch();
    deleteCompliance.mockReturnValue(of({success: true}));
    eformColumns.mockReturnValue(of({success: false}));

    component.openDeleteConfirm(rowById(1));
    component.confirmDelete();

    expect(component.highlightedRowKey).toBeNull();
    expect(state.takePendingRowHighlight()).toBeNull();
  });

  it('the confirm text warns that answers and photos go too — for a completed row only', () => {
    eformColumns.mockReturnValue(of({success: true, model: response([caseModel(1), caseModel(2, 0)])}));
    state.requestFetch();

    component.openDeleteConfirm(rowById(1));
    expect(component.deleteTargetCompleted).toBe(true);
    component.cancelDelete();

    component.openDeleteConfirm(rowById(2));
    expect(component.deleteTargetCompleted).toBe(false);
  });
});

/**
 * #1291 — `Rediger` → `Gem` must return to the SAME Rapport result with the
 * edited row landed on, not to the un-fetched placeholder. The round trip is a
 * full router navigation to the shared case page, so THIS view is destroyed on
 * the way out and a NEW one is created on the way back; only the state service
 * (on the cached lazy module) survives. Each test therefore renders one
 * instance, edits from it, destroys it, runs the page's `enterPage()` the way
 * `ComplianceReportPageComponent.ngOnInit` does — with the case page's
 * `?highlightId=` on a save, without it on a plain Back — and then mounts a
 * second instance, exactly the order the real ngSwitch produces.
 */
describe('ComplianceReportViewComponent — edit returns to the edited row', () => {
  let state: ComplianceReportStateService;
  let eformColumns: jest.Mock;
  let router: {navigate: jest.Mock; url: string};

  const caseModel = (complianceId: number): ComplianceReportCaseModel => ({
    complianceId,
    sdkCaseId: 100 + complianceId,
    propertyId: 5,
    propertyName: 'Ejendom A',
    title: `Område ${complianceId}`,
    taskDate: '2026-08-11',
    completed: true,
    doneAt: null,
    workerNames: [],
    checkListId: 509,
    tags: [],
    cells: {},
    imagesCount: 0,
    images: [],
  });

  const response = (...tables: ComplianceReportCaseModel[][]): ComplianceReportHeadlineGroupModel[] => [
    {
      headlineTagId: 1,
      headlineName: 'Overskrift',
      tagsCaption: '',
      templates: tables.map((cases, t) => ({
        checkListId: 509 + t,
        checkListName: `eForm ${t}`,
        schemaUnavailable: false,
        columns: [],
        cases,
      })),
    },
  ];

  const mount = (): ComponentFixture<ComplianceReportViewComponent> => {
    const fixture = TestBed.createComponent(ComplianceReportViewComponent);
    fixture.detectChanges();
    return fixture;
  };

  const rowOf = (component: ComplianceReportViewComponent, complianceId: number) =>
    component.sections
      .flatMap((s) => s.tables)
      .flatMap((t) => t.allRows)
      .find((r) => r.complianceId === complianceId)!;

  /**
   * Visit 1 in Rapport: fetch, render, optionally move to `page`, press
   * Rediger on `complianceId`, leave.
   */
  const editAndLeave = (model: ComplianceReportHeadlineGroupModel[], complianceId: number, page = 0) => {
    state.setMode('report');
    eformColumns.mockReturnValue(of({success: true, model}));
    state.requestFetch();
    const first = mount();
    if (page > 0) {
      state.setPage(page);
    }
    first.componentInstance.onEdit(rowOf(first.componentInstance, complianceId));
    first.destroy();
  };

  beforeEach(async () => {
    jest.useFakeTimers();
    eformColumns = jest.fn().mockReturnValue(of({success: true, model: []}));
    router = {navigate: jest.fn().mockResolvedValue(true), url: '/plugins/backend-configuration-pn/compliance-report'};

    await TestBed.configureTestingModule({
      declarations: [ComplianceReportViewComponent],
      imports: [TranslateModule.forRoot()],
      providers: [
        ComplianceReportStateService,
        {provide: BackendConfigurationPnComplianceReportService, useValue: {eformColumns}},
        {provide: BackendConfigurationPnCompliancesService, useValue: {deleteCompliance: jest.fn()}},
        {provide: BackendConfigurationPnPropertiesService, useValue: referenceDataPropertiesService()},
        {provide: BackendConfigurationPnCalendarService, useValue: referenceDataCalendarService()},
        {provide: MatDialog, useValue: {open: jest.fn()}},
        {provide: Router, useValue: router},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    }).compileComponents();

    state = TestBed.inject(ComplianceReportStateService);
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  it('still routes Rediger to the shared case page with the unchanged reverseRoute contract', () => {
    editAndLeave(response([caseModel(1), caseModel(2)]), 2);

    expect(router.navigate).toHaveBeenCalledWith(
      ['/plugins/backend-configuration-pn/case', 102, 509, 2],
      {queryParams: {reverseRoute: '/plugins/backend-configuration-pn/compliance-report'}},
    );
  });

  it('after Gem: re-fetches on return and lands on the EDITED row', () => {
    editAndLeave(response([caseModel(1), caseModel(2), caseModel(3)]), 2);
    eformColumns.mockClear();

    state.enterPage(102);
    const second = mount().componentInstance;

    expect(state.reportVisible).toBe(true);
    expect(eformColumns).toHaveBeenCalledTimes(1);
    expect(second.sections[0].tables[0].allRows.map((r) => r.complianceId)).toEqual([1, 2, 3]);
    expect(second.highlightedRowKey).toBe('case:102');
    expect(second.rowClassFormatter['row-highlight-flash'](rowOf(second, 2), 0)).toBe(true);
    expect(second.rowClassFormatter['row-highlight-flash'](rowOf(second, 1), 0)).toBe(false);
  });

  it('after Gem: keeps the page the user left from', () => {
    editAndLeave(response([caseModel(1), caseModel(2)]), 1, 2);
    eformColumns.mockClear();

    state.enterPage(101);
    mount();

    expect(eformColumns).toHaveBeenCalledTimes(1);
    expect(eformColumns.mock.calls[0][0].pageIndex).toBe(2);
    expect(state.page).toBe(2);
  });

  it('after Gem: expands the table the row budget collapsed, scrolls, and drops the highlight after ~3 s', () => {
    let id = 1000;
    const big = () => Array.from({length: 100}, () => caseModel(++id));
    const model = response(big(), big(), big(), big(), big(), [caseModel(2), caseModel(3)]);
    editAndLeave(model, 3);

    state.enterPage(103);
    const fixture = mount();
    const second = fixture.componentInstance;
    const tr = document.createElement('tr');
    tr.className = 'row-highlight-flash';
    const scrollIntoView = jest.fn();
    (tr as any).scrollIntoView = scrollIntoView;
    fixture.nativeElement.appendChild(tr);

    const sixth = second.sections[0].tables[5];
    expect(sixth.expanded).toBe(true);
    expect(second.highlightedRowKey).toBe('case:103');

    jest.advanceTimersByTime(0);
    expect(scrollIntoView).toHaveBeenCalledWith({behavior: 'smooth', block: 'center'});

    jest.advanceTimersByTime(3000);
    expect(second.highlightedRowKey).toBeNull();
  });

  it('Back WITHOUT saving (no highlightId) is the status quo: placeholder, no fetch, no highlight', () => {
    editAndLeave(response([caseModel(1), caseModel(2)]), 2);
    eformColumns.mockClear();

    state.enterPage();

    expect(state.reportVisible).toBe(false);
    // The page does not mount the view while the report is hidden; even a view
    // that did mount could not be served the buffered trigger.
    const second = mount().componentInstance;
    expect(eformColumns).not.toHaveBeenCalled();
    expect(second.highlightedRowKey).toBeNull();
  });

  it('is one-shot: a later entry with the same highlightId does not fetch again', () => {
    editAndLeave(response([caseModel(1), caseModel(2)]), 2);
    state.enterPage(102);
    mount().destroy();
    eformColumns.mockClear();

    state.enterPage(102);

    expect(state.reportVisible).toBe(false);
    mount();
    expect(eformColumns).not.toHaveBeenCalled();
  });
});

/**
 * #1300 — Rapport's `Slet` is disabled (with a tooltip) for an UNCOMPLETED task
 * dated after today (Copenhagen date). A COMPLETED future row — completed early
 * from the calendar — stays deletable, and so does every past/today row.
 * Clock pinned at 2026-09-18 12:00 in Copenhagen.
 */
describe('ComplianceReportViewComponent — future tasks cannot be deleted (#1300)', () => {
  let fixture: ComponentFixture<ComplianceReportViewComponent>;
  let component: ComplianceReportViewComponent;
  let state: ComplianceReportStateService;
  let eformColumns: jest.Mock;
  let dialogOpen: jest.Mock;

  const caseModel = (complianceId: number, taskDate: string, completed: boolean): ComplianceReportCaseModel => ({
    complianceId,
    sdkCaseId: completed ? 100 + complianceId : 0,
    propertyId: 5,
    propertyName: 'Ejendom A',
    title: 'Område 1',
    taskDate,
    completed,
    doneAt: null,
    workerNames: [],
    checkListId: 509,
    tags: [],
    cells: {},
    imagesCount: 0,
    images: [],
  });

  const response = (cases: ComplianceReportCaseModel[]): ComplianceReportHeadlineGroupModel[] => [
    {
      headlineTagId: 1,
      headlineName: 'Overskrift',
      tagsCaption: '',
      templates: [
        {checkListId: 509, checkListName: 'eForm', schemaUnavailable: false, columns: [], cases},
      ],
    },
  ];

  const rowById = (complianceId: number) =>
    component.sections
      .flatMap((s) => s.tables)
      .flatMap((t) => t.allRows)
      .find((r) => r.complianceId === complianceId)!;

  beforeEach(async () => {
    jest.useFakeTimers();
    jest.setSystemTime(new Date('2026-09-18T10:00:00Z'));
    eformColumns = jest.fn().mockReturnValue(of({success: true, model: []}));
    dialogOpen = jest.fn(() => ({afterClosed: () => new Subject<unknown>(), close: jest.fn()}));

    await TestBed.configureTestingModule({
      declarations: [ComplianceReportViewComponent],
      imports: [TranslateModule.forRoot()],
      providers: [
        ComplianceReportStateService,
        {provide: BackendConfigurationPnComplianceReportService, useValue: {eformColumns}},
        {provide: BackendConfigurationPnCompliancesService, useValue: {deleteCompliance: jest.fn()}},
        {provide: BackendConfigurationPnPropertiesService, useValue: referenceDataPropertiesService()},
        {provide: BackendConfigurationPnCalendarService, useValue: referenceDataCalendarService()},
        {provide: MatDialog, useValue: {open: dialogOpen}},
        {provide: Router, useValue: {navigate: jest.fn(), url: '/x'}},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    }).compileComponents();

    fixture = TestBed.createComponent(ComplianceReportViewComponent);
    component = fixture.componentInstance;
    state = TestBed.inject(ComplianceReportStateService);
    state.setMode('report');
    fixture.detectChanges();

    eformColumns.mockReturnValue(
      of({
        success: true,
        model: response([
          caseModel(1, '2026-09-19', false), // uncompleted, tomorrow
          caseModel(2, '2026-09-18', false), // uncompleted, today
          caseModel(3, '2026-09-17', false), // uncompleted, yesterday
          caseModel(4, '2026-09-25', true), // completed early, future
          caseModel(5, '2026-09-10', true), // completed, past
        ]),
      }),
    );
    state.requestFetch();
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  it.each([
    [1, false, 'uncompleted tomorrow'],
    [2, true, 'uncompleted today'],
    [3, true, 'uncompleted yesterday'],
    [4, true, 'completed future (early from the calendar)'],
    [5, true, 'completed past'],
  ])('row %p → deletable %p (%s)', (id, expected) => {
    expect(component.canDelete(rowById(id as number))).toBe(expected);
  });

  it('does not open the confirm dialog for an uncompleted future row', () => {
    component.openDeleteConfirm(rowById(1));
    expect(dialogOpen).not.toHaveBeenCalled();
  });

  it('opens the confirm dialog for today\'s row', () => {
    component.openDeleteConfirm(rowById(2));
    expect(dialogOpen).toHaveBeenCalledTimes(1);
  });
});

/**
 * #1329: the Rapport meta line names the calendar and the employees.
 *
 * The calendar used to read `#<id>` in the usual flow — property picked, then
 * Rapport, then a calendar — because the boards were fetched only at mount and
 * only when a calendar was ALREADY set, and picking a property clears it. The
 * reference data now follows `filters$`, like the filter bar's.
 */
describe('ComplianceReportViewComponent — the meta line names the calendar and the employees', () => {
  let state: ComplianceReportStateService;
  let component: ComplianceReportViewComponent;
  let getBoards: jest.Mock;
  let getDeviceUsersFiltered: jest.Mock;

  const boardsFor = (propertyId: number) =>
    of({success: true, model: [{id: propertyId * 10, name: `Calendar ${propertyId}`}]});
  const workersFor = (propertyId: number | null) =>
    of({
      success: true,
      model: [
        {siteId: 1, fullName: 'Worker A', userFirstName: 'Worker', userLastName: 'A', siteName: 'a'},
        {siteId: 2, fullName: '', userFirstName: 'Worker', userLastName: `B${propertyId ?? ''}`, siteName: 'b'},
      ],
    });

  beforeEach(async () => {
    getBoards = jest.fn((propertyId: number) => boardsFor(propertyId));
    getDeviceUsersFiltered = jest.fn((model: {propertyIds: number[]}) => workersFor(model.propertyIds[0] ?? null));

    await TestBed.configureTestingModule({
      declarations: [ComplianceReportViewComponent],
      imports: [TranslateModule.forRoot()],
      providers: [
        ComplianceReportStateService,
        {provide: BackendConfigurationPnComplianceReportService, useValue: {eformColumns: jest.fn()}},
        {provide: BackendConfigurationPnCompliancesService, useValue: {deleteCompliance: jest.fn()}},
        {
          provide: BackendConfigurationPnPropertiesService,
          useValue: {
            getAllPropertiesDictionary: jest.fn().mockReturnValue(of({success: true, model: [{id: 5, name: 'Property A'}]})),
            getDeviceUsersFiltered,
          },
        },
        {provide: BackendConfigurationPnCalendarService, useValue: {getBoards}},
        {provide: MatDialog, useValue: {open: jest.fn()}},
        {provide: Router, useValue: {navigate: jest.fn(), url: '/x'}},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    }).compileComponents();

    state = TestBed.inject(ComplianceReportStateService);
  });

  const mount = () => {
    const fixture = TestBed.createComponent(ComplianceReportViewComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    return fixture;
  };

  it('names a calendar picked AFTER mount, not #<id>', () => {
    state.setFilterSilently({propertyId: 5, boardIds: []});
    mount();

    state.setFilterSilently({boardIds: [50]});

    expect(component.boardLabel).toBe('Calendar 5');
    expect(component.boardLabel).not.toContain('#');
    expect(component.propertyLabel).toBe('Property A');
  });

  it('loads the properties dictionary even when no property is set at mount', () => {
    mount();

    state.setFilterSilently({propertyId: 5});

    expect(component.propertyLabel).toBe('Property A');
  });

  it('reloads the calendars when the property changes, and only then', () => {
    state.setFilterSilently({propertyId: 5});
    mount();
    expect(getBoards).toHaveBeenCalledTimes(1);

    state.setFilterSilently({propertyId: 6, boardIds: [60]});
    expect(getBoards).toHaveBeenCalledTimes(2);
    expect(getBoards).toHaveBeenLastCalledWith(6);
    expect(component.boardLabel).toBe('Calendar 6');

    // Another filter changing leaves the property-scoped lists alone.
    state.setFilterSilently({tagIds: [1]});
    expect(getBoards).toHaveBeenCalledTimes(2);
    expect(getDeviceUsersFiltered).toHaveBeenCalledTimes(2);
  });

  it('asks for no calendars without a property', () => {
    mount();

    expect(getBoards).not.toHaveBeenCalled();
    expect(component.boardLabel).toBe('All');
  });

  it('reads Alle on the Medarbejdere line when no employee is selected', () => {
    mount();

    expect(component.employeeLabel).toBe('All');
  });

  it('names the selected employees, with the filter bar\'s name fallback', () => {
    state.setFilterSilently({propertyId: 5});
    mount();

    state.setFilterSilently({siteIds: [2, 1]});

    expect(getDeviceUsersFiltered).toHaveBeenLastCalledWith(expect.objectContaining({propertyIds: [5]}));
    expect(component.employeeLabel).toBe('Worker B5, Worker A');
  });

  it('renders the employee line under the filter line once the report has fetched', () => {
    const fixture = mount();
    state.setFilterSilently({siteIds: [1]});
    component.hasFetched = true;
    fixture.detectChanges();

    const meta: HTMLElement = fixture.nativeElement.querySelector('#complianceReportMeta');
    const lines = meta.querySelectorAll('.compliance-report__meta-line');
    expect(lines.length).toBe(2);
    expect(lines[1].id).toBe('complianceReportMetaEmployees');
    expect(lines[1].textContent).toContain('Employees:');
    expect(lines[1].textContent).toContain('Worker A');
    expect(lines[0].querySelectorAll('.compliance-report__meta-separator').length).toBeGreaterThanOrEqual(1);
  });
});
