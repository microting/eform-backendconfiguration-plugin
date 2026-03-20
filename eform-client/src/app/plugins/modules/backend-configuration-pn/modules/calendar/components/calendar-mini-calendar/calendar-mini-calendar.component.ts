import {Component, EventEmitter, Input, OnInit, Output} from '@angular/core';

interface CalendarDay {
  date: Date;
  dayNum: number;
  inCurrentMonth: boolean;
  isToday: boolean;
  isSelected: boolean;
}

@Component({
  standalone: false,
  selector: 'app-calendar-mini-calendar',
  templateUrl: './calendar-mini-calendar.component.html',
  styleUrls: ['./calendar-mini-calendar.component.scss'],
})
export class CalendarMiniCalendarComponent implements OnInit {
  @Input() selectedDate: Date | null = null;
  @Output() dateSelected = new EventEmitter<Date>();

  displayMonth!: Date;
  weeks: CalendarDay[][] = [];
  readonly dayHeaders = ['Ma', 'Ti', 'On', 'To', 'Fr', 'Lø', 'Sø'];

  ngOnInit() {
    this.displayMonth = this.selectedDate
      ? new Date(this.selectedDate.getFullYear(), this.selectedDate.getMonth(), 1)
      : new Date(new Date().getFullYear(), new Date().getMonth(), 1);
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
    this.selectedDate = day.date;
    this.buildCalendar();
    this.dateSelected.emit(day.date);
  }

  get monthLabel(): string {
    return this.displayMonth.toLocaleDateString('da-DK', {month: 'long', year: 'numeric'});
  }

  private buildCalendar() {
    const year = this.displayMonth.getFullYear();
    const month = this.displayMonth.getMonth();
    const today = new Date();
    today.setHours(0, 0, 0, 0);

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
      });
    }

    this.weeks = [];
    for (let i = 0; i < 6; i++) {
      this.weeks.push(days.slice(i * 7, i * 7 + 7));
    }
  }
}
