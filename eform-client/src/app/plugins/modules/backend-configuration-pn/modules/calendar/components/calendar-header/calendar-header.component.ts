import {Component, EventEmitter, Input, OnInit, Output} from '@angular/core';
import {TranslateService} from '@ngx-translate/core';
import {CommonDictionaryModel} from 'src/app/common/models';
import {boardTextColor, CalendarBoardModel} from '../../../../models/calendar';
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
  // #1211 assignee filter. `teams` are SDK worker tags that have at least one
  // live member (#1213); `employees` is every worker linked to the selected
  // property. Two peer lists, never synced to each other — see
  // `assigneesLabel` and the container's onTeamToggled.
  @Input() teams: CommonDictionaryModel[] = [];
  @Input() employees: CommonDictionaryModel[] = [];
  @Input() activeSiteIds: number[] = [];
  @Input() activeTeamIds: number[] = [];

  viewModeOptions: {value: string; label: string}[] = [];

  @Output() navigate = new EventEmitter<-1 | 1>();
  @Output() goToToday = new EventEmitter<void>();
  @Output() viewModeChange = new EventEmitter<'week' | 'day' | 'schedule' | 'month'>();
  @Output() propertySelected = new EventEmitter<number>();
  @Output() boardToggled = new EventEmitter<number>();
  @Output() selectAllBoards = new EventEmitter<void>();
  @Output() clearBoards = new EventEmitter<void>();
  @Output() createBoard = new EventEmitter<void>();
  // #1210: the row `⋮` menu no longer edits in place. Each action just names
  // the calendar and hands over to the container, which owns the dialogs.
  @Output() editBoard = new EventEmitter<CalendarBoardModel>();
  @Output() duplicateBoard = new EventEmitter<CalendarBoardModel>();
  @Output() deleteBoard = new EventEmitter<CalendarBoardModel>();
  // #1211: one event per toggled entry, and a separate reset. Emitting ids
  // rather than the whole next selection keeps the store the single owner of
  // the lists — the header never has to hold a copy that could drift.
  @Output() employeeToggled = new EventEmitter<number>();
  @Output() teamToggled = new EventEmitter<number>();
  @Output() clearAssignees = new EventEmitter<void>();

  readonly boardTextColor = boardTextColor;

  constructor(private translate: TranslateService) {}

  isBoardActive(boardId: number): boolean {
    return this.activeBoardIds.includes(boardId);
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

  isEmployeeActive(siteId: number): boolean {
    return this.activeSiteIds.includes(siteId);
  }

  isTeamActive(teamId: number): boolean {
    return this.activeTeamIds.includes(teamId);
  }

  // The assignee button label, in the same three forms as `boardsLabel`.
  //
  // Counted over the RAW id arrays, not over the rendered `teams`/`employees`
  // lists. The rendered lists are only a VIEW of the filter: `employees` is
  // property-scoped and loaded by its own request, `teams` is loaded once in
  // ngOnInit, and neither is guaranteed to still contain every active id — a
  // worker tag deleted from Property workers while it sits in `activeTeamIds`
  // is the reachable case. Counting over the rendered rows made such an id
  // vanish from the count, so the button could read "All employees" over a
  // grid the request had genuinely narrowed. The count must describe the
  // FILTER, which is what the server is given.
  //
  // Teams and employees are counted TOGETHER because they are one control and
  // one filter: "2 selected" over a team plus a person is the truth; "1 team,
  // 1 employee" would need a fourth form for no gain.
  //
  // The one-selected form still looks the name up in the rendered lists —
  // that is the only place a name exists. When the single active id resolves
  // to no row (the deleted-tag case above) there is no name to show, so it
  // falls through to the count form, "1 selected": vague, but true, and it
  // keeps the button visibly filtered instead of claiming "All employees".
  //
  // Unlike the calendars label there is no "everything is checked" special
  // case. Checking every calendar and checking none render the same grid, so
  // both read "All calendars"; here, checking every employee is NOT the same
  // as checking none — an unassigned task, or one assigned only through a team
  // the user did not tick, survives the empty filter and not the full one. The
  // empty selection alone is "All employees".
  get assigneesLabel(): string {
    const total = this.activeTeamIds.length + this.activeSiteIds.length;
    if (total === 0) return this.translate.instant('All employees');
    if (total === 1) {
      const named = this.teams.find(t => this.activeTeamIds.includes(t.id))
        ?? this.employees.find(e => this.activeSiteIds.includes(e.id));
      if (named) return named.name;
    }
    return this.translate.instant('{{count}} selected', {count: total});
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
