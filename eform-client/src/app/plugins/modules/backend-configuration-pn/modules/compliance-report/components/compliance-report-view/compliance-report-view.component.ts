import {Component, OnDestroy, OnInit, TemplateRef, ViewChild} from '@angular/core';
import {MatDialog, MatDialogRef} from '@angular/material/dialog';
import {Router} from '@angular/router';
import {TranslateService} from '@ngx-translate/core';
import {MtxGridColumn} from '@ng-matero/extensions/grid';
import {Subject, merge, of} from 'rxjs';
import {catchError, finalize, switchMap, takeUntil, tap} from 'rxjs/operators';
import {CommonDictionaryModel} from 'src/app/common/models';
import {
  CalendarBoardModel,
  ComplianceReportCaseModel,
  ComplianceReportTagGroupModel,
} from '../../../../models';
import {
  BackendConfigurationPnCalendarService,
  BackendConfigurationPnComplianceReportService,
  BackendConfigurationPnCompliancesService,
  BackendConfigurationPnPropertiesService,
} from '../../../../services';
import {
  COMPLIANCE_EMPTY_CELL,
  COMPLIANCE_REPORT_PAGE_ROW_BUDGET,
  COMPLIANCE_REPORT_SECTION_ROW_CAP,
  ComplianceReportSection,
  buildComplianceReportSections,
  complianceAnswerText,
  complianceWorkerNames,
  formatComplianceReportDate,
} from '../../helpers';
import {ComplianceReportStateService} from '../../store';

/**
 * One column of a sub-report's grid. `answerKey` is the ONLY way an answer cell
 * is addressed — `MtxGridColumn.field` is used for the fixed metadata columns
 * and, for the answer columns, is a unique identity mtx-grid requires but that
 * nothing reads.
 */
interface ComplianceReportGridColumn extends MtxGridColumn {
  /** `ComplianceReportColumnModel.key`, present on answer columns only. */
  answerKey?: string;
}

/** A sub-report as the template renders it: the model plus its own grid state. */
interface ComplianceReportRenderedSection extends ComplianceReportSection {
  /**
   * Built ONCE per section. mtx-grid MUTATES its column objects
   * (`_countPinnedPosition` writes `left`/`right` onto them), so two sections
   * must never share an array or the pin offsets of the wider one leak into the
   * narrower one.
   */
  gridColumns: ComplianceReportGridColumn[];
  /** The rows currently in the DOM — the first N until the section is expanded. */
  rows: ComplianceReportRowVm[];
  /** Every row of the sub-report. */
  allRows: ComplianceReportRowVm[];
  expanded: boolean;
}

/**
 * The flat shape mtx-grid renders. Built once per case so that no `formatter`
 * is needed for the metadata columns: mtx-grid pipes a `formatter`'s return
 * value through `[innerHTML]`, which sanitises — and therefore mangles — worker
 * names and answers that legitimately contain `<`, `&` or quotes. Plain fields
 * and cell templates are interpolated instead.
 */
interface ComplianceReportRowVm {
  complianceId: number;
  sdkCaseId: number;
  /** The template this row was answered against — the `Rediger` route needs it. */
  checkListId: number;
  propertyName: string;
  doneBy: string;
  /** `DoneAtUserModifiable ?? DoneAt`. Case metadata (#1160 finding 7). */
  doneAt: string | Date | null;
  /** The task title — the prototype's `Område`. */
  title: string;
  imagesCount: number;
  completed: boolean;
  /** The KEYED answer bag, read only through `complianceAnswerText`. */
  cells: {[key: string]: string};
}

