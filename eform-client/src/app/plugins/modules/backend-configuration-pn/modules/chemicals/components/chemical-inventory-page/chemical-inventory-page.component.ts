import {Component, DestroyRef, OnInit, inject} from '@angular/core';
import {takeUntilDestroyed} from '@angular/core/rxjs-interop';
import {Overlay} from '@angular/cdk/overlay';
import {MatDialog} from '@angular/material/dialog';
import {ActivatedRoute, Router} from '@angular/router';
import {MtxGridColumn} from '@ng-matero/extensions/grid';
import {TranslateService} from '@ngx-translate/core';
import {Store} from '@ngrx/store';
import {saveAs} from 'file-saver';
import {ToastrService} from 'ngx-toastr';
import {Observable, combineLatest} from 'rxjs';
import {finalize} from 'rxjs/operators';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {ChemicalDownload, ChemicalDownloadError, ChemicalInventoryModel, ChemicalLocationModel} from '../../../../models';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {
  ChemicalInventoryFiltersModel,
  chemicalsInitialState,
  chemicalsUpdateInventoryFilters,
  selectChemicalsInventoryFilters,
  selectChemicalsPropertyId,
} from '../../../../state';
import {
  ChemicalActionSummary,
  ChemicalInventoryRow,
  buildInventoryRows,
  filterInventoryRows,
  isStockEnabled,
  localToday,
  sdsFileNameFor,
  summarizeActions,
} from '../../helpers';
import {ChemicalRegisterPlacementDialogComponent} from '../chemical-register-placement-dialog/chemical-register-placement-dialog.component';

/**
 * W1 "Kemikalier – beholdning": open placements of the selected property with
 * search/location/status filters, the "Kræver handling" chip and banner (Plan D
 * rule), exports (PDF for Arbejdstilsynet, Excel of the current filter, removed
 * placements) and "Registrér placering". Honours the filter of the digest deeplink
 * ?propertyId=<id>&filter=requires-action (Plan D Contract C4); the property is the
 * container's (it checks the id against the known properties).
 */
