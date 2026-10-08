import {Overlay} from '@angular/cdk/overlay';
import {DatePipe, DecimalPipe, NgIf} from '@angular/common';
import {Component, OnDestroy, OnInit, inject} from '@angular/core';
import {MatButtonModule} from '@angular/material/button';
import {MatCardModule} from '@angular/material/card';
import {MatDialog} from '@angular/material/dialog';
import {MatIconModule} from '@angular/material/icon';
import {MatTableModule} from '@angular/material/table';
import {MatTooltipModule} from '@angular/material/tooltip';
import {ActivatedRoute, Router, RouterModule} from '@angular/router';
import {TranslateModule, TranslateService} from '@ngx-translate/core';
import {Subscription, catchError, finalize, forkJoin, map, of, switchMap, tap} from 'rxjs';
import {TailBiteOutbreakDetail, TailBiteWorker} from '../../../../models';
import {BackendConfigurationPnTailBiteService} from '../../../../services';
import {openConfirm} from '../../shared/tail-bite-confirm';
import {OUTBREAK_STATUS_BADGE, OUTBREAK_STATUS_LABEL} from '../../shared/tail-bite-outbreak-status';
import {TAIL_BITE_BASE} from '../../shared/tail-bite-route';
import {describeRule} from '../../shared/tail-bite-rule-text';
import {TailBiteAssessmentFormComponent} from '../tail-bite-assessment-form/tail-bite-assessment-form.component';
import {TailBiteFollowUpsComponent} from '../tail-bite-follow-ups/tail-bite-follow-ups.component';
import {askText} from '../tail-bite-text-dialog/tail-bite-text-dialog.component';
import {TailBiteOutbreakRowView, TailBiteOutbreakView, buildOutbreakView} from './tail-bite-outbreak-view';

/**
 * One outbreak: the registrations behind it, the risk assessment, the follow-ups and closing (manager only;
 * the server refuses everyone else with "Not found or no access."). The outbreak names its property: a link that
 * carries another property id is corrected to the outbreak's own, so the picker always shows the right property.
 */
@Component({
  selector: 'app-tail-bite-outbreak-detail',
  templateUrl: './tail-bite-outbreak-detail.component.html',
  imports: [DatePipe, DecimalPipe, NgIf, RouterModule, MatButtonModule, MatCardModule, MatIconModule, MatTableModule, MatTooltipModule,
    TranslateModule, TailBiteAssessmentFormComponent, TailBiteFollowUpsComponent],
})
export class TailBiteOutbreakDetailComponent implements OnInit, OnDestroy {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private service = inject(BackendConfigurationPnTailBiteService);
  private dialog = inject(MatDialog);
  private overlay = inject(Overlay);
  private translate = inject(TranslateService);

  readonly statusLabel = OUTBREAK_STATUS_LABEL;
  readonly statusBadge = OUTBREAK_STATUS_BADGE;
  readonly columns = ['effectiveAt', 'location', 'minor', 'severe', 'actions', 'siteName', 'photos', 'cancel'];
  outbreakId: number | null = null;
  propertyId: number | null = null;
  detail: TailBiteOutbreakDetail | null = null;
  view: TailBiteOutbreakView | null = null;
  workers: TailBiteWorker[] = [];
  /** Single-flight: set while a cancel (with its reason dialog) or a close is running. */
  busy = false;
  /** True when the first load of this outbreak was refused or failed: the page says so instead of staying blank. */
  notFound = false;
  private redirected = false;
  /** Bumped by every request; an answer carrying an older number (other outbreak, older refresh, left page) is dropped. */
  private requestSeq = 0;
  private request?: Subscription;
  private sub?: Subscription;

  ngOnInit(): void {
    this.sub = this.route.paramMap.subscribe((params) => {
      const id = Number(params.get('id'));
      this.outbreakId = Number.isInteger(id) && id > 0 ? id : null;
      this.propertyId = Number(params.get('propertyId'));
      this.clearPage();
      this.load();
    });
  }

  ngOnDestroy(): void {
    this.requestSeq++;
    this.request?.unsubscribe();
    this.sub?.unsubscribe();
  }

  /** Forgets the outbreak so nothing of the previous one is visible while the next loads. */
  private clearPage(): void {
    this.requestSeq++;
    this.request?.unsubscribe();
    this.detail = null;
    this.view = null;
    this.workers = [];
    this.notFound = false;
  }

