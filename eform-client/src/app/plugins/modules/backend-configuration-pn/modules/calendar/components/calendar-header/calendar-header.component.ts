import {Component, EventEmitter, Input, OnInit, Output} from '@angular/core';
import {TranslateService} from '@ngx-translate/core';
import {CommonDictionaryModel} from 'src/app/common/models';
import {boardTextColor, CalendarBoardModel, CALENDAR_COLORS} from '../../../../models/calendar';
import {getCurrentLocale} from '../../services/calendar-locale.helper';

@Component({
  standalone: false,
  selector: 'app-calendar-header',
  templateUrl: './calendar-header.component.html',
  styleUrls: ['./calendar-header.component.scss'],
})
export class CalendarHeaderComponent implements OnInit {
  @Input() currentDate: string = '';
  @Input() viewMode: 'week' | 'day' | 'schedule' | 'month' = 'week';
  @Input() propertyName: string = '';
  @Input() scheduleScope: 'week' | 'month' = 'week';
  @Input() properties: CommonDictionaryModel[] = [];
  @Input() selectedPropertyId: number | null = null;
  @Input() boards: CalendarBoardModel[] = [];
  @Input() activeBoardIds: number[] = [];

  viewModeOptions: {value: string; label: string}[] = [];

  @Output() navigate = new EventEmitter<-1 | 1>();
  @Output() goToToday = new EventEmitter<void>();
  @Output() viewModeChange = new EventEmitter<'week' | 'day' | 'schedule' | 'month'>();
  @Output() propertySelected = new EventEmitter<number>();
  @Output() boardToggled = new EventEmitter<number>();
  @Output() selectAllBoards = new EventEmitter<void>();
  @Output() clearBoards = new EventEmitter<void>();
  @Output() createBoard = new EventEmitter<void>();
  @Output() updateBoard = new EventEmitter<{id: number; name: string; color: string}>();
  @Output() deleteBoard = new EventEmitter<CalendarBoardModel>();

  readonly boardTextColor = boardTextColor;
  readonly boardColors = CALENDAR_COLORS;

  // Inline calendar rename / recolour, carried over from the retired sidebar's
  // row popover. #1210 replaces it with a modal and a duplicate-name guard.
  editingBoardId: number | null = null;
  editingBoardName = '';
  editingBoardColor = '';

  constructor(private translate: TranslateService) {}

  isBoardActive(boardId: number): boolean {
    return this.activeBoardIds.includes(boardId);
  }

  startEditBoard(board: CalendarBoardModel) {
    this.editingBoardId = board.id;
    this.editingBoardName = board.name;
    this.editingBoardColor = board.color;
  }

  submitEditBoard() {
    if (this.editingBoardId !== null && this.editingBoardName.trim()) {
      this.updateBoard.emit({id: this.editingBoardId, name: this.editingBoardName.trim(), color: this.editingBoardColor});
    }
    this.editingBoardId = null;
  }

  // The calendars button label, in the mock-up's three forms: the single
  // selected calendar's name, "All calendars" when every one is checked, and
  // "N calendars" in between. Counting is done over `boards` so an id left in
  // the filter for a calendar this property does not have cannot inflate it.
  //
  // The EMPTY selection reads "All calendars" too, and that is not a slip:
  // GetTasksForWeek only narrows when the filter is non-empty
  // (`if (filter.BoardIds is { Count: > 0 } ...)` in
  // BackendConfigurationCalendarService.cs), so an empty set is "no filter"
  // and renders exactly the grid every-calendar-checked renders. A label
  // reading "select a calendar" over a grid showing all of them would be a
  // lie. There is no selection that empties the grid, so no such branch
  // exists.
  get boardsLabel(): string {
    const selected = this.boards.filter(b => this.activeBoardIds.includes(b.id));
    if (selected.length === 1) return selected[0].name;
    if (selected.length === 0 || selected.length === this.boards.length) {
      return this.translate.instant('All calendars');
    }
    return this.translate.instant('{{count}} calendars', {count: selected.length});
  }

  ngOnInit() {
    this.buildViewModeOptions();
  }

  private buildViewModeOptions() {
    this.viewModeOptions = [
      {value: 'day', label: this.translate.instant('Day')},
      {value: 'week', label: this.translate.instant('Week')},
      {value: 'month', label: this.translate.instant('Month')},
      {value: 'schedule', label: this.translate.instant('List')},
    ];
  }

  get displayDate(): string {
    if (!this.currentDate) return '';
    const d = new Date(this.currentDate);
    const locale = getCurrentLocale(this.translate);
    // Month view (and month-scoped Tidsplan) titles the whole month:
    // "juli 2026" — locale casing kept, matching the week title style.
    if (this.viewMode === 'month' || (this.viewMode === 'schedule' && this.scheduleScope === 'month')) {
      return d.toLocaleDateString(locale, {month: 'long', year: 'numeric'});
    }
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
    if (this.viewMode === 'day') return 'Previous day';
    if (this.viewMode === 'month' || (this.viewMode === 'schedule' && this.scheduleScope === 'month')) return 'Previous month';
    return 'Previous week';
  }

  get nextTooltipKey(): string {
    if (this.viewMode === 'day') return 'Next day';
    if (this.viewMode === 'month' || (this.viewMode === 'schedule' && this.scheduleScope === 'month')) return 'Next month';
    return 'Next week';
  }

  private getMondayOfWeek(d: Date): Date {
    const date = new Date(d);
    const day = date.getDay();
    date.setDate(date.getDate() + (day === 0 ? -6 : 1 - day));
    return date;
  }
}
