import {Component, EventEmitter, Input, OnInit, Output} from '@angular/core';
import {TranslateService} from '@ngx-translate/core';
import {getCurrentLocale} from '../../services/calendar-locale.helper';

@Component({
  standalone: false,
  selector: 'app-calendar-header',
  templateUrl: './calendar-header.component.html',
  styleUrls: ['./calendar-header.component.scss'],
})
export class CalendarHeaderComponent implements OnInit {
  @Input() currentDate: string = '';
  @Input() viewMode: 'week' | 'day' | 'schedule' = 'week';
  @Input() sidebarOpen = true;
  @Input() propertyName: string = '';

  viewModeOptions: {value: string; label: string}[] = [];

  @Output() navigate = new EventEmitter<-1 | 1>();
  @Output() goToToday = new EventEmitter<void>();
  @Output() viewModeChange = new EventEmitter<'week' | 'day' | 'schedule'>();
  @Output() toggleSidebar = new EventEmitter<void>();
  @Output() propertyPillClicked = new EventEmitter<void>();

  constructor(private translate: TranslateService) {}

  ngOnInit() {
    this.viewModeOptions = [
      {value: 'week', label: this.translate.instant('Week')},
      {value: 'day', label: this.translate.instant('Day')},
      {value: 'schedule', label: this.translate.instant('List')},
    ];
  }

  get displayDate(): string {
    if (!this.currentDate) return '';
    const d = new Date(this.currentDate);
    const locale = getCurrentLocale(this.translate);
    // Day + schedule/list views show a single date in long form, matching
    // the event-modal label style: "Lørdag, 21. april 2026".
    if (this.viewMode === 'day' || this.viewMode === 'schedule') {
      const formatted = d.toLocaleDateString(locale, {
        weekday: 'long',
        day: 'numeric',
        month: 'long',
        year: 'numeric',
      });
      // Capitalize first letter (Danish locale returns "lørdag, …" lowercase).
      return formatted.charAt(0).toUpperCase() + formatted.slice(1);
    }
    // Week view: "Month Year" (e.g. "April 2026").
    const monday = this.getMondayOfWeek(d);
    const formatted = monday.toLocaleDateString(locale, {month: 'long', year: 'numeric'});
    // Capitalize first letter (Danish locale returns "april 2026" lowercase).
    return formatted.charAt(0).toUpperCase() + formatted.slice(1);
  }

  get weekBadge(): string {
    if (this.viewMode !== 'week' || !this.currentDate) return '';
    const monday = this.getMondayOfWeek(new Date(this.currentDate));
    // ISO-8601 week number: anchor on the Thursday of this week, then count
    // whole weeks since the first Monday of the ISO year (the Monday of the
    // week containing Jan 4). UTC keeps the day-diff exact across DST.
    const thursday = new Date(Date.UTC(monday.getFullYear(), monday.getMonth(), monday.getDate() + 3));
    const firstThursday = new Date(Date.UTC(thursday.getUTCFullYear(), 0, 4));
    firstThursday.setUTCDate(firstThursday.getUTCDate() - ((firstThursday.getUTCDay() + 6) % 7));
    const isoWeek = 1 + Math.round((thursday.getTime() - firstThursday.getTime()) / (7 * 86400000));
    const label = this.translate.instant('Week');
    return `${label} ${isoWeek}`;
  }

  get prevTooltipKey(): string {
    return this.viewMode === 'week' ? 'Previous week' : 'Previous day';
  }

  get nextTooltipKey(): string {
    return this.viewMode === 'week' ? 'Next week' : 'Next day';
  }

  private getMondayOfWeek(d: Date): Date {
    const date = new Date(d);
    const day = date.getDay();
    date.setDate(date.getDate() + (day === 0 ? -6 : 1 - day));
    return date;
  }
}
