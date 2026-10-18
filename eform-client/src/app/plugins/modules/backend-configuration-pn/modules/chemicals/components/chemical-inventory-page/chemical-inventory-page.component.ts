import {Component, DestroyRef, OnInit, inject} from '@angular/core';
import {takeUntilDestroyed} from '@angular/core/rxjs-interop';
import {Overlay} from '@angular/cdk/overlay';
import {ComponentType} from '@angular/cdk/portal';
import {MatDialog} from '@angular/material/dialog';
import {ActivatedRoute, Router} from '@angular/router';
import {MtxGridColumn} from '@ng-matero/extensions/grid';
import {TranslateService} from '@ngx-translate/core';
import {Store} from '@ngrx/store';
import {ToastrService} from 'ngx-toastr';
import {Observable, combineLatest} from 'rxjs';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {ChemicalDownload, ChemicalInventoryModel, ChemicalLocationModel} from '../../../../models';
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
  ChemicalLatestLoad,
  buildInventoryRows,
  downloadFile,
  filterInventoryRows,
  isStockEnabled,
  localToday,
  openSafetyDataSheet,
  sdsFileNameFor,
  summarizeActions,
} from '../../helpers';
import {ChemicalMoveDialogComponent} from '../chemical-move-dialog/chemical-move-dialog.component';
import {ChemicalNoteDialogComponent} from '../chemical-note-dialog/chemical-note-dialog.component';
import {
  ChemicalDrawerAction,
  ChemicalDrawerResult,
  ChemicalProductDrawerComponent,
  ChemicalProductDrawerData,
} from '../chemical-product-drawer/chemical-product-drawer.component';
import {ChemicalRegisterPlacementDialogComponent} from '../chemical-register-placement-dialog/chemical-register-placement-dialog.component';
import {ChemicalRemoveDialogComponent} from '../chemical-remove-dialog/chemical-remove-dialog.component';
import {ChemicalStockDialogComponent} from '../chemical-stock-dialog/chemical-stock-dialog.component';

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
  /** The newest inventory request; nothing is shown as empty while it is in flight (I-1). */
  readonly load = new ChemicalLatestLoad();

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
    this.load.reset();
    this.setInventory(null);
    if (this.propertyId !== null) {
      this.load.run(this.chemicalsService.getPropertyInventory(this.propertyId), (inventory) => this.setInventory(inventory));
    }
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

  /** W2 side panel (read-only, so Esc and a backdrop click close it); the action chosen there opens its dialog. */
  openDetails(row: ChemicalInventoryRow): void {
    const openers: Record<ChemicalDrawerAction, (target: ChemicalInventoryRow) => void> = {
      move: (target) => this.openMove(target),
      stock: (target) => this.openStock(target),
      remove: (target) => this.openRemove(target),
    };
    this.dialog.open<ChemicalProductDrawerComponent, ChemicalProductDrawerData, ChemicalDrawerResult>(ChemicalProductDrawerComponent, {
      ...dialogConfigHelper(this.overlay, {row, inventory: this.inventory, stockEnabled: this.stockEnabled}),
      disableClose: false,
      ariaLabel: row.productName,
      position: {top: '0', right: '0'},
      height: '100vh',
      maxHeight: '100vh',
      panelClass: 'chemicals-drawer-panel',
    }).afterClosed().subscribe((result) => result && openers[result.action](result.row));
  }

  openMove(row: ChemicalInventoryRow): void {
    this.openAndReload(ChemicalMoveDialogComponent, {row, locations: this.inventory?.locations ?? [], stockEnabled: this.stockEnabled});
  }

  openNote(row: ChemicalInventoryRow): void {
    this.openAndReload(ChemicalNoteDialogComponent, {row});
  }

  openStock(row: ChemicalInventoryRow): void {
    this.openAndReload(ChemicalStockDialogComponent, {row});
  }

  openRemove(row: ChemicalInventoryRow): void {
    this.openAndReload(ChemicalRemoveDialogComponent, {row, today: localToday()});
  }

  /**
   * Every placement dialog closes with true after a save; only then is the inventory stale. The data
   * type is the dialog's own (`data` field), so a wrong shape does not compile.
   */
  private openAndReload<D>(component: ComponentType<{readonly data: D}>, data: D): void {
    this.dialog.open<{readonly data: D}, D, boolean>(component, dialogConfigHelper(this.overlay, data))
      .afterClosed().subscribe((saved) => saved && this.reload());
  }

  openSds(row: ChemicalInventoryRow): void {
    const fileName = sdsFileNameFor(row);
    if (fileName) {
      openSafetyDataSheet(this.chemicalsService, this.toastr, this.translate, fileName);
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
    downloadFile(download(), this.toastr, this.translate, () => (this.exporting = false));
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
