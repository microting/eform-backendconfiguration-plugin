import {Overlay} from '@angular/cdk/overlay';
import {DatePipe, NgFor, NgIf, NgSwitch, NgSwitchCase, NgSwitchDefault} from '@angular/common';
import {Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, inject} from '@angular/core';
import {FormsModule} from '@angular/forms';
import {MatDialog} from '@angular/material/dialog';
import {MatFormFieldModule} from '@angular/material/form-field';
import {MtxSelectModule} from '@ng-matero/extensions/select';
import {TranslateModule, TranslateService} from '@ngx-translate/core';
import {Observable, finalize, switchMap} from 'rxjs';
import {OperationResult} from 'src/app/common/models';
import {TAIL_BITE_FACTORS, TailBiteFactor, TailBiteOutbreakAction, TailBiteOutbreakDetail, TailBiteWorker} from '../../../../models';
import {BackendConfigurationPnTailBiteService} from '../../../../services';
import {dateOnlyToLocal, isOverdue, serverDay} from '../../shared/tail-bite-dates';
import {memoize} from '../../shared/tail-bite-memo';
import {askText} from '../tail-bite-text-dialog/tail-bite-text-dialog.component';

/** A select item: a worker, or the responsible person who left (disabled). */
export type TailBiteWorkerItem = TailBiteWorker & {disabled?: boolean};

export type TailBiteActionState = 'done' | 'withdrawn' | 'overdue' | 'open';

/** Withdrawn beats done beats overdue. Due today is open: the comparison is by calendar day, with no time zone shift. */
export function actionState(a: TailBiteOutbreakAction, today: Date): TailBiteActionState {
  if (a.withdrawnAt) {
    return 'withdrawn';
  }
  if (a.doneAt) {
    return 'done';
  }
  return isOverdue(a.followUpDate, today) ? 'overdue' : 'open';
}

/** The server refuses to withdraw or reassign an action that is done or withdrawn, so the UI does not offer it. */
export function canChangeAction(state: TailBiteActionState): boolean {
  return state === 'open' || state === 'overdue';
}

/**
 * The follow-up actions of an outbreak: mark done (and undo), withdraw with a reason, reassign. Withdrawn
 * actions stay listed (they are part of the audit trail). A responsible person who has left the property is
 * flagged so the manager reassigns or withdraws. Every outcome of a change, also a refusal, tells the parent
 * to reload, so the page always shows what the server holds.
 */
@Component({
  selector: 'app-tail-bite-follow-ups',
  templateUrl: './tail-bite-follow-ups.component.html',
  imports: [DatePipe, NgFor, NgIf, NgSwitch, NgSwitchCase, NgSwitchDefault, FormsModule, MatFormFieldModule, MtxSelectModule, TranslateModule],
})
export class TailBiteFollowUpsComponent implements OnChanges {
  private service = inject(BackendConfigurationPnTailBiteService);
  private dialog = inject(MatDialog);
  private overlay = inject(Overlay);
  private translate = inject(TranslateService);

  @Input({required: true}) detail!: TailBiteOutbreakDetail;
  @Input() workers: TailBiteWorker[] = [];
  @Output() changed = new EventEmitter<void>();

  today = new Date();
  /** Single-flight: set before a change (or its reason dialog), reset when it ends. */
  busy = false;