/**
 * The Rapport view of the standalone Compliance page (#1167): per tag group,
 * per eForm template, a sub-report whose columns are that template's answer
 * fields.
 *
 * Its contract with the shell (#1163) is the same as Oversigt's and Detaljer's:
 *
 *  - subscribe to `fetchRequested$`, the ONLY fetch trigger. It replays its last
 *    emission to a late subscriber on purpose, which is what makes a mode switch
 *    (an `ngSwitch` that destroys and recreates this component) actually query;
 *  - read `requestModel` AT FETCH TIME, never cached;
 *  - report `setTotalCount()` back — it is load-bearing here, the filter bar's
 *    `canDownload` gates the Download button on `state.total > 0` — and
 *    `setLoading()` so the shell can disable `Opdater tabel`.
 *
 * THE RULE OF THIS VIEW: a cell is looked up by its column's KEY
 * (`complianceAnswerText`), never by position and never by zipping headers
 * against values. A missing key renders the en dash IN PLACE, so no later column
 * shifts — the #1160-finding-3 desync is not merely fixed here, it is
 * inexpressible. See `compliance-report-sections.spec.ts`.
 *
 * NOT PAGINATED, by design ("Rapport paginerer ikke - hver delrapport vises
 * hel", compliance.js:1820) — the shell already hides the pagination `<nav>`
 * outside Detaljer, so nothing there needed changing. The unbounded-DOM problem
 * that creates is answered by two ceilings instead, both of them reversible by
 * one click on the sub-report the user wants: a per-section cap
 * (`COMPLIANCE_REPORT_SECTION_ROW_CAP`) and, because sections are tag ×
 * template and a page can hold dozens of small ones, a cumulative page budget
 * (`COMPLIANCE_REPORT_PAGE_ROW_BUDGET`).
 */
@Component({
  standalone: false,
  selector: 'app-compliance-report-view',
  templateUrl: './compliance-report-view.component.html',
  styleUrls: ['./compliance-report-view.component.scss'],
})
export class ComplianceReportViewComponent implements OnInit, OnDestroy {
  // `static: true` — all four sit at the root of the template, outside every
  // structural directive, so they resolve before `ngOnInit` runs and a response
  // can never land on an undefined TemplateRef.
  @ViewChild('answerTpl', {static: true}) answerTpl!: TemplateRef<unknown>;
  @ViewChild('imagesTpl', {static: true}) imagesTpl!: TemplateRef<unknown>;
  @ViewChild('actionsTpl', {static: true}) actionsTpl!: TemplateRef<unknown>;
  @ViewChild('deleteConfirmTpl', {static: true}) deleteConfirmTpl!: TemplateRef<unknown>;

  readonly emptyCell = COMPLIANCE_EMPTY_CELL;

  sections: ComplianceReportRenderedSection[] = [];
  hasFetched = false;
  /**
   * A fetch failed while there was NOTHING on screen to keep — the first fetch
   * of this visit. Same reasoning as both siblings': without it that case
   * renders an entirely blank card, because the shell's placeholder, the
   * spinner and the empty-result line are each gated off.
   */
  loadFailed = false;

  private properties: CommonDictionaryModel[] = [];
  private boards: CalendarBoardModel[] = [];
  private destroy$ = new Subject<void>();
  /** Refreshes that are NOT a user gesture: after a delete. */
  private refresh$ = new Subject<void>();
  private deleteDialogRef: MatDialogRef<unknown> | null = null;
  private pendingDeleteId: number | null = null;

  constructor(
    public state: ComplianceReportStateService,
    private complianceReportService: BackendConfigurationPnComplianceReportService,
    private compliancesService: BackendConfigurationPnCompliancesService,
    private propertiesService: BackendConfigurationPnPropertiesService,
    private calendarService: BackendConfigurationPnCalendarService,
    private translate: TranslateService,
    private dialog: MatDialog,
    private router: Router,
  ) {}

  ngOnInit(): void {
    this.loadMetaReferenceData();

    merge(this.state.fetchRequested$, this.refresh$)
      .pipe(
        tap(() => {
          // A re-render detaches the row the confirm dialog was opened from.
          this.closeDeleteDialog();
          // Cleared on every attempt: while the spinner is up the previous
          // failure is no longer the current state of the view.
          this.loadFailed = false;
          this.state.setLoading(true);
        }),
        // switchMap, so a trigger landing while an earlier request is in flight
        // cancels it rather than racing it into the view.
        switchMap(() =>
          this.complianceReportService.eformColumns(this.state.requestModel).pipe(
            // The service already toasts a failed OperationResult; swallow the
            // transport error here so the trigger stream survives it.
            catchError(() => of(null)),
          ),
        ),
        // Runs on unsubscribe too — i.e. when takeUntil completes the stream on
        // destroy — so a request still in flight when the ngSwitch tears this
        // component down cannot leave `loading` stuck true and `Opdater tabel`
        // permanently disabled.
        finalize(() => this.state.setLoading(false)),
        takeUntil(this.destroy$),
      )
      .subscribe((res) => {
        this.state.setLoading(false);
        if (!res || !res.success) {
          // For a RE-fetch, leave the previous rendering standing rather than
          // replacing it with "no tasks match the selected filters", which
          // would blame the filters for a transport or server error.
          this.loadFailed = !this.hasFetched;
          return;
        }
        this.applyResponse(res.model ?? []);
      });
  }

