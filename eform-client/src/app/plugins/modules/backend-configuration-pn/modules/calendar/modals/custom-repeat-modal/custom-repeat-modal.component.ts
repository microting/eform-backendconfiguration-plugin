import {Component, Inject, OnInit} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {TranslateService} from '@ngx-translate/core';
import {CalendarRepeatService} from '../../services/calendar-repeat.service';
import {CalendarRepeatMeta} from '../../../../models/calendar';
import {getCurrentLocale} from '../../services/calendar-locale.helper';

export interface CustomRepeatModalData {
  date: Date;
  meta?: CalendarRepeatMeta | null;
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

  monthlyKind: 'everyNMonthDom' | 'monthlyFirstWeekday' = 'everyNMonthDom';
  monthlyDom = 1;
  monthlyWeekday = 1;

  readonly monthlyDomOptions: {value: number; label: string}[] =
    Array.from({length: 28}, (_, i) => ({value: i + 1, label: String(i + 1)}));

  monthlyKindOptions: {value: string; label: string}[] = [];
  monthlyWeekdayOptions: {value: number; label: string}[] = [];

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
    // Uppercase the initial — several locales translate the short weekday
    // names in lowercase (da: 'man' → 'm'), and the circles must read
    // "M T O T F L S", not "m T O T F L S".
    const dayInitial = (key: string) => this.translate.instant(key).charAt(0).toUpperCase();
    this.weekdays = [
      {label: dayInitial('Mon'), value: 1, active: false},
      {label: dayInitial('Tue'), value: 2, active: false},
      {label: dayInitial('Wed'), value: 3, active: false},
      {label: dayInitial('Thu'), value: 4, active: false},
      {label: dayInitial('Fri'), value: 5, active: false},
      {label: dayInitial('Sat'), value: 6, active: false},
      {label: dayInitial('Sun'), value: 0, active: false},
    ];

    this.monthlyKindOptions = [
      {value: 'everyNMonthDom', label: this.translate.instant('Monthly day')},
      {value: 'monthlyFirstWeekday', label: this.translate.instant('Monthly on the first')},
    ];

    // Generate full weekday names in the current locale (Mon=1 … Sun=0, order Mon–Sun).
    this.monthlyWeekdayOptions = [1, 2, 3, 4, 5, 6, 0].map(v => {
      // Jan 6 2025 = Monday; offsets give Mon(1)..Sat(6) at Jan 6-11, Sun(0) at Jan 12.
      const d = new Date(2025, 0, v === 0 ? 12 : 5 + v);
      const name = d.toLocaleDateString(getCurrentLocale(this.translate), {weekday: 'long'});
      return {value: v, label: name.charAt(0).toUpperCase() + name.slice(1)};
    });

    // Always seed a sensible untilDate fallback (date + 3 months) so the "På"
    // branch has a value if the user toggles to it later — even when we
    // hydrate from an existing meta whose endMode is not 'until'.
    const fallbackUntil = new Date(
      this.data.date.getFullYear(),
      this.data.date.getMonth() + 3,
      this.data.date.getDate(),
    );
    this.untilDateObj = fallbackUntil;
    this.untilDate = fallbackUntil.toISOString().split('T')[0];

    if (this.data.meta) {
      // Hydrate from an existing custom-repeat rule.
      const decomposed = this.repeatService.decomposeCustomMeta(this.data.meta);
      this.step = decomposed.step;
      this.unit = decomposed.unit;
      this.endMode = decomposed.endMode;
      if (decomposed.afterCount != null) {
        this.afterCount = decomposed.afterCount;
      }
      // Sunday is value: 0 here and JS getDay() returns 0 too, so includes(0)
      // matches correctly without special-casing.
      for (const circle of this.weekdays) {
        circle.active = decomposed.weekdays.includes(circle.value);
      }
      if (decomposed.untilTs != null) {
        this.untilDateObj = new Date(decomposed.untilTs);
        this.untilDate = this.untilDateObj.toISOString().split('T')[0];
      }
      if (decomposed.monthlyKind) {
        this.monthlyKind = decomposed.monthlyKind;
      }
      if (decomposed.dom != null) {
        this.monthlyDom = decomposed.dom;
      }
      if (decomposed.monthlyWeekday != null) {
        this.monthlyWeekday = decomposed.monthlyWeekday;
      }
    } else {
      // Fresh open: pre-select the weekday matching the task date.
      const wdVal = this.data.date.getDay();
      const circle = this.weekdays.find(w => w.value === wdVal);
      if (circle) circle.active = true;

      // Default monthlyDom to 1 (the MONTH unit hard-codes day=1 per the
      // spec — the user selects day-of-month from the picker, but the initial
      // default is always 1, not the start-date's day).
      this.monthlyDom = 1;
      this.monthlyWeekday = this.data.date.getDay();
    }
  }

  toggleWeekday(circle: WeekdayCircle) {
    circle.active = !circle.active;
  }

  onUnitChange(val: 'day' | 'week' | 'month' | 'year') {
    this.unit = val;
    if (val !== 'month') {
      this.monthlyKind = 'everyNMonthDom';
      this.monthlyDom = 1;
      this.monthlyWeekday = this.data.date.getDay();
    }
  }

  get activeWeekdays(): number[] {
    return this.weekdays.filter(w => w.active).map(w => w.value);
  }

  onConfirm() {
    // Enforce the min=1 step constraint on the model. The input's min="1" is
    // advisory only — a typed 0 (or blank/negative) otherwise ships
    // repeatEvery=0, which the backend treats as an "always" event (#922).
    this.step = Math.max(1, Math.floor(this.step) || 1);

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
      this.data.date,
      this.unit === 'month' ? this.monthlyKind : undefined,
      this.unit === 'month' ? this.monthlyDom : undefined,
      this.unit === 'month' ? this.monthlyWeekday : undefined,
    );
    this.dialogRef.close(meta);
  }

  onCancel() {
    this.dialogRef.close(null);
  }

  get formattedUntilDate(): string {
    if (!this.untilDateObj) return '';
    const formatted = this.untilDateObj.toLocaleDateString(getCurrentLocale(this.translate),
      {weekday: 'long', day: 'numeric', month: 'long', year: 'numeric'});
    return formatted.charAt(0).toUpperCase() + formatted.slice(1);
  }

  onMiniDateSelected(date: Date) {
    this.untilDateObj = date;
    this.untilDate = date.toISOString().split('T')[0];
    this.showMiniPicker = false;
  }
}
