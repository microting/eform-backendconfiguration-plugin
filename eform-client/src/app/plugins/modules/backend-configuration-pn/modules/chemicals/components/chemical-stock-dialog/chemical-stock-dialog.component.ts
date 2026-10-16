import {Component, inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {ChemicalStockEntryKind, ChemicalStockUnit} from '../../../../models';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {
  ChemicalPlacementDialogData,
  cancelPlacementDialog,
  closeWhenSaved,
  discardContext,
  isValidQuantity,
  stockUnitLabel,
} from '../../helpers';

/**
 * "Registrér lagerbevægelse": Modtaget / Forbrugt / Justeret (spec §6 AddStockEntry).
 * Received/Consumed take a positive amount; Adjusted takes the newly counted
 * balance (Plan A decision 6), which must differ from the current one (E-20: the
 * server refuses an adjustment that changes nothing). Every amount follows the
 * server's ChemicalQuantity rules (three decimals, at most one million). The unit
 * is fixed once a placement has one.
 */
@Component({
  selector: 'app-chemical-stock-dialog',
  templateUrl: './chemical-stock-dialog.component.html',
  styleUrls: ['./chemical-stock-dialog.component.scss'],
  standalone: false,
})
export class ChemicalStockDialogComponent {
  private chemicalsService = inject(BackendConfigurationPnChemicalsService);
  private dialogRef = inject(MatDialogRef<ChemicalStockDialogComponent, boolean>);
  private discardContext = discardContext(this.dialogRef);
  readonly data: ChemicalPlacementDialogData = inject(MAT_DIALOG_DATA);

  readonly kinds = ChemicalStockEntryKind;
  readonly units = ChemicalStockUnit;
  readonly unitLocked = !!this.data.row.placement.unit;
  kind: ChemicalStockEntryKind = ChemicalStockEntryKind.Consumed;
  unit: ChemicalStockUnit = this.data.row.placement.unit ?? ChemicalStockUnit.L;
  amount: number | null = null;
  batchLot = '';
  note = '';
  saving = false;

  get unitLabel(): string {
    return stockUnitLabel(this.unit);
  }

  /** The amount alone, against the rule of the chosen kind (no busy state). */
  get amountValid(): boolean {
    const balance = this.data.row.placement.balance;
    switch (this.kind) {
      case ChemicalStockEntryKind.Adjusted: return isValidQuantity(this.amount, true) && this.amount !== balance;
      case ChemicalStockEntryKind.Consumed: return isValidQuantity(this.amount, false) && this.amount <= balance;
      default: return isValidQuantity(this.amount, false);
    }
  }

  /** A typed amount outside the server's limits (more than three decimals, negative or above one million). */
  get outsideLimits(): boolean {
    return this.amount !== null && this.amount !== undefined && !isValidQuantity(this.amount, true);
  }

  get canSave(): boolean {
    return !this.saving && this.amountValid;
  }

  get dirty(): boolean {
    return this.amount !== null || this.batchLot.trim() !== '' || this.note.trim() !== '';
  }

  cancel(): void {
    cancelPlacementDialog(this.discardContext, {
      busy: this.saving, dirty: this.dirty, productName: this.data.row.productName, confirmId: 'chemicalStockDiscardConfirm',
    });
  }

  save(): void {
    if (!this.canSave) {
      return;
    }
    this.saving = true;
    const batchLot = this.batchLot.trim();
    closeWhenSaved(this.dialogRef, this.chemicalsService.addStockEntry(this.data.row.placement.id, {
      kind: this.kind,
      amount: {
        containerSize: null, containerCount: null, amount: this.amount, unit: this.unit,
        batchLot: this.kind === ChemicalStockEntryKind.Received && batchLot ? batchLot : null,
        note: this.note.trim() || null, at: null,
      },
    }), () => (this.saving = false));
  }
}