  ngOnDestroy(): void {
    this.closeDeleteDialog();
    // No `setLoading(false)` here on purpose. `loading` is the SHELL's flag: it
    // resets it in `setMode()`, `setFilter()` and `enterPage()`, which covers
    // every transition that unmounts this component, and for the ordinary
    // teardown the `finalize` above already clears it (it sits UPSTREAM of
    // `takeUntil`, so completing the stream here unsubscribes through it and
    // fires the callback). Both siblings omit it for the same reason.
    this.destroy$.next();
    this.destroy$.complete();
  }

  // -------------------------------------------------------------------
  // Response → sections
  // -------------------------------------------------------------------

  private applyResponse(groups: ComplianceReportTagGroupModel[]): void {
    const untagged = this.translate.instant('Without tag');
    // The PAGE budget, spent in server order. Sections are (tag × template)
    // pairs, so the per-section cap on its own bounds nothing: dozens of small
    // sections each stay under it and the whole 5000-row server allowance
    // reaches the DOM. Once this is spent the remaining sections render
    // collapsed — heading, true row count, `Vis alle` — rather than not at all.
    let revealed = 0;
    this.sections = buildComplianceReportSections(groups, untagged).map((section) => {
      const rendered = this.renderSection(section, revealed);
      revealed += rendered.rows.length;
      return rendered;
    });
    this.hasFetched = true;

    // The row count of THIS view. A case carrying three tags is three rows —
    // it belongs in three sub-reports — and #1169's Rapport export duplicates
    // it the same way, so the number on screen and the number in the file
    // agree.
    //
    // The call is load-bearing rather than contract parity:
    // `ComplianceReportFiltersComponent.canDownload` is
    // `!!exportFormat && state.reportVisible && state.total > 0` and drives
    // `[disabled]` on `#complianceDownloadBtn`. Drop it and Download stays dead
    // after a Rapport fetch. The pagination chrome is NOT a reader — the shell
    // hides the whole <nav> outside Detaljer.
    this.state.setTotalCount(
      this.sections.reduce((sum, section) => sum + section.allRows.length, 0),
    );
  }

  /**
   * `revealedBefore` is how many rows the sections ABOVE this one already put in
   * the DOM. A section renders the smaller of its own cap and what is left of
   * the page budget — which is 0 once the budget is spent, and then it renders
   * as a heading with a row count and a `Vis alle` button.
   *
   * `expanded` is derived from what was actually rendered, not from which of the
   * two limits bit, so the reveal control and its "Viser X af Y" line are the
   * same for both reasons and `expandSection` needs no branch.
   */
  private renderSection(
    section: ComplianceReportSection,
    revealedBefore: number,
  ): ComplianceReportRenderedSection {
    const allRows = section.cases.map((c) => this.toRowVm(c, section.checkListId));
    const budgetLeft = Math.max(0, COMPLIANCE_REPORT_PAGE_ROW_BUDGET - revealedBefore);
    const visible = Math.min(allRows.length, COMPLIANCE_REPORT_SECTION_ROW_CAP, budgetLeft);
    return {
      ...section,
      gridColumns: this.buildGridColumns(section),
      allRows,
      rows: allRows.slice(0, visible),
      expanded: visible === allRows.length,
    };
  }

