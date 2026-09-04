import {Component, OnDestroy, OnInit} from '@angular/core';
import {Subject, of} from 'rxjs';
import {catchError, finalize, switchMap, takeUntil, tap} from 'rxjs/operators';
import {
  ComplianceReportOverviewModel,
  ComplianceReportOverviewRequestModel,
  ComplianceReportOverviewRowModel,
} from '../../../../models';
import {BackendConfigurationPnComplianceReportService} from '../../../../services';
import {
  ComplianceLevel,
  ComplianceOverviewColumn,
  ComplianceOverviewSort,
  ComplianceOverviewSortKey,
  OVERVIEW_COLUMNS,
  OverdueLevel,
  complianceLevel,
  formatCompliancePercent,
  initialOverviewSort,
  nextOverviewSort,
  overdueLevel,
  sortSummaries,
} from '../../helpers';
import {ComplianceReportStateService} from '../../store';

/**
 * The Oversigt view of the standalone Compliance page (#1164): one row per
 * property — name, overdue count, compliance percentage — plus a weighted
 * totals row, with a drill-down into Detaljer.
 *
 * Its whole contract with the shell (#1163) is the same as Detaljer's:
 *
 *  - subscribe to `fetchRequested$`, the ONLY fetch trigger. It replays its
 *    last emission to a late subscriber on purpose, which is what makes a mode
 *    switch (an `ngSwitch` that destroys and recreates this component) query;
 *  - read `requestModel` AT FETCH TIME, never cached;
 *  - report `setTotalCount()` and `setLoading()` back.
 *
 * NOTHING IS RECOMPUTED HERE. `compliancePct`, `overdue`, `dueTotal` and the
 * weighted `totals` all come off #1162's response exactly as sent; this
 * component sorts, formats and bands them and nothing else. That is what keeps
 * #1169's export and this screen showing the same numbers.
 *
 * Sorting is CLIENT-SIDE and deliberately so: Oversigt is one row per property,
 * a handful of rows, already entirely in hand — a server round-trip per header
 * click would buy nothing. The aggregation endpoint has no `Sort` parameter to
 * push it into.
 */
@Component({
  standalone: false,
  selector: 'app-compliance-overview-view',
  templateUrl: './compliance-overview-view.component.html',
  styleUrls: ['./compliance-overview-view.component.scss'],
})
export class ComplianceOverviewViewComponent implements OnInit, OnDestroy {
  readonly columns: ComplianceOverviewColumn[] = OVERVIEW_COLUMNS;

  /** Sorted for display. `unsortedRows` keeps the server's order untouched. */
  rows: ComplianceReportOverviewRowModel[] = [];
  totals: ComplianceReportOverviewRowModel | null = null;
  sort: ComplianceOverviewSort = initialOverviewSort();
  hasFetched = false;
  /**
   * A fetch failed while there was NOTHING on screen to keep — i.e. the first
   * fetch of this visit. Same reasoning as Detaljer's: without it that case
   * renders an entirely blank card, because the shell's placeholder, the
   * spinner and the empty-result line are each gated off.
   */
  loadFailed = false;

  private unsortedRows: ComplianceReportOverviewRowModel[] = [];
  private destroy$ = new Subject<void>();

  constructor(
    public state: ComplianceReportStateService,
    private complianceReportService: BackendConfigurationPnComplianceReportService,
  ) {}

  ngOnInit(): void {
    this.state.fetchRequested$
      .pipe(
        tap(() => {
          // Cleared on every attempt: while the spinner is up the previous
          // failure is no longer the current state of the view.
          this.loadFailed = false;
          this.state.setLoading(true);
        }),
        // switchMap, so a trigger landing while an earlier request is in flight
        // cancels it rather than racing it into the view.
        switchMap(() =>
          this.complianceReportService.overview(this.buildRequest()).pipe(
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
          this.loadFailed = !this.hasFetched;
          return;
        }
        this.applyResponse(res.model ?? null);
      });
  }

  ngOnDestroy(): void {
    // No `setLoading(false)` here on purpose — and NOT because of any ordering
    // between the outgoing and the incoming `NgSwitchCase` child: there is none
    // to rely on. The reason is OWNERSHIP. `loading` is the SHELL's flag: the
    // shell resets it in `setMode()`, `setFilter()` and `enterPage()`, which
    // covers every transition that unmounts this component, and for the ordinary
    // teardown the `finalize` above already clears it (it sits UPSTREAM of
    // `takeUntil`, so completing the stream here unsubscribes through it and
    // fires the callback). A second reset here would be redundant with that
    // `finalize` on exactly the same teardown. #1165 removed the identical line
    // from `compliance-details-view.component.ts` for exactly this reason.
    this.destroy$.next();
    this.destroy$.complete();
  }

