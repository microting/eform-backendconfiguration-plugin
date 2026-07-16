import {Component, EventEmitter, Input, OnChanges, OnInit, Output, SimpleChanges} from '@angular/core';
import {TranslateService} from '@ngx-translate/core';
import {getCurrentLocale} from '../../services/calendar-locale.helper';

@Component({
  standalone: false,
  selector: 'app-calendar-header',
  templateUrl: './calendar-header.component.html',
  styleUrls: ['./calendar-header.component.scss'],
})
export class CalendarHeaderComponent implements OnInit, OnChanges {
  @Input() currentDate: string = '';
  @Input() viewMode: 'week' | 'day' | 'schedule' | 'compliance' = 'week';
  @Input() sidebarOpen = true;
  @Input() propertyName: string = '';
  @Input() isAdmin = false;

  viewModeOptions: {value: string; label: string}[] = [];

  @Output() navigate = new EventEmitter<-1 | 1>();
  @Output() goToToday = new EventEmitter<void>();
  @Output() viewModeChange = new EventEmitter<'week' | 'day' | 'schedule' | 'compliance'>();
  @Output() toggleSidebar = new EventEmitter<void>();
  @Output() propertyPillClicked = new EventEmitter<void>();

  constructor(private translate: TranslateService) {}

  ngOnInit() {
    this.buildViewModeOptions();
  }

  ngOnChanges(changes: SimpleChanges) {
    // isAdmin arrives asynchronously from the store after this component has
    // already initialised, so the option list must be rebuilt whenever it
    // changes (not just once in ngOnInit).
    if (changes['isAdmin']) {
      this.buildViewModeOptions();
    }
  }

  private buildViewModeOptions() {
    this.viewModeOptions = [
      {value: 'day', label: this.translate.instant('Day')},
      {value: 'week', label: this.translate.instant('Week')},
      {value: 'schedule', label: this.translate.instant('List')},
      ...(this.isAdmin ? [{value: 'compliance', label: this.translate.instant('Compliance')}] : []),
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
    // Week view: month(s) of the week's Monday and Sunday, rendered exactly
    // as the locale produces them — Danish stays lowercase ("juli 2026").
    // A week straddling a month border names both months with a spaced
    // hyphen ("juni - juli 2026"); a year border keeps each month's own
    // year ("december 2026 - januar 2027"). See
    // 2026-07-15-calendar-week-title-two-months-design.md.
    const monday = this.getMondayOfWeek(d);
    const sunday = new Date(monday);
    sunday.setDate(monday.getDate() + 6);
    const m1 = monday.toLocaleDateString(locale, {month: 'long'});
    const m2 = sunday.toLocaleDateString(locale, {month: 'long'});
    const y1 = monday.getFullYear();
    const y2 = sunday.getFullYear();
    if (m1 === m2 && y1 === y2) return `${m1} ${y1}`;
    if (y1 === y2) return `${m1} - ${m2} ${y1}`;
    return `${m1} ${y1} - ${m2} ${y2}`;
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
    return this.viewMode === 'day' ? 'Previous day' : 'Previous week';
  }

  get nextTooltipKey(): string {
    return this.viewMode === 'day' ? 'Next day' : 'Next week';
  }

  private getMondayOfWeek(d: Date): Date {
    const date = new Date(d);
    const day = date.getDay();
    date.setDate(date.getDate() + (day === 0 ? -6 : 1 - day));
    return date;
  }
}
