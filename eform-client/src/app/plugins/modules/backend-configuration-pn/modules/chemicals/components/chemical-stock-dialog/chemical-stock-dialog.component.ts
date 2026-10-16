import {Component, inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {finalize} from 'rxjs/operators';
import {ChemicalStockEntryKind, ChemicalStockUnit} from '../../../../models';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {ChemicalInventoryRow, stockUnitLabel} from '../../helpers';

/**
 * "Registrér lagerbevægelse": Modtaget / Forbrugt / Justeret (spec §6 AddStockEntry).
 * Received/Consumed take a positive amount; Adjusted takes the newly counted
 * balance (Plan A decision 6), which must differ from the current one (E-20: the
 * server refuses an adjustment that changes nothing). The unit is fixed once a
 * placement has one.
 */
@Component({
  selector: 'app-chemical-stock-dialog',
  templateUrl: './chemical-stock-dialog.component.html',
  styleUrls: ['./chemical-stock-dialog.component.scss'],
  standalone: false,
})
export class ChemicalStockDialogComponent {
  private chemicalsService = inject(BackendConfigurationPnChemicalsService);
  private dialogRef = inject(MatDialogRef<ChemicalStockDialogComponent>);
  readonly data: {row: ChemicalInventoryRow} = inject(MAT_DIALOG_DATA);

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

  get canSave(): boolean {
    if (this.saving || this.amount === null || this.amount === undefined || this.amount < 0) {
      return false;
    }
    const balance = this.data.row.placement.balance;
    switch (this.kind) {
      case ChemicalStockEntryKind.Adjusted: return this.amount !== balance;
      case ChemicalStockEntryKind.Consumed: return this.amount > 0 && this.amount <= balance;
      default: return this.amount > 0;
    }
  }

  cancel(): void {
    this.dialogRef.close(false);
  }

  save(): void {
    if (!this.canSave) {
      return;
    }
    this.saving = true;
    const batchLot = this.batchLot.trim();
    this.chemicalsService.addStockEntry(this.data.row.placement.id, {
      kind: this.kind,
      amount: {
        containerSize: null, containerCount: null, amount: this.amount, unit: this.unit,
        batchLot: this.kind === ChemicalStockEntryKind.Received && batchLot ? batchLot : null,
        note: this.note.trim() || null, at: null,
      },
    }).pipe(finalize(() => (this.saving = false))).subscribe((res) => {
      if (res && res.success) {
        this.dialogRef.close(true);
      }
    });
  }
}
