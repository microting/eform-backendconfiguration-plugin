import {Component, EventEmitter, Input, OnChanges, OnInit, Output, SimpleChanges} from '@angular/core';
import {TranslateService} from '@ngx-translate/core';
import {getCurrentLocale} from '../../services/calendar-locale.helper';

interface CalendarDay {
  date: Date;
  dayNum: number;
  inCurrentMonth: boolean;
  isToday: boolean;
  isSelected: boolean;
  isDisabled: boolean;
}

interface CalendarWeek {
  weekNumber: number;
  days: CalendarDay[];
}

@Component({
  standalone: false,
  selector: 'app-calendar-mini-calendar',
  templateUrl: './calendar-mini-calendar.component.html',
  styleUrls: ['./calendar-mini-calendar.component.scss'],
})
export class CalendarMiniCalendarComponent implements OnInit, OnChanges {
  @Input() selectedDate: Date | null = null;
  @Input() minDate: Date | null = null;
  @Output() dateSelected = new EventEmitter<Date>();

  displayMonth!: Date;
  weeks: CalendarWeek[] = [];
  dayHeaders: string[] = [];

  constructor(private translate: TranslateService) {}

  ngOnInit() {
    this.dayHeaders = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun']
      .map(k => this.translate.instant(k).charAt(0).toUpperCase());
    this.displayMonth = this.selectedDate
      ? new Date(this.selectedDate.getFullYear(), this.selectedDate.getMonth(), 1)
      : new Date(new Date().getFullYear(), new Date().getMonth(), 1);
    this.buildCalendar();
  }

  ngOnChanges(changes: SimpleChanges) {
    if (this.weeks.length === 0) return;
    if (changes['selectedDate'] && this.selectedDate) {
      this.displayMonth = new Date(this.selectedDate.getFullYear(), this.selectedDate.getMonth(), 1);
    }
    this.buildCalendar();
  }

  navigateMonth(dir: -1 | 1) {
    this.displayMonth = new Date(
      this.displayMonth.getFullYear(),
      this.displayMonth.getMonth() + dir,
      1
    );
    this.buildCalendar();
  }

  selectDate(day: CalendarDay) {
    if (day.isDisabled) return;
    this.selectedDate = day.date;
    this.buildCalendar();
    this.dateSelected.emit(day.date);
  }

  get monthLabel(): string {
    return this.displayMonth.toLocaleDateString(getCurrentLocale(this.translate), {month: 'long', year: 'numeric'});
  }

  private buildCalendar() {
    const year = this.displayMonth.getFullYear();
    const month = this.displayMonth.getMonth();
    const today = new Date();
    today.setHours(0, 0, 0, 0);
    const minDateNorm = this.minDate ? new Date(this.minDate.getFullYear(), this.minDate.getMonth(), this.minDate.getDate()).getTime() : null;

    const firstDay = new Date(year, month, 1);
    // Monday-based: getDay() 0=Sun, 1=Mon … shift so Mon=0
    const startOffset = (firstDay.getDay() + 6) % 7;

    const days: CalendarDay[] = [];
    for (let i = -startOffset; i < 42 - startOffset; i++) {
      const date = new Date(year, month, 1 + i);
      date.setHours(0, 0, 0, 0);
      days.push({
        date,
        dayNum: date.getDate(),
        inCurrentMonth: date.getMonth() === month,
        isToday: date.getTime() === today.getTime(),
        isSelected: this.selectedDate
          ? new Date(this.selectedDate).setHours(0, 0, 0, 0) === date.getTime()
          : false,
        isDisabled: minDateNorm !== null && date.getTime() < minDateNorm,
      });
    }

    this.weeks = [];
    for (let i = 0; i < 6; i++) {
      const weekDays = days.slice(i * 7, i * 7 + 7);
      this.weeks.push({
        weekNumber: this.getIsoWeek(weekDays[0].date),
        days: weekDays,
      });
    }
  }

  // ISO 8601 week: anchor on the Thursday of the same week, then count
  // whole weeks since the first Thursday of the ISO year.
  private getIsoWeek(d: Date): number {
    const date = new Date(Date.UTC(d.getFullYear(), d.getMonth(), d.getDate()));
    const dayNum = date.getUTCDay() || 7;
    date.setUTCDate(date.getUTCDate() + 4 - dayNum);
    const yearStart = new Date(Date.UTC(date.getUTCFullYear(), 0, 1));
    return Math.ceil((((date.getTime() - yearStart.getTime()) / 86400000) + 1) / 7);
  }
}
