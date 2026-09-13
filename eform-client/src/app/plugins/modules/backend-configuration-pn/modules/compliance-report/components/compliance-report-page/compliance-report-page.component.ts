import {Component, OnDestroy, OnInit} from '@angular/core';
import {MatDialog} from '@angular/material/dialog';
import {TranslateService} from '@ngx-translate/core';
import {Subject} from 'rxjs';
import {finalize, takeUntil} from 'rxjs/operators';
import {saveAs} from 'file-saver';
import {ComplianceReportExportRequestModel} from '../../../../models';
import {BackendConfigurationPnComplianceReportService} from '../../../../services';
import {ComplianceExportFormat} from '../compliance-report-filters/compliance-report-filters.component';
import {
  CompliancePdfPreviewDialogComponent,
  CompliancePdfPreviewDialogData,
} from '../compliance-pdf-preview-dialog/compliance-pdf-preview-dialog.component';
import {ComplianceMode, ComplianceReportStateService} from '../../store';

/**
 * The shell of the standalone Compliance page (#1160 / #1163): filter bar,
 * mode toggle, the single result container and the pagination chrome.
 *
 * It draws no rows of its own. #1164 (Oversigt), #1165 (Detaljer) and #1167
 * (Rapport) each render into the container's matching `ngSwitch` branch, read
 * their filters from `ComplianceReportStateService.requestModel`, subscribe to
 * `fetchRequested$` for the query trigger and report back through
 * `setTotalCount()` / `setLoading()`. Until they land the container is empty
 * after a fetch and the pagination reads "Ingen resultater" — the shell has no
 * rows to show, and saying so is more honest than a fake placeholder.
 */