  /** Called when a load ends without a page: only a first load (nothing shown yet, no redirect under way) is a "not found". */
  private loadFailed(seq: number): void {
    if (seq === this.requestSeq && !this.view && !this.redirected) {
      this.notFound = true;
    }
  }

  /** Refreshes the page. A refused or failed refresh keeps what is shown; only the first load starts (and stays) empty. */
  load(): void {
    const id = this.outbreakId;
    if (id === null) {
      return;
    }
    const seq = ++this.requestSeq;
    this.redirected = false;
    this.request?.unsubscribe();
    this.request = forkJoin({detail: this.service.getOutbreak(id), regs: this.service.getOutbreakRegistrations(id)})
      .pipe(
        switchMap(({detail, regs}) => {
          if (seq !== this.requestSeq || !detail?.success || !regs?.success) {
            return of(null);
          }
          const propertyId = regs.model.propertyId;
          if (propertyId !== this.propertyId) {
            // The URL names another property: move to the outbreak's own; the route change reloads.
            this.redirected = true;
            this.router.navigate([TAIL_BITE_BASE, propertyId, 'outbreaks', id], {replaceUrl: true});
            return of(null);
          }
          return forkJoin({
            tree: this.service.getTree(propertyId),
            // The rest only enriches the page: a failed call is treated like a refused one.
            workers: this.service.getWorkers(propertyId).pipe(catchError(() => of(null))),
            occupancy: this.service.getOccupancy(propertyId).pipe(catchError(() => of(null))),
            history: this.service.getRuleHistory(detail.model.ruleId).pipe(catchError(() => of(null))),
          }).pipe(map((rest) => ({detail: detail.model, regs: regs.model, ...rest})));
        }),
      )
      .subscribe({
        next: (data) => {
          if (seq !== this.requestSeq) {
            return;
          }
          if (!data?.tree?.success) {
            this.loadFailed(seq);
            return;
          }
          this.notFound = false;
          this.detail = data.detail;
          this.workers = data.workers?.success ? data.workers.model : [];
          this.view = buildOutbreakView(
            data.detail,
            data.regs,
            data.tree.model,
            data.occupancy?.success ? data.occupancy.model : [],
            data.history?.success ? data.history.model : [],
            this.translate.instant('Deleted location'),
          );
        },
        // A failed refresh keeps what is shown; the API service already toasts the error.
        error: () => this.loadFailed(seq),
      });
  }

  get ruleText(): string {
    return this.view?.rule ? describeRule(this.translate, this.view.rule) : '';
  }

  /** Mirrors TailBiteOutbreakService.Close: assessed, no open follow-up, not closed already. */
  get canClose(): boolean {
    return this.view?.status === 'readyToClose';
  }

  /** The translation key saying why closing is not possible yet; null when it is, or when the outbreak is closed. */
  get closeBlockedReason(): string | null {
    switch (this.view?.status) {
      case 'needsAssessment':
        return 'Save the risk assessment before closing.';
      case 'followUp':
        return 'Can be closed when every follow-up is done or withdrawn.';
      default:
        return null;
    }
  }

  /** Asks first. A refusal or failure reloads (the page may be out of date) and leaves the dialog open for a retry. */
  close(): void {
    const detail = this.detail;
    if (this.busy || !this.canClose || !detail || !this.view) {
      return;
    }
    openConfirm(this.dialog, this.overlay, {
      headerText: this.translate.instant('Close outbreak'), itemLabel: this.translate.instant('Location'), itemName: this.view.title,
      confirmText: this.translate.instant('Close outbreak'), confirmId: 'tailBiteCloseConfirm',
    }, () => {
      this.busy = true;
      return this.service.closeOutbreak(detail.summary.id).pipe(
        tap({
          next: (res) => {
            if (!res?.success) {
              this.load();
            }
          },
          error: () => this.load(),
        }),
        finalize(() => (this.busy = false)),
      );
    }).subscribe({next: () => this.load(), error: () => undefined});
  }

  /** The reason dialog keeps the page busy; dismissing it completes without a value, which only frees the page. */
  cancelRegistration(row: TailBiteOutbreakRowView): void {
    if (this.busy) {
      return;
    }
    this.busy = true;
    askText(this.dialog, this.overlay, {title: this.translate.instant('Cancel registration'), label: 'Reason', maxLength: 1000})
      .pipe(
        switchMap((reason) => this.service.cancelRegistration(row.registrationId, reason)),
        finalize(() => (this.busy = false)),
      )
      .subscribe({next: () => this.load(), error: () => this.load()});
  }
}
