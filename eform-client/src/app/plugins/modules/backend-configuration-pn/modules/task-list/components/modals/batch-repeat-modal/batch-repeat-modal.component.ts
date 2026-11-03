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
 * the date is changed after the rule was chosen. An Nth-weekday rule also
 * follows it when the rule is chosen after the date, because the server takes
 * its weekday and ordinal from the start date (#1289): the label must show the
 * rule that is saved.
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
  // Rules whose weekday and ordinal the server takes from the start date.
  private static readonly nthWeekdayKinds = new Set([
    'monthlyByDay', 'everyNMonthByDay', 'monthlyFirstWeekday', 'everyNMonthFirstWeekday',
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
      this.repeatMeta = this.repeatService.reanchorMetaToDate(this.repeatMeta, date);
    }
  }

  openCustomRepeat() {
    const ref = this.dialog.open(
      CustomRepeatModalComponent,
      dialogConfigHelper(this.overlay, {
        date: this.startDate ?? new Date(),
        meta: this.repeatMeta,
      }),
    );
    ref.afterClosed().subscribe((meta: CalendarRepeatMeta | null) => {
      // Cancelled keeps the rule chosen before.
      if (!meta) {
        return;
      }
      this.repeatMeta = this.startDate && BatchRepeatModalComponent.nthWeekdayKinds.has(meta.kind)
        ? this.repeatService.reanchorMetaToDate(meta, this.startDate)
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