  // -------------------------------------------------------------------
  // Request
  // -------------------------------------------------------------------

  /**
   * The aggregation body: the shared filter set MINUS status, paging and sort.
   *
   * Built field by field rather than by spreading `requestModel` and deleting
   * keys, so the omissions are visible: `status` genuinely never reaches the
   * wire even though the (disabled) control still holds a value, and no
   * `pageIndex`/`pageSize`/`sort` can leak in when the paged model grows a
   * field. `dateFrom`/`dateTo` are copied through only when present — an
   * incomplete `Sæt periode` range means "no period filter", and the state
   * service omits them for that case.
   */
  private buildRequest(): ComplianceReportOverviewRequestModel {
    const model = this.state.requestModel;
    const request: ComplianceReportOverviewRequestModel = {
      propertyId: model.propertyId,
      boardIds: model.boardIds,
      tagIds: model.tagIds,
      siteIds: model.siteIds,
    };
    if (model.dateFrom !== undefined) {
      request.dateFrom = model.dateFrom;
    }
    if (model.dateTo !== undefined) {
      request.dateTo = model.dateTo;
    }
    return request;
  }

  private applyResponse(model: ComplianceReportOverviewModel | null): void {
    this.unsortedRows = model?.rows ?? [];
    this.totals = model?.totals ?? null;
    this.hasFetched = true;
    // This HAS a reader on screen in Oversigt today, so the call is load-
    // bearing, not contract parity: `ComplianceReportFiltersComponent.canDownload`
    // (compliance-report-filters.component.ts:337) is
    // `!!exportFormat && state.reportVisible && state.total > 0`, and it drives
    // `[disabled]` on `#complianceDownloadBtn`. Drop the call and Download stays
    // permanently dead after an Oversigt fetch.
    //
    // What is NOT a reader here is the pagination chrome: the shell hides the
    // whole <nav> outside Detaljer, and `Ingen resultater` lives INSIDE it.
    this.state.setTotalCount(this.unsortedRows.length);
    this.applySort();
  }

  // -------------------------------------------------------------------
  // Sorting — client-side, two-state cycle, per-key initial direction
  // -------------------------------------------------------------------

  onSortHeader(key: ComplianceOverviewSortKey): void {
    this.sort = nextOverviewSort(this.sort, key);
    this.applySort();
  }

  /**
   * Sort and render are deliberately split: `sortSummaries` returns a copy and
   * the template renders `rows` in the order it is handed, so re-sorting is a
   * pure function of (server rows, sort state) and never of what is already on
   * screen.
   */
  private applySort(): void {
    this.rows = sortSummaries(this.unsortedRows, this.sort.key, this.sort.direction);
  }

  /** `aria-sort` belongs on the `<th>`, and ONLY on the active column. */
  ariaSort(key: ComplianceOverviewSortKey): 'ascending' | 'descending' | null {
    if (this.sort.key !== key) {
      return null;
    }
    return this.sort.direction === 'desc' ? 'descending' : 'ascending';
  }

  isSorted(key: ComplianceOverviewSortKey): boolean {
    return this.sort.key === key;
  }

  isSortedDescending(key: ComplianceOverviewSortKey): boolean {
    return this.sort.key === key && this.sort.direction === 'desc';
  }

  // -------------------------------------------------------------------
  // Drill-down
  // -------------------------------------------------------------------

  /**
   * Oversigt → Detaljer for one property.
   *
   * `drillIntoProperty` is the shell's own method and does all of it: it sets
   * the property filter and forces status to `all` through the SILENT path (so
   * the already-fetched result is not blanked by the blank-on-change state
   * machine — `mtx-select` emitting on a programmatic write is exactly the trap
   * this avoids), records the drilled id and the pre-drill status, and switches
   * the mode. Returning to Oversigt unwinds both, and only while they still
   * hold what the drill wrote. Nothing about it is reimplemented here.
   *
   * The totals row does not call this: it carries `propertyId: 0` and is not
   * a property.
   */
  onRowActivated(row: ComplianceReportOverviewRowModel): void {
    this.state.drillIntoProperty(row.propertyId);
  }

  // -------------------------------------------------------------------
  // Presentation
  // -------------------------------------------------------------------

  formatPercent(pct: number | null): string {
    return formatCompliancePercent(pct);
  }

  complianceLevel(pct: number | null): ComplianceLevel {
    return complianceLevel(pct);
  }

  overdueLevel(count: number | null): OverdueLevel {
    return overdueLevel(count);
  }

  trackByRow(_: number, row: ComplianceReportOverviewRowModel): number {
    return row.propertyId;
  }
}
