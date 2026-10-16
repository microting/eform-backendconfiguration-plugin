import {Component, inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {TranslateService} from '@ngx-translate/core';
import {ToastrService} from 'ngx-toastr';
import {ChemicalInventoryModel, ChemicalPlacementModel} from '../../../../models';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {
  ChemicalInventoryRow,
  ChemicalStockHistoryRow,
  placementStockHistory,
  placementsOfChemical,
  sdsFileNameFor,
  stockUnitLabel,
} from '../../helpers';

export interface ChemicalProductDrawerData {
  row: ChemicalInventoryRow;
  inventory: ChemicalInventoryModel;
  stockEnabled: boolean;
}

export type ChemicalDrawerAction = 'move' | 'stock' | 'remove';

export type ChemicalDrawerResult = {action: ChemicalDrawerAction; row: ChemicalInventoryRow} | undefined;

/**
 * W2 "Produktdetaljer i sidepanel": BMD overview (status dates, deadlines,
 * H-statements, SDS), the chemical's placements on the property and the
 * placement's stock history (server balances, E-06). Actions close the panel
 * with the chosen action so the page opens the matching dialog (same chaining
 * as the adhoc drawer).
 */
@Component({
  selector: 'app-chemical-product-drawer',
  templateUrl: './chemical-product-drawer.component.html',
  styleUrls: ['./chemical-product-drawer.component.scss'],
  standalone: false,
})
export class ChemicalProductDrawerComponent {
  private chemicalsService = inject(BackendConfigurationPnChemicalsService);
  private dialogRef = inject(MatDialogRef<ChemicalProductDrawerComponent, ChemicalDrawerResult>);
  private toastr = inject(ToastrService);
  private translate = inject(TranslateService);
  readonly data: ChemicalProductDrawerData = inject(MAT_DIALOG_DATA);

  readonly row = this.data.row;
  readonly stockEnabled = this.data.stockEnabled;
  readonly placements: ChemicalPlacementModel[] = placementsOfChemical(this.data.inventory, this.row.placement.chemicalId);
  readonly stockHistory: ChemicalStockHistoryRow[] = placementStockHistory(this.data.inventory, this.row.placement.id);
  readonly sdsFileName = sdsFileNameFor(this.row);
  readonly unitLabel = stockUnitLabel;
  tab: 'overview' | 'placements' | 'stock' = 'overview';

  locationName(locationId: number): string {
    return this.data.inventory.locations.find((l) => l.id === locationId)?.name ?? '';
  }

  act(action: ChemicalDrawerAction): void {
    this.dialogRef.close({action, row: this.row});
  }

  close(): void {
    this.dialogRef.close(undefined);
  }

  openSds(): void {
    if (!this.sdsFileName) {
      return;
    }
    // getBlobData runs through the host interceptors, which show nothing for a failed blob request.
    this.chemicalsService.getSdsBlob(this.sdsFileName).subscribe({
      next: (blob) => window.open(URL.createObjectURL(blob), '_blank'),
      error: () => this.toastr.error(this.translate.instant('Chemicals: Could not open safety data sheet')),
    });
  }
}
