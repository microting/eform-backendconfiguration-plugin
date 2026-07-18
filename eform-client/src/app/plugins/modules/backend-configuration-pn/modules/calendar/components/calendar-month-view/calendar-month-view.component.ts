import {Component, EventEmitter, Input, OnChanges, Output} from '@angular/core';
import {TranslateService} from '@ngx-translate/core';
import {boardTextColor, CalendarTaskLayoutModel} from '../../../../models/calendar';
import {getCurrentLocale} from '../../services/calendar-locale.helper';

interface MonthDay {
  dateIso: string;
  dayNum: number;
  inCurrentMonth: boolean;
  isToday: boolean;
  visibleTasks: CalendarTaskLayoutModel[];
  overflow: number;
}

interface MonthWeek {
  weekNumber: number;
  mondayIso: string;
  days: MonthDay[];
}

// Read-only Google-style month grid: 6 Mon–Sun rows with an ISO week-number
// gutter. Task chips are deliberately passive (spec: "KUN EN VISNING") — the
// only interactions are the week/day/Tidsplan click-throughs.
@Component({
  standalone: false,
  selector: 'app-calendar-month-view',
  templateUrl: './calendar-month-view.component.html',
  styleUrls: ['./calendar-month-view.component.scss'],
})
export class CalendarMonthViewComponent implements OnChanges {
  @Input() monthDate: string = '';
  @Input() tasksByDate: Map<string, CalendarTaskLayoutModel[]> = new Map();

  @Output() weekClicked = new EventEmitter<string>();
  @Output() dayClicked = new EventEmitter<string>();
  @Output() scheduleClicked = new EventEmitter<void>();

  private static readonly CHIP_LIMIT = 3;

  weeks: MonthWeek[] = [];
  dowLabels: string[] = [];
  readonly boardTextColor = boardTextColor;

  constructor(private translate: TranslateService) {}

  ngOnChanges() {
    this.buildGrid();
  }

  private buildGrid() {
    if (!this.monthDate) return;
    const locale = getCurrentLocale(this.translate);
    // Monday-first weekday headers from a known Monday (2024-01-01).
    this.dowLabels = Array.from({length: 7}, (_, i) => {
      const label = new Date(2024, 0, 1 + i).toLocaleDateString(locale, {weekday: 'short'});
      return label.charAt(0).toUpperCase() + label.slice(1);
    });

    const anchor = new Date(this.monthDate);
    const year = anchor.getFullYear();
    const month = anchor.getMonth();
    const today = new Date();
    today.setHours(0, 0, 0, 0);
    const firstDay = new Date(year, month, 1);
    const startOffset = (firstDay.getDay() + 6) % 7; // Mon=0

    const days: MonthDay[] = [];
    for (let i = -startOffset; i < 42 - startOffset; i++) {
      const date = new Date(year, month, 1 + i);
      date.setHours(0, 0, 0, 0);
      const dateIso = this.toLocalDateString(date);
      const tasks = this.tasksByDate.get(dateIso) ?? [];
      days.push({
        dateIso,
        dayNum: date.getDate(),
        inCurrentMonth: date.getMonth() === month,
        isToday: date.getTime() === today.getTime(),
        visibleTasks: tasks.slice(0, CalendarMonthViewComponent.CHIP_LIMIT),
        overflow: Math.max(0, tasks.length - CalendarMonthViewComponent.CHIP_LIMIT),
      });
    }

    this.weeks = [];
    for (let i = 0; i < 6; i++) {
      const weekDays = days.slice(i * 7, i * 7 + 7);
      this.weeks.push({
        weekNumber: this.getIsoWeek(new Date(weekDays[0].dateIso)),
        mondayIso: weekDays[0].dateIso,
        days: weekDays,
      });
    }
  }

  // ISO 8601 week number — same math as calendar-mini-calendar.component.ts.
  private getIsoWeek(d: Date): number {
    const date = new Date(Date.UTC(d.getFullYear(), d.getMonth(), d.getDate()));
    const dayNum = date.getUTCDay() || 7;
    date.setUTCDate(date.getUTCDate() + 4 - dayNum);
    const yearStart = new Date(Date.UTC(date.getUTCFullYear(), 0, 1));
    return Math.ceil((((date.getTime() - yearStart.getTime()) / 86400000) + 1) / 7);
  }

  private toLocalDateString(d: Date): string {
    return `${d.getFullYear()}-${(d.getMonth() + 1).toString().padStart(2, '0')}-${d.getDate().toString().padStart(2, '0')}`;
  }
}
