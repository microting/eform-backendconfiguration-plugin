import {Component, DestroyRef, OnInit, inject} from '@angular/core';
import {takeUntilDestroyed} from '@angular/core/rxjs-interop';
import {TranslateService} from '@ngx-translate/core';
import {Store} from '@ngrx/store';
import {saveAs} from 'file-saver';
import {ToastrService} from 'ngx-toastr';
import {finalize} from 'rxjs/operators';
import {
  ChemicalHistoryEventModel,
  ChemicalHistoryQueryModel,
  ChemicalHistoryUserModel,
  ChemicalLocationModel,
} from '../../../../models';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {
  ChemicalHistoryFiltersModel,
  chemicalsInitialState,
  chemicalsUpdateHistoryFilters,
  selectChemicalsHistoryView,
} from '../../../../state';
import {
  CHEMICAL_HISTORY_ACTIONS,
  ChemicalLatestLoad,
  HISTORY_PAGE_SIZES,
  SYSTEM_USER_ID,
  asUtcIso,
  buildHistoryQuery,
  copenhagenToday,
  defaultHistoryPeriod,
  downloadFailureText,
  formatHistoryDetails,
  historyActionKey,
  historyPeriodError,
} from '../../helpers';

/**
 * W6 "Historik": the immutable event log of the selected property (placements, stock, moves, locations,
 * permissions, settings and the system's own events), filtered by user, action, location and period, paged
 * on the server and exportable as Excel. The filters live in the store; any change starts again at page 0.
 * The property is the container's (?propertyId sync and the picker).
 */
@Component({
  selector: 'app-chemical-history-page',
  templateUrl: './chemical-history-page.component.html',
  styleUrls: ['./chemical-history-page.component.scss'],
  standalone: false,
})
export class ChemicalHistoryPageComponent implements OnInit {
  private store = inject(Store);
  private chemicalsService = inject(BackendConfigurationPnChemicalsService);
  private translate = inject(TranslateService);
  private toastr = inject(ToastrService);
  private destroyRef = inject(DestroyRef);

  readonly actions = CHEMICAL_HISTORY_ACTIONS;
  readonly actionKey = historyActionKey;
  readonly utc = asUtcIso;
  readonly pageSizeOptions = HISTORY_PAGE_SIZES;
  readonly systemUserId = SYSTEM_USER_ID;
  /** The clock (a spec sets it); today is taken from it at every load, not frozen at construction. */
  now: () => Date = () => new Date();
  /** The Copenhagen day of the latest load (the server counts the period in Copenhagen days): the default period ends there. */
  today = copenhagenToday();
  propertyId: number | null = null;
  filters: ChemicalHistoryFiltersModel = {...chemicalsInitialState.historyFilters};
  events: ChemicalHistoryEventModel[] = [];
  /** Every user seen in the period (the server's list, without System). */
  users: ChemicalHistoryUserModel[] = [];
  /** All locations of the property, archived ones included: their history stays. */
  locations: ChemicalLocationModel[] = [];
  /** The location filter: active ones in their order, then the archived ones, marked. */
  locationOptions: Array<{id: number; name: string}> = [];
  /** The user filter: System, the period's users and, if it is not among them, the selected user (m-5). */
  userOptions: ChemicalHistoryUserModel[] = [];
  total = 0;
  pageIndex = 0;
  pageSize = HISTORY_PAGE_SIZES[0];
  exporting = false;
  /** The newest page request; an answer for an older query or property is dropped. */
  private readonly load = new ChemicalLatestLoad();
  private readonly locationsLoad = new ChemicalLatestLoad();
  /** Every user name seen on this property, so a selected user never shows as a blank chip. */
  private readonly knownUsers = new Map<number, string>();

  get loading(): boolean {
    return this.load.loading;
  }

  get loadFailed(): boolean {
    return this.load.failed;
  }

  /** The location filter could not be filled (a refusal, an error or no answer). */
  get locationsFailed(): boolean {
    return this.locationsLoad.failed;
  }

  /** A download in flight belongs to this property: the container keeps the picker until it lands. */
  get busy(): boolean {
    return this.exporting;
  }

  get query(): ChemicalHistoryQueryModel {
    return buildHistoryQuery(this.filters, this.today, this.pageIndex, this.pageSize);
  }

