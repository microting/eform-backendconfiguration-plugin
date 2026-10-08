import {DatePipe, NgFor, NgIf} from '@angular/common';
import {Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, inject} from '@angular/core';
import {FormsModule} from '@angular/forms';
import {MatButtonModule} from '@angular/material/button';
import {MatButtonToggleModule} from '@angular/material/button-toggle';
import {MatDatepickerModule} from '@angular/material/datepicker';
import {MatFormFieldModule} from '@angular/material/form-field';
import {MatIconModule} from '@angular/material/icon';
import {MatInputModule} from '@angular/material/input';
import {MtxSelectModule} from '@ng-matero/extensions/select';
import {TranslateModule} from '@ngx-translate/core';
import {finalize} from 'rxjs';
import {
  TAIL_BITE_FACTORS,
  TailBiteAssignableWorker,
  TailBiteFactor,
  TailBiteOutbreakAction,
  TailBiteOutbreakDetail,
} from '../../../../models';
import {BackendConfigurationPnTailBiteService} from '../../../../services';
import {dateOnlyToLocal, openedDay} from '../../shared/tail-bite-dates';
import {memoize} from '../../shared/tail-bite-memo';
import {
  TailBiteFactorDraft,
  TailBiteFactorError,
  assessmentErrors,
  buildSaveRequest,
  draftFromDetail,
  emptyDraftAction,
  liveExistingActions,
} from './tail-bite-assessment';

const ERROR_TEXT: Record<TailBiteFactorError, string> = {
  unanswered: 'Answer this factor.',
  needsAction: 'A yes needs at least one action with a description, a responsible person and a follow-up date.',
  incompleteAction: 'A yes needs at least one action with a description, a responsible person and a follow-up date.',
  followUpBeforeOpened: 'A follow-up date cannot be before the day the outbreak opened.',
};

/**
 * The six-factor risk assessment (spec §5): every factor answered; a yes needs an action with a responsible
 * person and a follow-up date. Saving again revises the assessment; a yes turned into no withdraws that
 * factor's open actions on the server, which the form warns about.
 */
@Component({
  selector: 'app-tail-bite-assessment-form',
  templateUrl: './tail-bite-assessment-form.component.html',
  imports: [DatePipe, NgFor, NgIf, FormsModule, MatButtonModule, MatButtonToggleModule, MatDatepickerModule, MatFormFieldModule,
    MatIconModule, MatInputModule, MtxSelectModule, TranslateModule],
})
export class TailBiteAssessmentFormComponent implements OnChanges {
  private service = inject(BackendConfigurationPnTailBiteService);

  @Input({required: true}) detail!: TailBiteOutbreakDetail;
  @Input() workers: TailBiteAssignableWorker[] = [];
  @Output() saved = new EventEmitter<void>();

  readonly factors = TAIL_BITE_FACTORS;
  drafts: TailBiteFactorDraft[] = [];
  errors = new Map<TailBiteFactor, TailBiteFactorError>();
  submitted = false;
  /** Single-flight: set before the call, reset in finalize. */
  busy = false;
  /** The last save was refused or failed; everything entered is kept. */
  saveFailed = false;
  /** Earliest pickable follow-up date. */
  minFollowUp: Date | null = null;

  /** The previous save succeeded, so the next detail is its result and the form starts over from it. */
  private justSaved = false;

  /**
   * Drafts are rebuilt only for another outbreak or after a successful save. Any other new detail (the page reloading after
   * an action was done, withdrawn or reassigned) keeps what the user typed and only revalidates against the fresh actions.
   */
  ngOnChanges(changes: SimpleChanges): void {
    if (!changes['detail'] || !this.detail) {
      return;
    }
    const previous: TailBiteOutbreakDetail | undefined = changes['detail'].previousValue;
    const reset = this.justSaved || !previous || previous.summary.id !== this.detail.summary.id || this.drafts.length === 0;
    // The server compares the picked day with OpenedAt.Date, the UTC day; the picker needs that day as a local date.
    const opened = openedDay(this.detail.summary.openedAt);
    this.minFollowUp = opened ? dateOnlyToLocal(opened) : null;
    if (reset) {
      this.drafts = draftFromDetail(this.detail);
      this.submitted = false;
      this.saveFailed = false;
      this.justSaved = false;
    }
    this.refresh();
  }

  get readOnly(): boolean {
    return this.detail.summary.closed;
  }

  draft(factor: TailBiteFactor): TailBiteFactorDraft {
    return this.drafts.find((d) => d.factor === factor)!;
  }

  existing(factor: TailBiteFactor): TailBiteOutbreakAction[] {
    return liveExistingActions(this.detail, factor);
  }

  setAnswer(factor: TailBiteFactor, answer: boolean): void {
    const d = this.draft(factor);
    d.answer = answer;
    if (answer && d.newActions.length === 0 && this.existing(factor).length === 0) {
      d.newActions.push(emptyDraftAction());
    }
    this.refresh();
  }

  addAction(factor: TailBiteFactor): void {
    this.draft(factor).newActions.push(emptyDraftAction());
    this.refresh();
  }

  removeAction(factor: TailBiteFactor, index: number): void {
    this.draft(factor).newActions.splice(index, 1);
    this.refresh();
  }

  /** A yes turned into no will withdraw open follow-ups of the factor. */
  withdrawsOnNo(factor: TailBiteFactor): boolean {
    return this.draft(factor).answer === false && this.existing(factor).some((a) => a.doneAt === null);
  }

  /** The responsible-person choices: resigned workers are listed for their names only. */
  get assignableWorkers(): TailBiteAssignableWorker[] {
    return this.assignableOf(this.workers);
  }

  // Bound to mtx-selects: the same array until the workers change (see memoize).
  private readonly assignableOf = memoize((workers: TailBiteAssignableWorker[]) => workers.filter((w) => w.assignable));

  workerName(siteId: number): string {
    return this.workers.find((w) => w.siteId === siteId)?.name ?? `#${siteId}`;
  }

  followUpDate(a: TailBiteOutbreakAction): Date {
    return dateOnlyToLocal(a.followUpDate);
  }

  /** Why a factor blocks saving: the message key for its error. One lookup, so no nested conditionals in the template. */
  errorText(error: TailBiteFactorError): string {
    return ERROR_TEXT[error];
  }

  /** Save cannot succeed yet; drives the hint beside the button from the same errors save() checks. */
  get hasErrors(): boolean {
    return this.errors.size > 0;
  }

  /** Recomputes the errors after every edit, and drops a stale failure notice. */
  refresh(): void {
    this.errors = assessmentErrors(this.drafts, this.detail);
    this.saveFailed = false;
  }

  /** Saves the form. A refused or failed save keeps every answer and action typed in and shows why nothing happened. */
  save(): void {
    this.submitted = true;
    this.refresh();
    if (this.hasErrors || this.busy || this.readOnly) {
      return;
    }
    this.busy = true;
    this.service.saveAssessment(this.detail.summary.id, buildSaveRequest(this.drafts))
      .pipe(finalize(() => {
        this.busy = false;
      }))
      .subscribe({
        next: (res) => {
          if (res?.success) {
            // The saved actions now come from the server; clearing them here stops a second click re-posting them.
            this.justSaved = true;
            this.drafts.forEach((d) => (d.newActions = []));
            this.submitted = false;
            this.refresh();
            this.saved.emit();
          } else {
            this.saveFailed = true;
          }
        },
        error: () => {
          this.saveFailed = true;
        },
      });
  }
}
