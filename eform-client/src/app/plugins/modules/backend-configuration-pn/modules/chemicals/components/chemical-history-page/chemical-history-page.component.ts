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
  defaultHistoryPeriod,
  downloadFailureText,
  formatHistoryDetails,
  historyActionKey,
  historyPeriodError,
  localToday,
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
  /** The browser's calendar day: the default period ends today. */
  today = localToday();
  propertyId: number | null = null;
  filters: ChemicalHistoryFiltersModel = {...chemicalsInitialState.historyFilters};
  events: ChemicalHistoryEventModel[] = [];
  /** Every user seen in the period (the server's list, without System). */
  users: ChemicalHistoryUserModel[] = [];
  /** All locations of the property, archived ones included: their history stays. */
  locations: ChemicalLocationModel[] = [];
  total = 0;
  pageIndex = 0;
  pageSize = HISTORY_PAGE_SIZES[0];
  exporting = false;
  /** The newest page request; an answer for an older query or property is dropped. */
  private readonly load = new ChemicalLatestLoad();
  private readonly locationsLoad = new ChemicalLatestLoad();

  get loading(): boolean {
    return this.load.loading;
  }

  get loadFailed(): boolean {
    return this.load.failed;
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

  get userOptions(): ChemicalHistoryUserModel[] {
    return [{userId: SYSTEM_USER_ID, name: this.translate.instant('Chemicals: System user')}, ...this.users];
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
          this.users = [];
          this.loadLocations();
        }
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
    const query = this.query;
    this.load.reset();
    this.events = [];
    if (restart) {
      this.total = 0;
    }
    if (this.propertyId === null || historyPeriodError(query)) {
      this.total = 0;
      return;
    }
    this.load.run(this.chemicalsService.getHistory(this.propertyId, query), (page) => {
      this.events = page?.events ?? [];
      this.total = page?.total ?? 0;
      this.users = page?.users ?? [];
    });
  }

  private loadLocations(): void {
    this.locationsLoad.reset();
    this.locations = [];
    if (this.propertyId !== null) {
      this.locationsLoad.run(this.chemicalsService.getPropertyInventory(this.propertyId),
        (inventory) => (this.locations = inventory?.locations ?? []));
    }
  }
}
