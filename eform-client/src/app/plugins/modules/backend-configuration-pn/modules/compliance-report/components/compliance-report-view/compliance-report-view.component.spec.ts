import {ComponentFixture, TestBed} from '@angular/core/testing';
import {NO_ERRORS_SCHEMA} from '@angular/core';
import {MatDialog} from '@angular/material/dialog';
import {Router} from '@angular/router';
import {TranslateModule} from '@ngx-translate/core';
import {
  ComplianceReportCaseModel,
  ComplianceReportColumnModel,
  ComplianceReportImageModel,
  ComplianceReportTagGroupModel,
} from '../../../../models';
import {
  BackendConfigurationPnCalendarService,
  BackendConfigurationPnComplianceReportService,
  BackendConfigurationPnCompliancesService,
  BackendConfigurationPnPropertiesService,
} from '../../../../services';
import {ComplianceReportSection} from '../../helpers';
import {ComplianceReportStateService} from '../../store';
import {ComplianceReportViewComponent} from './compliance-report-view.component';

/**
 * The three grid rules of the Rapport view, none of which the helper spec can
 * reach: `buildGridColumns` lives on the component because it needs the
 * translate stream and the cell TemplateRefs.
 *
 * Each of them blanks a whole sub-report when it regresses, and none of them
 * fails loudly in a way a reviewer would spot:
 *
 *  1. **the `answer_` prefix** — `field` goes straight into MatTable's
 *     `displayedColumns` and becomes a `mat-column-{field}` class. A bare key
 *     could also collide with one of the six fixed metadata fields;
 *  2. **the duplicate-key dedupe** — two identical `displayedColumns` entries
 *     make MatTable throw `getTableDuplicateColumnNameError`, which takes the
 *     ENTIRE grid down, not the one column;
 *  3. **per-section column-array IDENTITY** — mtx-grid's `_countPinnedPosition`
 *     MUTATES `left`/`right` onto the column objects it is given, so two
 *     sections sharing one array (or one column object) would have the wider
 *     one's pin offsets leak into the narrower one and its frozen block overlap
 *     itself.
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
    cells: {},
    imagesCount: 0,
    images: [],
  });

  const section = (
    columns: ComplianceReportColumnModel[],
    checkListId = 509,
  ): ComplianceReportSection => ({
    key: `t7-c${checkListId}`,
    tagLabel: 'Miljøtilsyn',
    templateLabel: 'Brandtjek',
    checkListId,
    schemaUnavailable: false,
    columns,
    cases: [caseModel(1)],
  });

  /** `buildGridColumns` is private; the guarantees it owns are not. */
  const build = (columns: ComplianceReportColumnModel[]) =>
    (component as any).buildGridColumns(section(columns)) as {
      field: string;
      answerKey?: string;
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
        {provide: BackendConfigurationPnPropertiesService, useValue: {getAllPropertiesDictionary: jest.fn()}},
        {provide: BackendConfigurationPnCalendarService, useValue: {getBoards: jest.fn()}},
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

  it('gives every section its OWN column array and its OWN column objects', () => {
    // Through the real response path, because that is where the sharing bug
    // would be introduced.
    const groups: ComplianceReportTagGroupModel[] = [
      {
        tagId: 7,
        tagName: 'Miljøtilsyn',
        templates: [
          {
            checkListId: 509,
            checkListName: 'Brandtjek',
            mergedCheckListIds: [509],
            columns: [column('f1')],
            schemaUnavailable: false,
            cases: [caseModel(1)],
          },
          {
            checkListId: 511,
            checkListName: 'Eltjek',
            mergedCheckListIds: [511],
            columns: [column('f1')],
            schemaUnavailable: false,
            cases: [caseModel(2)],
          },
        ],
      },
    ];

    (component as any).applyResponse(groups);
    const [first, second] = component.sections;

    expect(component.sections.length).toBe(2);
    expect(first.gridColumns).not.toBe(second.gridColumns);
    // Object identity too, not just the array: mtx-grid writes `left`/`right`
    // onto the COLUMN, so one shared object is enough to leak an offset.
    for (const col of first.gridColumns) {
      expect(second.gridColumns).not.toContain(col);
    }

    // The mutation mtx-grid performs, simulated: it must not be visible from
    // the other section.
    (first.gridColumns[0] as any).left = '999px';
    expect((second.gridColumns[0] as any).left).toBeUndefined();
  });
});