@Component({
  selector: 'app-chemical-inventory-page',
  templateUrl: './chemical-inventory-page.component.html',
  styleUrls: ['./chemical-inventory-page.component.scss'],
  standalone: false,
})
export class ChemicalInventoryPageComponent implements OnInit {
  private store = inject(Store);
  private chemicalsService = inject(BackendConfigurationPnChemicalsService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private dialog = inject(MatDialog);
  private overlay = inject(Overlay);
  private translate = inject(TranslateService);
  private toastr = inject(ToastrService);
  private destroyRef = inject(DestroyRef);

  propertyId: number | null = null;
  inventory: ChemicalInventoryModel | null = null;
  filters: ChemicalInventoryFiltersModel = {...chemicalsInitialState.inventoryFilters};
  rows: ChemicalInventoryRow[] = [];
  visibleRows: ChemicalInventoryRow[] = [];
  /** "Kræver handling" counts of `rows`; set with them, not per change detection. */
  summary: ChemicalActionSummary = summarizeActions([]);
  /** Where a placement can be registered; set with the inventory. */
  activeLocations: ChemicalLocationModel[] = [];
  /** Rebuilt when the inventory loads (the Lager column depends on the property's stock flag), not per change detection. */
  columns: MtxGridColumn[] = [];
  exporting = false;
  /** The inventory request is in flight; nothing is shown as empty meanwhile (I-1). */
  loading = false;
  loadFailed = false;
  /** Only the newest inventory request may set the rows (another property, or a reload after a save). */
  private loadGeneration = 0;

  readonly rowClassFormatter = {
    'chemical-row--overdue': (row: ChemicalInventoryRow) => row.severity === 'overdue',
    'chemical-row--warning': (row: ChemicalInventoryRow) => row.severity === 'warning',
  };

  get stockEnabled(): boolean {
    return isStockEnabled(this.inventory, this.propertyId);
  }

  private buildColumns(): MtxGridColumn[] {
    const header = (key: string) => this.translate.stream(key);
    return [
      {header: header('Chemicals: Hazard'), field: 'pictograms'},
      {header: header('Chemicals: Product'), field: 'productName', sortable: true},
      {header: header('Status'), field: 'status'},
      {header: header('Location'), field: 'locationName', sortable: true},
      {header: header('Chemicals: Placement'), field: 'placementNote'},
      ...(this.stockEnabled ? [{header: header('Chemicals: Stock'), field: 'stock'}] : []),
      {header: header('Chemicals: Deadline'), field: 'deadline'},
      {header: header('Chemicals: Registered'), field: 'registered'},
      {header: header('Actions'), field: 'actions', width: '80px', pinned: 'right' as const},
    ];
  }

  ngOnInit(): void {
    this.applyDeeplink();
    combineLatest([this.store.select(selectChemicalsPropertyId), this.store.select(selectChemicalsInventoryFilters)])
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(([propertyId, filters]) => {
        this.filters = filters;
        if (propertyId !== this.propertyId) {
          this.propertyId = propertyId;
          this.reload();
        } else {
          this.applyFilters();
        }
      });
  }

  reload(): void {
    const generation = ++this.loadGeneration;
    this.setInventory(null);
    this.loadFailed = false;
    this.loading = this.propertyId !== null;
    this.applyFilters();
    if (this.propertyId === null) {
      return;
    }
    this.chemicalsService.getPropertyInventory(this.propertyId).subscribe({
      next: (res) => {
        if (generation !== this.loadGeneration) {
          return;
        }
        this.loading = false;
        if (!res || !res.success) {
          // ApiBaseService already toasted the server's message.
          this.loadFailed = true;
          return;
        }
        this.setInventory(res.model);
      },
      error: () => {
        if (generation === this.loadGeneration) {
          this.loading = false;
          this.loadFailed = true;
        }
      },
    });
  }

  updateFilters(patch: Partial<ChemicalInventoryFiltersModel>): void {
    this.store.dispatch(chemicalsUpdateInventoryFilters({payload: {...this.filters, ...patch}}));
  }

  toggleRequiresAction(): void {
    const requiresActionOnly = !this.filters.requiresActionOnly;
    this.updateFilters({requiresActionOnly});
    if (!requiresActionOnly) {
      this.dropFilterParam();
    }
  }

  openRegister(): void {
    if (this.propertyId === null) {
      return;
    }
    this.dialog.open(ChemicalRegisterPlacementDialogComponent, dialogConfigHelper(this.overlay, {
      propertyId: this.propertyId, locations: this.inventory?.locations ?? [], stockEnabled: this.stockEnabled,
    })).afterClosed().subscribe((saved) => saved && this.reload());
  }

  // TODO(Task 13): open the W2 side panel; the product button and the "Show details" item already call this.
  openDetails(row: ChemicalInventoryRow): void {
    void row;
  }

  openSds(row: ChemicalInventoryRow): void {
    const fileName = sdsFileNameFor(row);
    if (fileName) {
      // getBlobData runs through the host interceptors, which show nothing for a failed blob request.
      this.chemicalsService.getSdsBlob(fileName).subscribe({
        next: (blob) => window.open(URL.createObjectURL(blob), '_blank'),
        error: () => this.toastr.error(this.translate.instant('Chemicals: Could not open safety data sheet')),
      });
    }
  }

  exportAuthorityList(): void {
    this.save(() => this.chemicalsService.exportAuthorityList(this.propertyId));
  }

  exportInventory(): void {
    this.save(() => this.chemicalsService.exportInventory(this.propertyId, this.visibleRows.map((r) => r.placement.id)));
  }

  exportRemovedPlacements(): void {
    this.save(() => this.chemicalsService.exportRemovedPlacements(this.propertyId));
  }

  /**
   * One download at a time (the Export button is busy meanwhile). The raw download client
   * has no interceptors, so a refused or failed file is toasted here (m-2).
   */
  private save(download: () => Observable<ChemicalDownload>): void {
    // Nothing to export before the inventory has loaded (the Excel would hold no rows).
    if (this.propertyId === null || !this.inventory || this.exporting) {
      return;
    }
    this.exporting = true;
    download().pipe(finalize(() => (this.exporting = false))).subscribe({
      next: (file) => saveAs(file.blob, file.fileName),
      error: (err: Partial<ChemicalDownloadError>) => this.toastr.error(this.exportFailureText(err)),
    });
  }

  /**
   * The server's reason when it gave one (m-9), else a refusal or the generic failure. Only a
   * ChemicalDownloadError (it has a status) carries a reason: any other error's message is
   * technical and is never shown.
   */
  private exportFailureText(err: Partial<ChemicalDownloadError> | null): string {
    const isDownloadError = typeof err?.status === 'number';
    if (isDownloadError && err.message) {
      return err.message;
    }
    return this.translate.instant(isDownloadError && err.status === 403 ? 'Chemicals: Export not allowed' : 'Export failed');
  }

  /**
   * The chip was switched off: a ?filter=requires-action left from the digest link would
   * switch it on again on a reload or Back (m-2). Never while a navigation is in flight (R-2);
   * the URL write is a navigation of its own and would cancel it.
   */
  private dropFilterParam(): void {
    if (!this.route.snapshot.queryParamMap.has('filter') || this.router.currentNavigation()) {
      return;
    }
    void this.router.navigate([], {queryParams: {filter: null}, queryParamsHandling: 'merge', replaceUrl: true});
  }

  /** The inventory and everything derived from it; null clears the page while a property loads. */
  private setInventory(inventory: ChemicalInventoryModel | null): void {
    this.inventory = inventory;
    this.rows = inventory ? buildInventoryRows(inventory, localToday()) : [];
    this.summary = summarizeActions(this.rows);
    this.activeLocations = (inventory?.locations ?? []).filter((l) => !l.archived);
    if (inventory) {
      this.columns = this.buildColumns();
    }
    this.applyFilters();
  }

  private applyFilters(): void {
    this.visibleRows = filterInventoryRows(this.rows, this.filters);
  }

  private applyDeeplink(): void {
    if (this.route.snapshot.queryParamMap.get('filter') === 'requires-action') {
      this.store.dispatch(chemicalsUpdateInventoryFilters({
        payload: {...chemicalsInitialState.inventoryFilters, requiresActionOnly: true},
      }));
    }
  }
}
