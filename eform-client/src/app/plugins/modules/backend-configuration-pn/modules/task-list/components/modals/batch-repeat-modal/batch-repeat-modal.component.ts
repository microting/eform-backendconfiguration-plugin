import {Component, Inject} from '@angular/core';
import {
  MAT_DIALOG_DATA, MatDialog, MatDialogRef, MatDialogTitle, MatDialogContent, MatDialogActions,
} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {BackendConfigurationPnTaskListService} from '../../../../../services';
import {TaskListBatchModalData} from '../../task-list-page/task-list-page.component';
import {CalendarRepeatMeta} from '../../../../../models/calendar';
import {CalendarRepeatService, toDateOnlyString} from '../../../../calendar/services/calendar-repeat.service';
import {getCurrentLocale} from '../../../../calendar/services/calendar-locale.helper';
import {CustomRepeatModalComponent} from '../../../../calendar/modals/custom-repeat-modal/custom-repeat-modal.component';
import {NgIf, NgFor} from '@angular/common';
import {FormsModule} from '@angular/forms';
import {TranslatePipe, TranslateService} from '@ngx-translate/core';
import {MatFormField, MatLabel, MatSuffix} from '@angular/material/form-field';
import {MatInput} from '@angular/material/input';
import {MatDatepicker, MatDatepickerInput, MatDatepickerToggle} from '@angular/material/datepicker';

/**
 * Batch "Sæt frekvens" modal — one new start date plus one custom repeat rule
 * for every selected task.
 *
 * The rule is picked in the calendar's own "Brugerdefineret gentagelse" dialog
 * (CustomRepeatModalComponent), so the batch offers exactly the options of the
 * single-task edit modal, end condition ("Slutter") included. Like the edit
 * modal, a single-anchor rule (e.g. "1st Friday") follows the start date when
 * the date is changed after the rule was chosen. Monthly and yearly rules also
 * follow it when the rule is chosen after the date, because the server takes
 * their day of month, or weekday and ordinal (#1289), from the start date: the
 * label must show the rule that is saved.
 *
 * Nothing is pre-filled, for the same reason as BatchStartDateModalComponent:
 * Save stays disabled until both a date and a rule are actively chosen.
 */
@Component({
  selector: 'app-batch-repeat-modal',
  imports: [NgIf, NgFor, FormsModule, TranslatePipe, MatFormField, MatLabel, MatSuffix, MatInput, MatDialogTitle, MatDialogContent, MatDialogActions, MatDatepicker, MatDatepickerInput, MatDatepickerToggle],
  templateUrl: './batch-repeat-modal.component.html',
})
export class BatchRepeatModalComponent {
  startDate: Date | null = null;
  // Rules whose anchor (day of month, or weekday and ordinal) the server takes
  // from the start date.
  private static readonly dateAnchoredKinds = new Set([
    'monthlyByDay', 'everyNMonthByDay', 'monthlyFirstWeekday', 'everyNMonthFirstWeekday',
    'monthlyDom', 'everyNMonthDom', 'yearlyOne', 'everyNYear',
  ]);

  repeatMeta: CalendarRepeatMeta | null = null;

  constructor(
    public dialogRef: MatDialogRef<BatchRepeatModalComponent>,
    @Inject(MAT_DIALOG_DATA) public data: TaskListBatchModalData,
    private taskListService: BackendConfigurationPnTaskListService,
    private repeatService: CalendarRepeatService,
    private translate: TranslateService,
    private dialog: MatDialog,
    private overlay: Overlay,
  ) {}

  get valid(): boolean {
    return this.startDate != null && this.repeatMeta != null;
  }

  get repeatLabel(): string {
    return this.repeatMeta
      ? this.repeatService.formatCustomRepeatLabel(this.repeatMeta, getCurrentLocale(this.translate))
      : '';
  }

  onDateChange(date: Date | null) {
    if (date && this.repeatMeta) {
      this.repeatMeta = this.anchorToDate(this.repeatMeta, date);
    }
  }

  // reanchorMetaToDate, with a monthly day capped at 28 the way the server
  // derives it (DeriveDayOfMonth), so a start date on the 29th-31st is not
  // labelled with a day that is never saved.
  private anchorToDate(meta: CalendarRepeatMeta, date: Date): CalendarRepeatMeta {
    const anchored = this.repeatService.reanchorMetaToDate(meta, date);
    const monthlyDom = anchored.kind === 'monthlyDom' || anchored.kind === 'everyNMonthDom';
    return monthlyDom && anchored.dom != null && anchored.dom > 28 ? {...anchored, dom: 28} : anchored;
  }

  openCustomRepeat() {
    const ref = this.dialog.open(
      CustomRepeatModalComponent,
      dialogConfigHelper(this.overlay, {
        date: this.startDate ?? new Date(),
        meta: this.repeatMeta ? toDialogMeta(this.repeatMeta) : null,
      }),
    );
    ref.afterClosed().subscribe((meta: CalendarRepeatMeta | null) => {
      // Cancelled keeps the rule chosen before.
      if (!meta) {
        return;
      }
      this.repeatMeta = this.startDate && BatchRepeatModalComponent.dateAnchoredKinds.has(meta.kind)
        ? this.anchorToDate(meta, this.startDate)
        : meta;
    });
  }

  hide() {
    this.dialogRef.close();
  }

  submit() {
    if (!this.valid) {
      return;
    }
    const taskIds = this.data.selectedTasks.map(t => t.id);
    this.taskListService
      // Non-null asserted: `valid` above is exactly the null guard.
      .changeRepeat({
        taskIds,
        // Date-only local Y-M-D, so the picked day survives a UTC offset (#966).
        startDate: toDateOnlyString(this.startDate!),
        ...this.repeatService.metaToRepeatFields(this.repeatMeta!),
      })
      .subscribe(res => {
        if (res && res.success) {
          this.dialogRef.close(true);
        }
      });
  }
}

/**
 * The custom-repeat dialog only knows the "first weekday" monthly rule: it
 * hydrates an Nth-weekday kind (what re-anchoring to a date in a later week
 * produces) as "day of month", so confirming it unedited would silently turn
 * the rule into day 1. Hand it the first-weekday kind instead; the
 * re-anchoring after the dialog closes restores the ordinal from the date.
 */
function toDialogMeta(meta: CalendarRepeatMeta): CalendarRepeatMeta {
  switch (meta.kind) {
    case 'monthlyByDay':
      return {...meta, kind: 'monthlyFirstWeekday', ordinalWeek: 1};
    case 'everyNMonthByDay':
      return {...meta, kind: 'everyNMonthFirstWeekday', ordinalWeek: 1};
    default:
      return meta;
  }
}