  /** The translation key of what is wrong with the period, or null; such a period never reaches the server. */
  get periodErrorKey(): string | null {
    return historyPeriodError(this.query);
  }

  get periodError(): boolean {
    return this.periodErrorKey !== null;
  }

  ngOnInit(): void {
    this.store.select(selectChemicalsHistoryView)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(({propertyId, filters}) => {
        const propertyChanged = propertyId !== this.propertyId;
        this.propertyId = propertyId;
        this.filters = filters;
        this.pageIndex = 0;
        if (propertyChanged) {
          this.knownUsers.clear();
          this.users = [];
          this.loadLocations();
        }
        this.setUsers(this.users);
        this.reload(true);
      });
  }

  updateFilters(patch: Partial<ChemicalHistoryFiltersModel>): void {
    this.store.dispatch(chemicalsUpdateHistoryFilters({payload: {...this.filters, ...patch}}));
  }

  /**
   * A date field's committed value. An emptied field falls back to the default period's bound and shows it
   * again (the [value] binding would not, since the bound value did not change).
   */
  setDate(bound: 'dateFrom' | 'dateTo', input: {value: string}): void {
    const value = input.value || null;
    this.updateFilters({[bound]: value});
    if (!value) {
      const period = defaultHistoryPeriod(this.today);
      input.value = bound === 'dateFrom' ? period.from : period.to;
    }
  }

  onPage(event: {pageIndex: number; pageSize: number}): void {
    this.pageIndex = event.pageIndex;
    this.pageSize = event.pageSize;
    this.reload(false);
  }

  details(event: ChemicalHistoryEventModel): string {
    return formatHistoryDetails(event, (key) => this.translate.instant(key));
  }

  /** The whole filtered period (the server ignores paging for the file); one download at a time. */
  export(): void {
    if (this.propertyId === null || this.periodError || this.exporting) {
      return;
    }
    this.exporting = true;
    this.chemicalsService.exportHistory(this.propertyId, {...this.query, page: 0})
      .pipe(finalize(() => (this.exporting = false)))
      .subscribe({
        next: (file) => saveAs(file.blob, file.fileName),
        error: (err: unknown) => this.toastr.error(downloadFailureText(err, this.translate)),
      });
  }

  /** `restart`: a new property or filter, so the old count no longer applies; a page change keeps it. */
  private reload(restart: boolean): void {
    this.today = copenhagenToday(this.now());
    this.load.reset();
    this.events = [];
    const propertyId = this.propertyId;
    const unqueryable = propertyId === null || this.periodError;
    if (restart || unqueryable) {
      this.total = 0;
    }
    if (unqueryable) {
      return;
    }
    this.load.run(this.chemicalsService.getHistory(propertyId, this.query), (page) => {
      this.events = page?.events ?? [];
      this.total = page?.total ?? 0;
      this.setUsers(page?.users ?? []);
    });
  }

  private setUsers(users: ChemicalHistoryUserModel[]): void {
    this.users = users;
    users.forEach((u) => this.knownUsers.set(u.userId, u.name));
    const selected = this.filters.userId;
    const hidden = selected !== null && selected !== SYSTEM_USER_ID && !users.some((u) => u.userId === selected);
    this.userOptions = [
      {userId: SYSTEM_USER_ID, name: this.translate.instant('Chemicals: System user')},
      ...users,
      ...(hidden ? [{userId: selected, name: this.knownUsers.get(selected) ?? `#${selected}`}] : []),
    ];
  }

  private loadLocations(): void {
    this.locationsLoad.reset();
    this.setLocations([]);
    if (this.propertyId !== null) {
      this.locationsLoad.run(this.chemicalsService.getPropertyInventory(this.propertyId),
        (inventory) => this.setLocations(inventory?.locations ?? []));
    }
  }

  private setLocations(locations: ChemicalLocationModel[]): void {
    this.locations = locations;
    const archived = this.translate.instant('Chemicals: Archived');
    this.locationOptions = [...locations]
      .sort((a, b) => Number(a.archived) - Number(b.archived) || a.sortOrder - b.sortOrder || a.id - b.id)
      .map((l) => ({id: l.id, name: l.archived ? `${l.name} (${archived})` : l.name}));
  }
}
