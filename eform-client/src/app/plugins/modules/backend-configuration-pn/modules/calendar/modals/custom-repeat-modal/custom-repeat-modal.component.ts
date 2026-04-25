import {Component, Inject, OnInit} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {TranslateService} from '@ngx-translate/core';
import {CalendarRepeatService} from '../../services/calendar-repeat.service';
import {CalendarRepeatMeta} from '../../../../models/calendar';
import {getCurrentLocale} from '../../services/calendar-locale.helper';

export interface CustomRepeatModalData {
  date: Date;
}

interface WeekdayCircle {
  label: string;
  value: number;
  active: boolean;
}

@Component({
  standalone: false,
  selector: 'app-custom-repeat-modal',
  templateUrl: './custom-repeat-modal.component.html',
  styleUrls: ['./custom-repeat-modal.component.scss'],
})
export class CustomRepeatModalComponent implements OnInit {
  step = 1;
  unit: 'day' | 'week' | 'month' | 'year' = 'week';
  endMode: 'never' | 'after' | 'until' = 'never';
  afterCount = 10;
  untilDate: string = '';
  untilDateObj: Date | null = null;
  showMiniPicker = false;

  unitOptions: {value: string; label: string}[] = [];

  weekdays: WeekdayCircle[] = [];

  constructor(
    private dialogRef: MatDialogRef<CustomRepeatModalComponent>,
    @Inject(MAT_DIALOG_DATA) public data: CustomRepeatModalData,
    private repeatService: CalendarRepeatService,
    private translate: TranslateService,
  ) {}

  ngOnInit() {
    this.unitOptions = [
      {value: 'day', label: this.translate.instant('day')},
      {value: 'week', label: this.translate.instant('week')},
      {value: 'month', label: this.translate.instant('month')},
      {value: 'year', label: this.translate.instant('year')},
    ];
    this.weekdays = [
      {label: this.translate.instant('Mon').charAt(0), value: 1, active: false},
      {label: this.translate.instant('Tue').charAt(0), value: 2, active: false},
      {label: this.translate.instant('Wed').charAt(0), value: 3, active: false},
      {label: this.translate.instant('Thu').charAt(0), value: 4, active: false},
      {label: this.translate.instant('Fri').charAt(0), value: 5, active: false},
      {label: this.translate.instant('Sat').charAt(0), value: 6, active: false},
      {label: this.translate.instant('Sun').charAt(0), value: 0, active: false},
    ];
    // Pre-select the weekday matching the task date
    const wdVal = this.data.date.getDay();
    const circle = this.weekdays.find(w => w.value === wdVal);
    if (circle) circle.active = true;
    this.untilDateObj = new Date(this.data.date.getFullYear(), this.data.date.getMonth() + 3, this.data.date.getDate());
    this.untilDate = this.untilDateObj.toISOString().split('T')[0];
  }

  toggleWeekday(circle: WeekdayCircle) {
    circle.active = !circle.active;
  }

  get activeWeekdays(): number[] {
    return this.weekdays.filter(w => w.active).map(w => w.value);
  }

  onConfirm() {
    const untilTs = this.endMode === 'until' && this.untilDateObj
      ? this.untilDateObj.getTime()
      : undefined;

    const meta: CalendarRepeatMeta = this.repeatService.buildMetaFromCustomConfig(
      this.step,
      this.unit,
      this.activeWeekdays,
      this.endMode,
      this.endMode === 'after' ? this.afterCount : undefined,
      untilTs,
    );
    this.dialogRef.close(meta);
  }

  onCancel() {
    this.dialogRef.close(null);
  }

  get formattedUntilDate(): string {
    if (!this.untilDateObj) return '';
    const formatted = this.untilDateObj.toLocaleDateString(getCurrentLocale(this.translate), {weekday: 'long', day: 'numeric', month: 'long'});
    return formatted.charAt(0).toUpperCase() + formatted.slice(1);
  }

  onMiniDateSelected(date: Date) {
    this.untilDateObj = date;
    this.untilDate = date.toISOString().split('T')[0];
    this.showMiniPicker = false;
  }
}
