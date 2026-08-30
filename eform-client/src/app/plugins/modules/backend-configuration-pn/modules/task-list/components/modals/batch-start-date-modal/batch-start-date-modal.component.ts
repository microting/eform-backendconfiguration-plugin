import {Component, Inject, OnDestroy} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {Subject, Subscription, of} from 'rxjs';
import {catchError, debounceTime, switchMap} from 'rxjs/operators';
import {
  BackendConfigurationPnTaskListService,
  TaskListBatchStartDatePreviewModel,
} from '../../../../../services';
import {TaskListBatchModalData} from '../../task-list-page/task-list-page.component';

/**
 * Batch "change start date" modal (#1122) — re-anchors every selected task's
 * whole series to one new start date.
 *
 * Two things set it apart from its five sibling batch modals:
 *
 * 1. **The date picker has NO `minDate`.** Picking a date in the PAST is the
 *    entire point of the action: a yearly task re-anchored to 01-01 of this
 *    year is supposed to produce an overdue (red) occurrence on that date.
 *    Every other date input in this plugin floors at today; this one must not,
 *    and a future "tidy up the date floors" pass must not add one here.
 *
 * 2. **Save is gated on a resolved PREVIEW, not just on a filled-in field.**
 *    A past re-anchor retracts open occurrences and can deploy an unbounded
 *    number of overdue ones (daily for six months x every assigned site), so
 *    the admin must see the magnitude before committing. `valid` therefore
 *    requires `previewState === 'resolved'`; an in-flight or failed preview
 *    keeps Save disabled.
 *
 * Preview lifecycle — four explicit states, so Save can never be clicked
 * against a stale or missing projection:
 *
 *   idle      no date picked yet (or the date was cleared) -> hint text
 *   loading   a request is in flight -> spinner text, Save disabled
 *   resolved  counts rendered, Save enabled
 *   failed    request errored or answered success=false -> error text, Save
 *             stays disabled (a 4xx/5xx here usually means the endpoint or
 *             one of the selected tasks cannot be projected at all, which is
 *             exactly when applying blindly is worst)
 *
 * Superseded requests are cancelled by `switchMap` on a per-change Subject,
 * NOT by the debounce alone: debouncing only collapses rapid changes, it does
 * nothing about a slow first request landing AFTER a fast second one and
 * overwriting the newer counts. `switchMap` unsubscribes the previous inner
 * observable — which aborts the underlying HttpClient XHR — so the panel can
 * only ever show the counts for the date currently in the picker. It cancels
 * on a NEW emission only, though, so CLEARING the date (which emits nothing)
 * leaves the in-flight request alive; the subscriber therefore also drops any
 * result that arrives while `startDate` is null.
 */
type PreviewState = 'idle' | 'loading' | 'resolved' | 'failed';

@Component({
  standalone: false,
  selector: 'app-batch-start-date-modal',
  templateUrl: './batch-start-date-modal.component.html',
})
export class BatchStartDateModalComponent implements OnDestroy {
  // Deliberately NOT seeded with today's date. Nothing is pre-filled, for the
  // same reason the compliance modal pre-selects neither radio: an admin who
  // only opened the modal to see what it offers must not be one click away
  // from silently re-anchoring every selected series to today.
  startDate: Date | null = null;

  previewState: PreviewState = 'idle';
  preview: TaskListBatchStartDatePreviewModel | null = null;

  private readonly dateChanged$ = new Subject<Date>();
  private readonly sub: Subscription;

  constructor(
    public dialogRef: MatDialogRef<BatchStartDateModalComponent>,
    @Inject(MAT_DIALOG_DATA) public data: TaskListBatchModalData,
    private taskListService: BackendConfigurationPnTaskListService,
  ) {
    this.sub = this.dateChanged$
      .pipe(
        // The mat-datepicker also emits while the user TYPES into the input,
        // so collapse bursts before hitting the server.
        debounceTime(400),
        switchMap(date =>
          this.taskListService
            .changeStartDatePreview({taskIds: this.taskIds, startDate: toDateOnlyString(date)})
            // Caught INSIDE the switchMap: an error escaping to the outer
            // stream would complete the subscription and leave every later
            // date change unpreviewable (and therefore Save permanently
            // disabled) for the life of the dialog.
            .pipe(catchError(() => of(null))),
        ),
      )
      .subscribe(res => {
        // Late-arrival guard. `switchMap` only cancels a request when a NEW
        // date is emitted; clearing the field returns from `onDateChange`
        // WITHOUT emitting, so a preview already in flight is never
        // unsubscribed and lands normally seconds later. Without this check it
        // would flip the panel from the idle hint back to 'resolved' and
        // render the cleared date's counts next to an empty input. Keyed on
        // `startDate` (the ngModel-bound field, already null by the time the
        // response arrives) so ANY supersession is covered, not just clearing.
        if (this.startDate == null) {
          return;
        }
        if (res && res.success && res.model) {
          this.preview = res.model;
          this.previewState = 'resolved';
        } else {
          this.preview = null;
          this.previewState = 'failed';
        }
      });
  }

  ngOnDestroy() {
    // Also aborts an in-flight preview when the dialog is closed mid-request.
    this.sub.unsubscribe();
    this.dateChanged$.complete();
  }

  get valid(): boolean {
    return this.startDate != null && this.previewState === 'resolved';
  }

  private get taskIds(): number[] {
    return this.data.selectedTasks.map(t => t.id);
  }

  onDateChange(date: Date | null) {
    this.preview = null;
    if (date == null) {
      // Clearing the field must drop the panel back to idle, not leave the
      // previous date's counts on screen next to an empty input.
      this.previewState = 'idle';
      return;
    }
    // Flipped to 'loading' synchronously — BEFORE the debounce window — so
    // Save is already disabled during the 400 ms in which no request has even
    // been issued yet. Setting it inside the pipe would leave a gap where the
    // panel still shows the PREVIOUS date's resolved counts and Save is live.
    this.previewState = 'loading';
    this.dateChanged$.next(date);
  }

  hide() {
    this.dialogRef.close();
  }

  submit() {
    if (!this.valid) {
      return;
    }
    this.taskListService
      // Non-null asserted: `valid` above is exactly the null guard.
      .changeStartDate({taskIds: this.taskIds, startDate: toDateOnlyString(this.startDate!)})
      .subscribe(res => {
        if (res && res.success) {
          this.dialogRef.close(true);
        }
      });
  }
}

/**
 * Date-only "yyyy-MM-dd" via LOCAL getters (not `toISOString()`), so the picked
 * calendar day survives a UTC offset unchanged — same reasoning as
 * BatchCopyModalComponent.submit and TaskCreateEditModalComponent.onSave.
 * Getting this wrong here would be worse than elsewhere: a one-day slip on a
 * past re-anchor changes which occurrences get backfilled.
 */
function toDateOnlyString(d: Date): string {
  return `${d.getFullYear()}-${(d.getMonth() + 1).toString().padStart(2, '0')}-${d.getDate().toString().padStart(2, '0')}`;
}