/**
 * The two ceilings on the initial DOM. The per-section cap alone bounds
 * nothing: a section is one (tag × TEMPLATE) pair, so a realistic filter set
 * yields dozens of small sections, none of which reaches the cap, and the
 * server's whole 5000-row allowance lands on the page at once.
 *
 * Both ceilings must leave the section REVEALABLE — heading, true row count and
 * the `Vis alle` control — or a user cannot reach the sub-report they came for.
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
    cells: {},
    imagesCount: 0,
    images: [],
  });

  /** `n` template groups of `rows` cases each, all under one tag. */
  const groups = (n: number, rows: number): ComplianceReportTagGroupModel[] => {
    let complianceId = 0;
    return [
      {
        tagId: 7,
        tagName: 'Miljøtilsyn',
        templates: Array.from({length: n}, (_, i) => ({
          checkListId: 500 + i,
          checkListName: `Skema ${i}`,
          mergedCheckListIds: [500 + i],
          columns: [],
          schemaUnavailable: false,
          cases: Array.from({length: rows}, () => caseModel(++complianceId)),
        })),
      },
    ];
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [ComplianceReportViewComponent],
      imports: [TranslateModule.forRoot()],
      providers: [
        ComplianceReportStateService,
        {provide: BackendConfigurationPnComplianceReportService, useValue: {eformColumns: jest.fn()}},
        {provide: BackendConfigurationPnCompliancesService, useValue: {deleteCompliance: jest.fn()}},
        {provide: BackendConfigurationPnPropertiesService, useValue: {getAllPropertiesDictionary: jest.fn()}},
        {provide: BackendConfigurationPnCalendarService, useValue: {getBoards: jest.fn()}},
        {provide: MatDialog, useValue: {open: jest.fn()}},
        {provide: Router, useValue: {navigate: jest.fn(), url: '/x'}},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    }).compileComponents();

    const fixture = TestBed.createComponent(ComplianceReportViewComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('caps ONE big sub-report at the per-section cap', () => {
    (component as any).applyResponse(groups(1, 250));
    const [only] = component.sections;

    expect(only.rows.length).toBe(100);
    expect(only.allRows.length).toBe(250);
    expect(only.expanded).toBe(false);
  });

  it('bounds the whole page when many small sections each stay under that cap', () => {
    // 40 sections × 20 rows = 800 rows, and no single section ever caps — the
    // shape the per-section cap does not bound at all.
    (component as any).applyResponse(groups(40, 20));

    const revealed = component.sections.reduce((sum, s) => sum + s.rows.length, 0);
    expect(revealed).toBe(500);
    // Nothing is dropped: every section is still on the page, and the count it
    // reports back is the TRUE one (the filter bar's Download gate reads it).
    expect(component.sections.length).toBe(40);
    expect(component.sections.reduce((sum, s) => sum + s.allRows.length, 0)).toBe(800);
  });

  it('leaves a budget-collapsed section revealable, with its true row count', () => {
    (component as any).applyResponse(groups(40, 20));
    const collapsed = component.sections[39];

    expect(collapsed.rows.length).toBe(0);
    expect(collapsed.allRows.length).toBe(20);
    // `expanded` false is what renders the "Viser 0 af 20" footer AND its
    // `Vis alle` button — the same control the per-section cap uses.
    expect(collapsed.expanded).toBe(false);

    component.expandSection(collapsed);

    expect(collapsed.rows.length).toBe(20);
    expect(collapsed.expanded).toBe(true);
  });

  it('spends the budget in server order, so the first sections render whole', () => {
    (component as any).applyResponse(groups(40, 20));

    // 25 sections × 20 = the 500-row budget exactly; the 26th gets nothing.
    expect(component.sections.slice(0, 25).every((s) => s.rows.length === 20)).toBe(true);
    expect(component.sections.slice(0, 25).every((s) => s.expanded)).toBe(true);
    expect(component.sections.slice(25).every((s) => s.rows.length === 0)).toBe(true);
  });

  it('leaves a result that fits under both ceilings fully expanded', () => {
    (component as any).applyResponse(groups(3, 20));

    expect(component.sections.every((s) => s.expanded)).toBe(true);
    expect(component.sections.every((s) => s.rows.length === s.allRows.length)).toBe(true);
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
    cells: {},
    imagesCount,
    images,
  });

  /** `toRowVm` is private; the alignment invariant it owns is not. */
  const rowVm = (caseModel_: ComplianceReportCaseModel) =>
    (component as any).toRowVm(caseModel_, 509) as {
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
        {provide: BackendConfigurationPnPropertiesService, useValue: {getAllPropertiesDictionary: jest.fn()}},
        {provide: BackendConfigurationPnCalendarService, useValue: {getBoards: jest.fn()}},
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