  /** The worker picked per action while a reassignment is saved; dropped on a refusal and on every fresh detail. */
  private picked = new Map<number, number>();

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['detail']) {
      this.picked.clear();
      this.today = new Date();
    }
  }

  /** Per-action select items, rebuilt only when the detail or the workers are another object. */
  private itemsCache = new Map<number, TailBiteWorkerItem[]>();
  private itemsFor?: {detail: TailBiteOutbreakDetail; workers: TailBiteWorker[]};

  /**
   * The workers on the property, plus the responsible person if they have left (disabled, so the select still names
   * them but they cannot be picked again). The left worker's name is not known here, so it shows as #id.
   */
  itemsOf(a: TailBiteOutbreakAction): TailBiteWorkerItem[] {
    if (this.itemsFor?.detail !== this.detail || this.itemsFor?.workers !== this.workers) {
      this.itemsCache.clear();
      this.itemsFor = {detail: this.detail, workers: this.workers};
    }
    let items = this.itemsCache.get(a.id);
    if (!items) {
      items = this.isOnProperty(a.responsibleSiteId)
        ? this.workers
        : [...this.workers, {siteId: a.responsibleSiteId, name: this.workerName(a.responsibleSiteId), isManager: false, propertyWorkerIds: [], disabled: true}];
      this.itemsCache.set(a.id, items);
    }
    return items;
  }

  get actions(): TailBiteOutbreakAction[] {
    return this.sortedActions(this.detail.actions);
  }

  // The rows keep their array until the server sends another action list (see memoize).
  private readonly sortedActions = memoize((actions: TailBiteOutbreakAction[]) =>
    [...actions].sort((a, b) => serverDay(a.followUpDate).localeCompare(serverDay(b.followUpDate)) || a.id - b.id));

  get editable(): boolean {
    return !this.detail.summary.closed;
  }

  factorLabel(factor: TailBiteFactor): string {
    return TAIL_BITE_FACTORS.find((f) => f.factor === factor)?.label ?? '';
  }

  followUpDate(a: TailBiteOutbreakAction): Date {
    return dateOnlyToLocal(a.followUpDate);
  }

  isOnProperty(siteId: number): boolean {
    return this.workers.some((w) => w.siteId === siteId);
  }

  workerName(siteId: number): string {
    return this.workers.find((w) => w.siteId === siteId)?.name ?? `#${siteId}`;
  }

  state(a: TailBiteOutbreakAction): TailBiteActionState {
    return actionState(a, this.today);
  }

  /** Done can be set and undone, but not on a withdrawn action. */
  canDone(a: TailBiteOutbreakAction): boolean {
    return this.editable && this.state(a) !== 'withdrawn';
  }

  /** Withdraw and reassign: open and overdue actions only. */
  canChange(a: TailBiteOutbreakAction): boolean {
    return this.editable && canChangeAction(this.state(a));
  }

  doneLabel(a: TailBiteOutbreakAction): string {
    return a.doneAt ? 'Mark not done' : 'Mark done';
  }

  /** The select's value: the worker just picked while saving, otherwise the one the server holds. */
  selectedSite(a: TailBiteOutbreakAction): number {
    return this.picked.get(a.id) ?? a.responsibleSiteId;
  }

  toggleDone(a: TailBiteOutbreakAction): void {
    if (this.busy || !this.canDone(a)) {
      return;
    }
    this.run(this.service.setActionDone(a.id, a.doneAt === null));
  }

  reassign(a: TailBiteOutbreakAction, siteId: number | null): void {
    if (this.busy) {
      return;
    }
    if (!this.canChange(a) || siteId === null || siteId === a.responsibleSiteId) {
      this.picked.delete(a.id);
      return;
    }
    this.picked.set(a.id, siteId);
    this.run(this.service.reassignAction(a.id, siteId), () => this.picked.delete(a.id));
  }

  /** The reason dialog keeps the page busy; dismissing it completes without a value, which only frees the page. */
  withdraw(a: TailBiteOutbreakAction): void {
    if (this.busy || !this.canChange(a)) {
      return;
    }
    const reason$ = askText(this.dialog, this.overlay, {title: this.translate.instant('Withdraw'), label: 'Reason', maxLength: 1000});
    this.run(reason$.pipe(switchMap((reason) => this.service.withdrawAction(a.id, reason))));
  }

  private run(call: Observable<OperationResult>, onRefused?: () => void): void {
    this.busy = true;
    call.pipe(finalize(() => (this.busy = false))).subscribe({
      next: (res) => {
        if (!res?.success) {
          onRefused?.();
        }
        this.changed.emit();
      },
      error: () => {
        onRefused?.();
        this.changed.emit();
      },
    });
  }
}
