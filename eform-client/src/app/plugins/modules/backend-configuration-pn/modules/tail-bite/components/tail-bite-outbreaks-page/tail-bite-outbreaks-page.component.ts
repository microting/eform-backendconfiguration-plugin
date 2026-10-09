import {DatePipe, NgIf} from '@angular/common';
import {Component, OnDestroy, OnInit, inject} from '@angular/core';
import {FormsModule} from '@angular/forms';
import {MatCardModule} from '@angular/material/card';
import {MatSlideToggleModule} from '@angular/material/slide-toggle';
import {MatTableModule} from '@angular/material/table';
import {ActivatedRoute, RouterModule} from '@angular/router';
import {TranslateModule} from '@ngx-translate/core';
import {Subscription, forkJoin} from 'rxjs';
import {BackendConfigurationPnTailBiteService} from '../../../../services';
import {OUTBREAK_STATUS_BADGE, OUTBREAK_STATUS_LABEL} from '../../shared/tail-bite-outbreak-status';
import {propertyIdParam} from '../../shared/tail-bite-route';
import {TailBiteOutbreakListRow, outbreakRows} from './tail-bite-outbreak-rows';

/** Outbreaks of the property in the URL, open ones by default, newest first (the server's order). */
@Component({
  selector: 'app-tail-bite-outbreaks-page',
  templateUrl: './tail-bite-outbreaks-page.component.html',
  imports: [DatePipe, NgIf, FormsModule, RouterModule, MatCardModule, MatSlideToggleModule, MatTableModule, TranslateModule],
})
export class TailBiteOutbreaksPageComponent implements OnInit, OnDestroy {
  private service = inject(BackendConfigurationPnTailBiteService);
  private route = inject(ActivatedRoute);

  readonly statusLabel = OUTBREAK_STATUS_LABEL;
  readonly statusBadge = OUTBREAK_STATUS_BADGE;
  readonly columns = ['title', 'openedAt', 'status', 'openActions'];
  rows: TailBiteOutbreakListRow[] = [];
  showClosed = false;
  /** The toggle value the shown rows were loaded with; a failed toggle load turns the toggle back to it. */
  private rowsShowClosed = false;
  /** True once a list for the current property and toggle has arrived; "no outbreaks" is only said then. */
  loaded = false;
  private propertyId: number | null = null;
  /** Bumped by every request; an answer carrying an older number (old property, old toggle, older refresh) is dropped. */
  private requestSeq = 0;
  private subs = new Subscription();
  private request?: Subscription;

  ngOnInit(): void {
    this.subs.add(
      propertyIdParam(this.route).subscribe((id) => {
        this.propertyId = id;
        this.clearPage();
        this.load();
      }),
    );
  }

  ngOnDestroy(): void {
    this.requestSeq++;
    this.request?.unsubscribe();
    this.subs.unsubscribe();
  }

  /**
   * Reloads for the new toggle value. The shown rows stay until the new list arrives; a refused or failed load keeps them
   * and turns the toggle back, so the toggle always matches the rows.
   */
  setShowClosed(show: boolean): void {
    this.showClosed = show;
    this.load(() => {
      this.showClosed = this.rowsShowClosed;
    });
  }

  /** Forgets the list so nothing stale is visible while the next one loads. */
  private clearPage(): void {
    this.rows = [];
    this.loaded = false;
  }

  /**
   * Refreshes the list. A refused or failed refresh keeps what is shown (and runs `onFailed`, if this is still the latest
   * request); only the first load starts (and stays) empty.
   */
  load(onFailed?: () => void): void {
    const propertyId = this.propertyId;
    if (propertyId === null) {
      return;
    }
    const seq = ++this.requestSeq;
    const showClosed = this.showClosed;
    this.request?.unsubscribe();
    this.request = forkJoin([this.service.getOutbreaks(propertyId, !showClosed), this.service.getTree(propertyId)]).subscribe({
      next: ([outbreaks, tree]) => {
        if (seq !== this.requestSeq) {
          return;
        }
        if (outbreaks?.success && tree?.success) {
          this.rows = outbreakRows(outbreaks.model, tree.model.locations);
          this.rowsShowClosed = showClosed;
          this.loaded = true;
        } else {
          onFailed?.();
        }
      },
      // A failed refresh keeps what is shown; the API service already toasts the error.
      error: () => {
        if (seq === this.requestSeq) {
          onFailed?.();
        }
      },
    });
  }
}
