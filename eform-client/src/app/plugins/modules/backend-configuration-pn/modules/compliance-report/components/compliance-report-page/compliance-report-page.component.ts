import {Component, OnInit} from '@angular/core';
import {ComplianceExportFormat} from '../compliance-report-filters/compliance-report-filters.component';
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
export class ComplianceReportPageComponent implements OnInit {
  readonly modes: {mode: ComplianceMode; label: string}[] = [
    // Deliberately NOT the existing 'Overview' key: its Danish is 'Overblik'
    // and it is used on unrelated screens, so retranslating it to 'Oversigt'
    // would silently change them.
    {mode: 'overview', label: 'Compliance overview'},
    {mode: 'details', label: 'Compliance details'},
    {mode: 'report', label: 'Compliance report'},
  ];

  constructor(public state: ComplianceReportStateService) {}

  ngOnInit(): void {
    // Land on a populated Oversigt rather than "click this button to see
    // anything": Oversigt is one cheap server-side aggregation per property
    // (#1162), and the prototype's own comment (compliance.js:2371-2372)
    // records the auto-fetch as a design choice. Exactly once, only in
    // Oversigt, and only here — filter datasets resolving asynchronously must
    // not re-trigger it.
    //
    // The Detaljer/Rapport half is NOT just "skip the fetch". The state
    // service lives on the lazy module, whose NgModuleRef Angular caches for
    // the app's lifetime, so re-entering the page inherits the previous
    // visit's `reportVisible`, `total` and the buffered `fetchRequested$`
    // trigger. Skipping `requestFetch()` alone would still mount the child
    // over a true `reportVisible` and let the replay fire an unbounded row
    // query with no user gesture, which #1163 §6 forbids. `enterPage()` owns
    // both branches; see its comment.
    this.state.enterPage();
  }

  isActive(mode: ComplianceMode): boolean {
    return this.state.mode === mode;
  }

  onModeChange(mode: ComplianceMode): void {
    // Never routes through setFilter: a mode switch preserves reportVisible, so
    // one fetch serves all three modes.
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

  /** #1169 replaces this stub with the server-side export call. */
  onDownloadRequested(format: ComplianceExportFormat): void {
    // Intentionally inert. The control is placed and correctly gated here so
    // #1169 only has to wire the request; do not add a client-side export —
    // decision 4 in #1160 puts PDF/Excel/CSV generation on the server.
    void format;
  }
}