  private toRowVm(
    caseModel: ComplianceReportCaseModel,
    checkListId: number,
  ): ComplianceReportRowVm {
    return {
      complianceId: caseModel.complianceId,
      sdkCaseId: caseModel.sdkCaseId,
      checkListId,
      propertyName: caseModel.propertyName,
      doneBy: complianceWorkerNames(caseModel.workerNames),
      doneAt: caseModel.doneAt,
      title: caseModel.title,
      imagesCount: caseModel.imagesCount ?? 0,
      completed: !!caseModel.completed,
      // Carried through untouched. It is read ONLY through
      // `complianceAnswerText(row, column.answerKey)`.
      cells: caseModel.cells ?? {},
    };
  }

  /**
   * Fixed metadata → the template's answer fields → actions.
   *
   * Order is the PROTOTYPE's (`renderReportTableHead`, compliance.js:1708-1721):
   * `ID, Ejendom, Udført af, Udført dato, Område, Billeder`, i.e. Udført af
   * BEFORE Udført dato — which is not the order `report-table.component.ts:71-86`
   * uses. The prototype is the signed-off design (#1167 §3).
   *
   * Pinning is mtx-grid's `pinned`, NOT the prototype's `applyFrozenColumnOffsets`
   * measure-and-write loop over every `<tr>` (#1167 §5). One constraint comes
   * with it: `MtxGrid._countPinnedPosition` computes each pinned column's offset
   * as the sum of `parseFloat(col.width || '80px')` of the pinned columns before
   * it, so EVERY pinned column must carry an explicit `width` or the offsets are
   * computed against a fictitious 80 px and the frozen block overlaps itself.
   *
   * Six frozen columns is ~730 px of the viewport. That is the prototype's own
   * boundary minus its seventh column, which was an artefact of its fabricated
   * `Note` column; the widths below are the tightest that still fit the content.
   */
  private buildGridColumns(section: ComplianceReportSection): ComplianceReportGridColumn[] {
    const columns: ComplianceReportGridColumn[] = [
      {
        field: 'sdkCaseId',
        header: this.translate.stream('Id'),
        width: '80px',
        pinned: 'left',
        class: 'is-num',
      },
      {
        field: 'propertyName',
        header: this.translate.stream('Property'),
        width: '150px',
        pinned: 'left',
      },
      {
        field: 'doneBy',
        header: this.translate.stream('Completed by'),
        width: '140px',
        pinned: 'left',
      },
      {
        field: 'doneAt',
        // NOT the existing 'Completed date' key: its Danish is `Udført`, and
        // #1169's export renders this same column as `Udført dato`. A user must
        // not see one Danish word on screen and another in the file they
        // download from that screen.
        header: this.translate.stream('Completion date'),
        // `type: 'date'` renders through `_getText`, so a case with no
        // completion timestamp lands on `emptyValuePlaceholder`, i.e. the same
        // en dash every other empty cell uses. `timezone: 'utc'` matches
        // `report-table.component.ts:73`, which renders the same field.
        type: 'date',
        typeParameter: {format: 'dd.MM.y', timezone: 'utc'},
        width: '110px',
        pinned: 'left',
      },
      {
        field: 'title',
        header: this.translate.stream('Area'),
        width: '170px',
        pinned: 'left',
      },
      {
        field: 'imagesCount',
        header: this.translate.stream('Pictures'),
        width: '80px',
        pinned: 'left',
        cellTemplate: this.imagesTpl,
      },
    ];

    // De-duplicated by KEY. MatTable throws `Duplicate column definition name`
    // and renders NOTHING for the whole grid if two entries of
    // `displayedColumns` match, so a projection that ever emitted one field
    // twice would take the entire sub-report down rather than showing one
    // column twice. #1166 derives columns from a template's distinct fields, so
    // this should not fire; it costs one Set and removes a whole failure mode.
    const seenKeys = new Set<string>();
    for (const column of section.columns) {
      if (!column?.key || seenKeys.has(column.key)) {
        continue;
      }
      seenKeys.add(column.key);
      columns.push({
        // mtx-grid requires a unique `field`, and it is what the sticky/pin
        // bookkeeping keys off. It deliberately does NOT resolve the value:
        // the cell template reads `answerKey` out of the KEYED bag instead, so
        // a missing key is a dash in place rather than an empty string (which
        // is what `field: 'cells.' + key` would render — MtxGridCell._getText
        // maps `undefined` to '' and only `null`/''/[] to the placeholder).
        // `answer_` prefixed rather than the bare key: mtx-grid feeds
        // `field` straight into MatTable's `displayedColumns`, which turns it
        // into a `mat-column-{field}` class, and the prefix also guarantees no
        // answer column can ever collide with one of the six fixed fields
        // above. Underscore, not a colon — a colon in a generated class name
        // needs escaping in every selector that would ever touch it.
        field: `answer_${column.key}`,
        answerKey: column.key,
        header: column.label || column.key,
        cellTemplate: this.answerTpl,
      });
    }

    columns.push({
      field: 'actions',
      header: this.translate.stream('Actions'),
      width: '110px',
      pinned: 'right',
      cellTemplate: this.actionsTpl,
    });

    return columns;
  }