@Component({
  standalone: false,
  selector: 'app-compliance-report-page',
  templateUrl: './compliance-report-page.component.html',
  styleUrls: ['./compliance-report-page.component.scss'],
})
export class ComplianceReportPageComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();

  readonly modes: {mode: ComplianceMode; label: string}[] = [
    // Deliberately NOT the existing 'Overview' key: its Danish is 'Overblik'
    // and it is used on unrelated screens, so retranslating it to 'Oversigt'
    // would silently change them.
    {mode: 'overview', label: 'Compliance overview'},
    {mode: 'details', label: 'Compliance details'},
    {mode: 'report', label: 'Compliance report'},
  ];

  /**
   * True while an export request is in flight (#1189). Bound into the filter
   * bar, which disables Download and shows a spinner; cleared by `finalize`
   * on success AND on error, so a 400 never leaves the button dead.
   */
  exporting = false;

  constructor(
    public state: ComplianceReportStateService,
    private complianceReportService: BackendConfigurationPnComplianceReportService,
    private dialog: MatDialog,
    private translate: TranslateService,
  ) {}

  ngOnInit(): void {
    // Land on a populated Oversigt rather than a placeholder: Oversigt is one
    // cheap server-side aggregation per property (#1162), and the prototype's
    // own comment (compliance.js:2371-2372) records the auto-fetch as a design
    // choice. Exactly once, only in Oversigt, and only here — filter datasets
    // resolving asynchronously must not re-trigger it.
    //
    // The Detaljer/Rapport half is NOT just "skip the fetch". The state
    // service lives on the lazy module, whose NgModuleRef Angular caches for
    // the app's lifetime, so re-entering the page inherits the previous
    // visit's `reportVisible`, `total` and the buffered `fetchRequested$`
    // trigger. Skipping `requestFetch()` alone would still mount the child
    // over a true `reportVisible` and let the replay fire an unbounded row
    // query with no user gesture, which #1163 §6 forbids. `enterPage()` owns
    // both branches; see its comment. Entering the page is deliberately NOT
    // the `Oversigt` reset (#1185 decision B1) — the previous visit's filters
    // and mode survive re-entry.
    this.state.enterPage();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  isActive(mode: ComplianceMode): boolean {
    return this.state.mode === mode;
  }

  onModeChange(mode: ComplianceMode): void {
    // Pressing `Oversigt` — from Detaljer, from Rapport, or while already in
    // Oversigt — is a RESET (#1185): every filter back to its default and one
    // Oversigt fetch. The other two buttons are plain mode switches that keep
    // the filters and `reportVisible`, so the child the ngSwitch creates
    // re-queries the same filters through the replay.
    if (mode === 'overview') {
      this.state.resetToOverview();
      return;
    }
    this.state.setMode(mode);
  }

  // --- pagination chrome (data owned by the active view) ---

  onPrevPage(): void {
    if (this.state.showAll || this.state.page === 0) {
      return;
    }
    this.state.setPage(this.state.page - 1);
  }

  onNextPage(): void {
    if (this.state.showAll || this.state.page >= this.state.totalPages - 1) {
      return;
    }
    this.state.setPage(this.state.page + 1);
  }

  onGoToPage(pageIndex: number | 'gap'): void {
    if (pageIndex === 'gap') {
      return;
    }
    this.state.setPage(pageIndex);
  }

  onShowAll(): void {
    this.state.setShowAll();
  }

  /**
   * "Hent som" → Download (#1189). Posts the server-side export (#1169 — the
   * PDF/CSV is GENERATED on the server, #1160 decision 4; nothing here
   * renders a document) for the CURRENT view and filters, then either saves
   * the CSV straight away or opens the PDF preview dialog over the received
   * bytes.
   *
   * The body is `state.requestModel` as-is plus `viewMode`/`format`/
   * `includeImageAppendix`. `requestModel` also carries `pageIndex`,
   * `pageSize`, `sort` and `isSortDsc`, which the C# request model does not
   * declare; the model binder ignores them, so they are harmless on the wire.
   * `dateFrom`/`dateTo` are omitted by the state service when the custom range
   * is incomplete — the server then binds `default(DateTime)` and returns an
   * empty file — but `canDownload` (`reportVisible && total > 0`) already
   * keeps that path unreachable: nothing is visible without a valid period.
   */
  onDownloadRequested(format: ComplianceExportFormat): void {
    if (this.exporting) {
      return;
    }
    const body: ComplianceReportExportRequestModel = {
      ...this.state.requestModel,
      viewMode: this.state.mode,
      format,
      // The image appendix is on for a Rapport PDF (#1192, mock-up p9 shows the
      // "Bilag" pages always present) and off otherwise: CSV cannot carry an
      // image and Oversigt/Detaljer have none. The SERVER default stays
      // `false` for API callers; this is the UI's choice, sent explicitly so
      // the wire shape is complete.
      includeImageAppendix: format === 'pdf' && this.state.mode === 'report',
    };

    this.exporting = true;
    this.complianceReportService
      .export(body, this.buildFallbackFileName(body))
      .pipe(
        // Runs on success, on error AND on teardown, so the button re-enables
        // whatever the outcome.
        finalize(() => (this.exporting = false)),
        takeUntil(this.destroy$),
      )
      .subscribe({
        next: ({blob, fileName}) => {
          if (format === 'csv') {
            saveAs(blob, fileName);
            return;
          }
          this.dialog.open(CompliancePdfPreviewDialogComponent, {
            data: {blob, fileName} as CompliancePdfPreviewDialogData,
            // Wide enough for an A4-landscape page to be readable without
            // zooming; the dialog's own SCSS sizes the viewer height.
            width: 'min(95vw, 1400px)',
            maxWidth: '95vw',
            autoFocus: false,
          });
        },
        // The service has already toasted (`Export failed`); nothing else to
        // do here — `finalize` above re-enables the button.
        error: () => {},
      });
  }

  /**
   * Last-resort file name, used only when the response carried no readable
   * `Content-Disposition` (the server always sends one; a proxy that strips
   * it is the case this covers). Same `{view}-...-{from}-{to}.{ext}` skeleton
   * as the server's, minus the property/board labels the page does not hold.
   */
  private buildFallbackFileName(body: ComplianceReportExportRequestModel): string {
    const viewLabel = this.modes.find((m) => m.mode === body.viewMode)?.label ?? 'Compliance';
    const toDanishDate = (iso: string | undefined): string => {
      // yyyy-MM-dd → dd.MM.yyyy, matching `ComplianceExportFileNaming`.
      const m = iso ? /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso) : null;
      return m ? `${m[3]}.${m[2]}.${m[1]}` : '';
    };
    const parts = [
      this.translate.instant(viewLabel),
      toDanishDate(body.dateFrom),
      toDanishDate(body.dateTo),
    ].filter((part) => !!part);
    return `${parts.join('-')}.${body.format}`;
  }
}
