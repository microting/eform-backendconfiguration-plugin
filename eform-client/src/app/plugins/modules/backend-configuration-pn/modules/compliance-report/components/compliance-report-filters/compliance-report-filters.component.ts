import {Component, EventEmitter, OnDestroy, OnInit, Output} from '@angular/core';
import {Subject} from 'rxjs';
import {distinctUntilChanged, map, takeUntil} from 'rxjs/operators';
import {TranslateService} from '@ngx-translate/core';
import {CommonDictionaryModel, SharedTagModel} from 'src/app/common/models';
import {ItemsPlanningPnTagsService} from 'src/app/plugins/modules/items-planning-pn/services';
import {
  BackendConfigurationPnCalendarService,
  BackendConfigurationPnPropertiesService,
} from '../../../../services';
import {CalendarBoardModel, ComplianceReportStatus} from '../../../../models';
import {getCurrentLocale} from '../../../calendar/services/calendar-locale.helper';
import {
  CompliancePeriodPreset,
  ComplianceReportStateService,
} from '../../store';

export type ComplianceExportFormat = 'pdf' | 'csv' | 'excel';

/**
 * The shared ten-control filter bar of the standalone Compliance page
 * (#1163 §4). Left to right it mirrors the prototype's
 * `Compliance.html:13-54`: property, calendar, tags, status, employee, period,
 * period label, `Opdater tabel`, `Hent som`, `Download`.
 *
 * It owns its own reference data. The calendar container loads these four sets
 * and passes them down as @Inputs; this page has no container above it, so the
 * loaders are copied here (calendar-container.component.ts:202-309) with two
 * deliberate differences: no auto-select of the first property (the page opens
 * on "Alle ejendomme"), and boards/employees reload — and reset to "all" — on
 * every property change.
 *
 * Every user-driven change routes through `ComplianceReportStateService
 * .setFilter()`, i.e. the invalidating path: results blank, pagination clears,
 * nothing is fetched. Only `Opdater tabel` fetches.
 */