  // -------------------------------------------------------------------
  // Cell rendering
  // -------------------------------------------------------------------

  /**
   * KEYED, never positional. Exposed to the template so the lookup that makes
   * this view correct is the one line the template calls.
   */
  answerText(row: ComplianceReportRowVm, column: ComplianceReportGridColumn): string {
    return complianceAnswerText(row, column?.answerKey);
  }

  /** `1 billede` / `{n} billeder` (compliance.js:1621). */
  imagesLabel(count: number): string {
    return count === 1
      ? this.translate.instant('1 image')
      : this.translate.instant('{{count}} images', {count});
  }

  trackBySection(_: number, section: ComplianceReportRenderedSection): string {
    return section.key;
  }

  trackByRow = (_: number, row: ComplianceReportRowVm): number => row.complianceId;

  // -------------------------------------------------------------------
  // Large results: reveal per sub-report
  // -------------------------------------------------------------------

  expandSection(section: ComplianceReportRenderedSection): void {
    // A NEW array identity, not a push: mtx-grid's `[data]` is an input and a
    // mutated-in-place array would not re-render.
    section.rows = section.allRows;
    section.expanded = true;
  }

  // -------------------------------------------------------------------
  // Meta line
  // -------------------------------------------------------------------

  /**
   * Property and calendar NAMES for the meta line.
   *
   * Loaded here rather than read off the filter bar: the two components are
   * siblings with no shared reference-data surface, and threading labels
   * through `ComplianceReportStateService` would mean the filter bar
   * re-publishing them from four call sites, any one of which can be missed.
   * The cost is one dictionary GET per mount of this view, plus one boards GET
   * only when a calendar filter is actually set. Neither touches
   * `setLoading()` — reference data is not a fetch (#1163).
   */
  private loadMetaReferenceData(): void {
    if (this.state.filters.propertyId != null) {
      this.propertiesService
        .getAllPropertiesDictionary()
        .pipe(takeUntil(this.destroy$))
        .subscribe((res) => {
          if (res && res.success) {
            this.properties = res.model ?? [];
          }
        });
    }
    const propertyId = this.state.filters.propertyId;
    if (propertyId != null && this.state.filters.boardIds.length > 0) {
      this.calendarService
        .getBoards(propertyId)
        .pipe(takeUntil(this.destroy$))
        .subscribe((res) => {
          if (res && res.success) {
            this.boards = res.model ?? [];
          }
        });
    }
  }

  /**
   * `Alle` — the prototype's word for an unfiltered dimension (`Ejendom: Alle`),
   * not `Alle ejendomme`, which would read as `Ejendom: Alle ejendomme`.
   */
  get propertyLabel(): string {
    const propertyId = this.state.filters.propertyId;
    if (propertyId == null) {
      return this.translate.instant('All');
    }
    const match = this.properties.find((p) => p.id === propertyId);
    // Until the dictionary lands the id is the honest answer — an empty label
    // would read as "no property filter", which is the opposite of the truth.
    return match ? match.name : `#${propertyId}`;
  }

  get boardLabel(): string {
    const boardIds = this.state.filters.boardIds;
    if (boardIds.length === 0) {
      return this.translate.instant('All');
    }
    const names = boardIds.map((id) => this.boards.find((b) => b.id === id)?.name ?? `#${id}`);
    return names.join(', ');
  }

