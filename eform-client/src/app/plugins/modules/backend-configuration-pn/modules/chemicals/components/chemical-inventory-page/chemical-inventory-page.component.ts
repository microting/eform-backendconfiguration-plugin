import {Component, DestroyRef, OnInit, inject} from '@angular/core';
import {takeUntilDestroyed} from '@angular/core/rxjs-interop';
import {Overlay} from '@angular/cdk/overlay';
import {MatDialog} from '@angular/material/dialog';
import {ActivatedRoute} from '@angular/router';
import {MtxGridColumn} from '@ng-matero/extensions/grid';
import {TranslateService} from '@ngx-translate/core';
import {Store} from '@ngrx/store';
import {saveAs} from 'file-saver';
import {ToastrService} from 'ngx-toastr';
import {Observable, combineLatest} from 'rxjs';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {ChemicalDownload, ChemicalInventoryModel} from '../../../../models';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {
  ChemicalInventoryFiltersModel,
  chemicalsInitialState,
  chemicalsSelectProperty,
  chemicalsUpdateInventoryFilters,
  selectChemicalsInventoryFilters,
  selectChemicalsPropertyId,
} from '../../../../state';
import {
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
 * placements) and "Registrér placering". Honours the digest deeplink
 * ?propertyId=<id>&filter=requires-action (Plan D Contract C4).
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
  exporting = false;

  readonly rowClassFormatter = {
    'chemical-row--overdue': (row: ChemicalInventoryRow) => row.severity === 'overdue',
    'chemical-row--warning': (row: ChemicalInventoryRow) => row.severity === 'warning',
  };

  get stockEnabled(): boolean {
    return isStockEnabled(this.inventory, this.propertyId);
  }

  get summary() {
    return summarizeActions(this.rows);
  }

  get activeLocations() {
    return (this.inventory?.locations ?? []).filter((l) => !l.archived);
  }

  /** Rebuilt when the inventory loads (the Lager column depends on the property's stock flag), not per change detection. */
  columns: MtxGridColumn[] = [];

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
    this.inventory = null;
    this.rows = [];
    this.applyFilters();
    if (this.propertyId === null) {
      return;
    }
    const propertyId = this.propertyId;
    this.chemicalsService.getPropertyInventory(propertyId).subscribe((res) => {
      // A late answer for a property the user already left must not replace the current rows.
      if (propertyId === this.propertyId && res && res.success) {
        this.inventory = res.model;
        this.columns = this.buildColumns();
        this.rows = buildInventoryRows(res.model, localToday());
        this.applyFilters();
      }
    });
  }

  updateFilters(patch: Partial<ChemicalInventoryFiltersModel>): void {
    this.store.dispatch(chemicalsUpdateInventoryFilters({payload: {...this.filters, ...patch}}));
  }

  toggleRequiresAction(): void {
    this.updateFilters({requiresActionOnly: !this.filters.requiresActionOnly});
  }

  openRegister(): void {
    if (this.propertyId === null) {
      return;
    }
    this.dialog.open(ChemicalRegisterPlacementDialogComponent, dialogConfigHelper(this.overlay, {
      propertyId: this.propertyId, locations: this.inventory?.locations ?? [], stockEnabled: this.stockEnabled,
    })).afterClosed().subscribe((saved) => saved && this.reload());
  }

  /** W2 side panel - wired in Task 13. */
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
    if (this.propertyId === null || this.exporting) {
      return;
    }
    this.exporting = true;
    download().subscribe({
      next: (file) => saveAs(file.blob, file.fileName),
      error: () => {
        this.exporting = false;
        this.toastr.error(this.translate.instant('Export failed'));
      },
      complete: () => (this.exporting = false),
    });
  }

  private applyFilters(): void {
    this.visibleRows = filterInventoryRows(this.rows, this.filters);
  }

  private applyDeeplink(): void {
    const params = this.route.snapshot.queryParamMap;
    const propertyId = Number(params.get('propertyId'));
    if (params.has('propertyId') && Number.isInteger(propertyId) && propertyId > 0) {
      this.store.dispatch(chemicalsSelectProperty({payload: propertyId}));
    }
    if (params.get('filter') === 'requires-action') {
      this.store.dispatch(chemicalsUpdateInventoryFilters({
        payload: {...chemicalsInitialState.inventoryFilters, requiresActionOnly: true},
      }));
    }
  }
}