@Component({
  standalone: false,
  selector: 'app-compliance-report-filters',
  templateUrl: './compliance-report-filters.component.html',
  styleUrls: ['./compliance-report-filters.component.scss'],
})
export class ComplianceReportFiltersComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();

  /**
   * #1169 wires the actual export here. Until then the button is a control
   * only — it is placed, enabled/disabled correctly, and emits.
   */
  @Output() downloadRequested = new EventEmitter<ComplianceExportFormat>();

  properties: CommonDictionaryModel[] = [];
  boards: CalendarBoardModel[] = [];
  tags: SharedTagModel[] = [];
  employees: CommonDictionaryModel[] = [];

  exportFormat: ComplianceExportFormat | null = null;

  statusOptions: {value: ComplianceReportStatus; label: string}[] = [];
  periodOptions: {value: CompliancePeriodPreset; label: string}[] = [];
  exportOptions: {value: ComplianceExportFormat; label: string}[] = [];

  constructor(
    public state: ComplianceReportStateService,
    private propertiesService: BackendConfigurationPnPropertiesService,
    private calendarService: BackendConfigurationPnCalendarService,
    private tagsService: ItemsPlanningPnTagsService,
    private translate: TranslateService,
  ) {}

  ngOnInit(): void {
    this.statusOptions = [
      {value: 'open', label: this.translate.instant('Not completed tasks')},
      {value: 'done', label: this.translate.instant('Completed tasks')},
      {value: 'all', label: this.translate.instant('All tasks')},
    ];
    this.periodOptions = [
      {value: '1', label: this.translate.instant('1 month')},
      {value: '3', label: this.translate.instant('3 months')},
      {value: '6', label: this.translate.instant('6 months')},
      {value: '12', label: this.translate.instant('12 months')},
      {value: 'ytd', label: this.translate.instant('Year to date')},
      {value: 'custom', label: this.translate.instant('Set period')},
    ];
    // Format names are product names, not translatable strings.
    this.exportOptions = [
      {value: 'pdf', label: 'PDF'},
      {value: 'csv', label: 'CSV'},
      {value: 'excel', label: 'Excel'},
    ];

    this.loadProperties();
    this.loadTags();

    // The property-scoped reference data is driven off the STATE, not off the
    // click handler. Two cases the click handler cannot cover:
    //
    //  - Re-entry. `ComplianceReportStateService` is module-scoped and Angular
    //    caches a lazy module's NgModuleRef for the app's lifetime, so the
    //    filter values survive navigating away and back — but a fresh filters
    //    component starts with `boards = []` while `filters.propertyId` is
    //    still set. The Kalender dropdown would render enabled and EMPTY, a
    //    previously chosen board would show blank, and `requestModel` would
    //    still serialise the stale `boardIds`.
    //  - `drillIntoProperty` (#1164) writes `propertyId` through
    //    `setFilterSilently`, which no `(ngModelChange)` ever sees.
    //
    // `filters$` is a BehaviorSubject, so this fires once immediately with the
    // current property — which is what replaces the direct `loadEmployees()`
    // call that used to live here. `distinctUntilChanged` on `propertyId`
    // keeps every OTHER filter change (tags, status, period, …) from
    // re-fetching the lists.
    //
    // Neither loader touches the report endpoint and neither emits on
    // `fetchRequested$`: reference data is not a fetch.
    this.state.filters$
      .pipe(
        map((filters) => filters.propertyId),
        distinctUntilChanged(),
        takeUntil(this.destroy$)
      )
      .subscribe((propertyId) => {
        this.loadBoards(propertyId);
        this.loadEmployees();
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  // ---------------------------------------------------------------
  // Reference data
  // ---------------------------------------------------------------

  private loadProperties(): void {
    this.propertiesService.getAllPropertiesDictionary().subscribe((res) => {
      if (res && res.success) {
        this.properties = res.model ?? [];
        // Deliberately NOT auto-selecting properties[0]: the calendar does
        // that because it cannot render without a property; this page opens
        // on "Alle ejendomme".
      }
    });
  }

  private loadBoards(propertyId: number | null): void {
    if (propertyId == null) {
      // getBoards is property-scoped and there is no "all properties" shape,
      // so with no property selected the dropdown offers only "Alle kalendere"
      // and is disabled (see boardDisabledHint).
      this.boards = [];
      return;
    }
    this.calendarService.getBoards(propertyId).subscribe((res) => {
      if (res && res.success) {
        this.boards = res.model ?? [];
      }
    });
  }

  /**
   * getPlanningsTags() is installation-global — there is no property filter
   * server-side, so a user filtering property X still sees tags that exist only
   * on property Y and selecting one silently yields zero rows. Carried forward
   * knowingly (#1163 §4.1); narrowing it needs a server change. Do NOT
   * "fix" it by intersecting against loaded rows — the rows are server-paged,
   * so the client never sees the full tag distribution.
   */
  private loadTags(): void {
    this.tagsService.getPlanningsTags().subscribe((res) => {
      if (res && res.success) {
        this.tags = res.model ?? [];
      }
    });
  }

  private loadEmployees(): void {
    const propertyId = this.state.filters.propertyId;
    this.propertiesService
      .getDeviceUsersFiltered({
        propertyIds: propertyId != null ? [propertyId] : [],
        nameFilter: '',
        sort: 'Name',
        isSortDsc: false,
        showResigned: false,
        tagIds: [],
      })
      .subscribe((res) => {
        if (res && res.success) {
          this.employees = (res.model ?? []).map(
            (u) =>
              ({
                id: u.siteId,
                name: u.fullName || `${u.userFirstName} ${u.userLastName}`.trim() || u.siteName,
                description: '',
              }) as CommonDictionaryModel
          );
        }
      });
  }

  // ---------------------------------------------------------------
  // Control bindings — every one of these invalidates
  // ---------------------------------------------------------------

  get propertyId(): number | null {
    return this.state.filters.propertyId;
  }

  onPropertyChange(value: number | null): void {
    // Board and employee lists are property-scoped, so their values are reset
    // together with the property (calendar-container.component.ts:213-217).
    // The state write is ALL this handler does — the `filters$` subscription in
    // ngOnInit reloads the two lists, so a silent write (drill-down) or a
    // remount (re-entry) reloads them too.
    this.state.setFilter({propertyId: value ?? null, boardIds: [], siteIds: []});
  }

  get boardId(): number | null {
    return this.state.filters.boardIds.length ? this.state.filters.boardIds[0] : null;
  }

  onBoardChange(value: number | null): void {
    this.state.setFilter({boardIds: value == null ? [] : [value]});
  }

  get boardDisabled(): boolean {
    return this.state.filters.propertyId == null;
  }

  get tagIds(): number[] {
    return this.state.filters.tagIds;
  }

  onTagsChange(value: number[] | null): void {
    this.state.setFilter({tagIds: value ?? []});
  }

  /**
   * `Alle tags` / the single tag's name / `"{first} +{n-1}"`. "First" is the
   * TAG LIST's order, never click order — the prototype re-reads the checkbox
   * panel in DOM order on every change for exactly this reason
   * (compliance.js:2079-2089).
   */
  get tagToggleLabel(): string {
    const selected = this.tags.filter((t) => this.tagIds.indexOf(t.id) !== -1);
    if (selected.length === 0) {
      return this.translate.instant('All tags');
    }
    if (selected.length === 1) {
      return selected[0].name;
    }
    return `${selected[0].name} +${selected.length - 1}`;
  }

  get status(): ComplianceReportStatus {
    return this.state.filters.status;
  }

  onStatusChange(value: ComplianceReportStatus): void {
    this.state.setFilter({status: value});
  }

  /**
   * Oversigt counts done and not-done together and ignores the status value,
   * so the control is disabled there — honest rather than cosmetic. It stays
   * ENABLED in Rapport (compliance.js:1508-1514), so nobody has to detour
   * through Detaljer to change status.
   */
  get statusDisabled(): boolean {
    return this.state.mode === 'overview';
  }

  get siteId(): number | null {
    return this.state.filters.siteIds.length ? this.state.filters.siteIds[0] : null;
  }

  onEmployeeChange(value: number | null): void {
    this.state.setFilter({siteIds: value == null ? [] : [value]});
  }

  get periodPreset(): CompliancePeriodPreset {
    return this.state.filters.periodPreset;
  }

  onPeriodChange(value: CompliancePeriodPreset): void {
    this.state.setFilter({periodPreset: value});
  }

  get customFrom(): Date | null {
    return this.state.filters.customFrom;
  }

  set customFrom(value: Date | null) {
    this.state.setFilter({customFrom: value});
  }

  get customTo(): Date | null {
    return this.state.filters.customTo;
  }

  set customTo(value: Date | null) {
    this.state.setFilter({customTo: value});
  }

  /**
   * `3. september 2026 – 1. januar 2026`. Empty (and hidden) when a custom
   * range is incomplete, matching updatePeriodDisplay (compliance.js:492-503).
   * The bounds come from the state service's single derivation, so the label
   * can never disagree with the range that was queried.
   */
  get periodDisplay(): string {
    const bounds = this.state.periodBounds;
    if (!bounds) {
      return '';
    }
    const locale = getCurrentLocale(this.translate);
    const fmt = (d: Date) =>
      d.toLocaleDateString(locale, {day: 'numeric', month: 'long', year: 'numeric'});
    return `${fmt(bounds.from)} – ${fmt(bounds.to)}`;
  }

  get isPeriodValid(): boolean {
    return this.state.isPeriodValid;
  }

  get canFetch(): boolean {
    return this.isPeriodValid && !this.state.loading;
  }

  onUpdateTable(): void {
    this.state.requestFetch();
  }

  get canDownload(): boolean {
    return !!this.exportFormat && this.state.reportVisible && this.state.total > 0;
  }

  onDownload(): void {
    if (!this.canDownload) {
      return;
    }
    this.downloadRequested.emit(this.exportFormat);
  }
}