  /** `01.01.2026 – 03.09.2026`, or empty for an incomplete `Sæt periode` range. */
  get periodLabel(): string {
    const bounds = this.state.periodBounds;
    if (!bounds) {
      return '';
    }
    return `${formatComplianceReportDate(bounds.from)} – ${formatComplianceReportDate(bounds.to)}`;
  }

  // -------------------------------------------------------------------
  // Actions
  // -------------------------------------------------------------------

  /**
   * `Rediger`. Only completed cases have anything to edit (compliance.js:1645).
   */
  canEdit(row: ComplianceReportRowVm): boolean {
    return row.completed && row.sdkCaseId > 0 && row.checkListId > 0;
  }

  /**
   * Opens the real eForm editor for THAT case, by navigating to the case route
   * the sibling reports page already uses for exactly this job
   * (`report-container.component.ts:205-207`).
   *
   * DELIBERATELY NOT `ComplianceCaseModalComponent`, which #1167 §7 recommends.
   * That modal writes `replyRequest.siteId = data.workerId` on save and PUTs it
   * through the client's `updateCase()` to `compliances/cases`, whose C# handler
   * `BackendConfigurationCompliancesService.Update(ReplyRequest)` assigns it
   * straight to `foundCase.SiteId` — so opening it without a real worker id
   * RE-HOMES the SDK case to site 0. #1166's `ComplianceReportCaseModel` carries worker NAMES and
   * no site ids (the same gap #1165 hit on `assigneeIds`), and the only producer
   * of that id is the calendar's `prepare-complete`, which needs an
   * `areaRulePlanningId` this DTO does not carry either. The case route takes
   * `sdkCaseId / templateId / planningId`, writes no site id, and its third
   * segment is read into a field the page never uses — so the compliance id is
   * passed there, giving the URL a meaningful value rather than a filler.
   *
   * The cost, accepted: a full navigation discards the fetched result. The
   * filters survive (the state service lives on the cached lazy module ref),
   * but `enterPage()` forces Rapport back to its un-fetched state, so returning
   * costs one `Opdater tabel`. Restoring the modal is a one-line change once the
   * row DTO carries a site id.
   */
  onEdit(row: ComplianceReportRowVm): void {
    if (!this.canEdit(row)) {
      return;
    }
    this.router
      .navigate(
        ['/plugins/backend-configuration-pn/case', row.sdkCaseId, row.checkListId, row.complianceId],
        {queryParams: {reverseRoute: this.router.url}},
      )
      .then();
  }

  /**
   * `Slet`, on EVERY row — the divergence from Detaljer, which renders it for
   * not-completed rows only (compliance.js:1246 vs :1652).
   */
  openDeleteConfirm(row: ComplianceReportRowVm): void {
    this.pendingDeleteId = row.complianceId;
    this.deleteDialogRef = this.dialog.open(this.deleteConfirmTpl, {autoFocus: false});
    this.deleteDialogRef.afterClosed().pipe(takeUntil(this.destroy$)).subscribe(() => {
      this.deleteDialogRef = null;
      this.pendingDeleteId = null;
    });
  }

  cancelDelete(): void {
    this.closeDeleteDialog();
  }

  /**
   * Deletes the COMPLIANCE LOG ROW through the existing endpoint
   * (`DELETE api/backend-configuration-pn/compliances/delete/{id}`) and nothing
   * else. That endpoint is shared with the standalone `/compliances` table and
   * with task-tracker, so neither it nor `deleteCompliance()` is touched here —
   * this is a new caller of an unchanged method.
   */
  confirmDelete(): void {
    const id = this.pendingDeleteId;
    this.closeDeleteDialog();
    if (id == null) {
      return;
    }
    this.compliancesService
      .deleteCompliance(id)
      .pipe(takeUntil(this.destroy$))
      .subscribe((res) => {
        if (res?.success) {
          this.refresh$.next();
        }
      });
  }

  private closeDeleteDialog(): void {
    this.deleteDialogRef?.close();
    this.deleteDialogRef = null;
    this.pendingDeleteId = null;
  }
}
