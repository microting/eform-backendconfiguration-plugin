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

  viewModeOptions: {value: string; label: string}[] = [];

  @Output() navigate = new EventEmitter<-1 | 1>();
  @Output() goToToday = new EventEmitter<void>();
  @Output() viewModeChange = new EventEmitter<'week' | 'day' | 'schedule'>();
  @Output() toggleSidebar = new EventEmitter<void>();

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
    const monday = this.getMondayOfWeek(d);
    const sunday = new Date(monday);
    sunday.setDate(monday.getDate() + 6);
    const fmt = (dt: Date) =>
      dt.toLocaleDateString(getCurrentLocale(this.translate), {day: 'numeric', month: 'long'});
    return `${fmt(monday)} – ${fmt(sunday)} ${monday.getFullYear()}`;
  }

  private getMondayOfWeek(d: Date): Date {
    const date = new Date(d);
    const day = date.getDay();
    date.setDate(date.getDate() + (day === 0 ? -6 : 1 - day));
    return date;
  }
}
