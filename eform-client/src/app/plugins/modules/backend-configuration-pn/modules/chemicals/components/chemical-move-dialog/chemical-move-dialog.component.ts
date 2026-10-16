import {Component, inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {ChemicalLocationModel} from '../../../../models';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {ChemicalPlacementDialogData, cancelPlacementDialog, closeWhenSaved, discardContext, isValidQuantity} from '../../helpers';

export interface ChemicalMoveDialogData extends ChemicalPlacementDialogData {
  locations: ChemicalLocationModel[];
  stockEnabled: boolean;
}

/** "Flyt til anden lokation": full move, or (stock on) a partial move of an amount (spec §6 MovePlacement). */
@Component({
  selector: 'app-chemical-move-dialog',
  templateUrl: './chemical-move-dialog.component.html',
  styleUrls: ['./chemical-move-dialog.component.scss'],
  standalone: false,
})
export class ChemicalMoveDialogComponent {
  private chemicalsService = inject(BackendConfigurationPnChemicalsService);
  private dialogRef = inject(MatDialogRef<ChemicalMoveDialogComponent, boolean>);
  private discardContext = discardContext(this.dialogRef);
  readonly data: ChemicalMoveDialogData = inject(MAT_DIALOG_DATA);

  readonly placement = this.data.row.placement;
  readonly targets = this.data.locations.filter((l) => !l.archived && l.id !== this.placement.locationId);
  /** Part of the balance can move only when stock is on and the placement has a counted balance. */
  readonly canPartial = this.data.stockEnabled && !!this.placement.unit && this.placement.balance > 0;
  targetLocationId: number | null = null;
  note = '';
  moveAll = true;
  amount: number | null = null;
  saving = false;

  /** A part is a valid quantity below the balance; the whole balance is "move everything". */
  get amountValid(): boolean {
    return isValidQuantity(this.amount, false) && this.amount < this.placement.balance;
  }

  /** A typed amount outside the server's limits (more than three decimals, negative or above one million). */
  get outsideLimits(): boolean {
    return this.amount !== null && this.amount !== undefined && !isValidQuantity(this.amount, true);
  }

  get canSave(): boolean {
    return this.targetLocationId !== null && (this.moveAll || this.amountValid) && !this.saving;
  }

  get dirty(): boolean {
    return this.targetLocationId !== null || this.note.trim() !== '' || !this.moveAll || this.amount !== null;
  }

  cancel(): void {
    cancelPlacementDialog(this.discardContext, {
      busy: this.saving, dirty: this.dirty, productName: this.data.row.productName, confirmId: 'chemicalMoveDiscardConfirm',
    });
  }

  save(): void {
    if (!this.canSave) {
      return;
    }
    this.saving = true;
    closeWhenSaved(this.dialogRef, this.chemicalsService.movePlacement(this.placement.id, {
      targetLocationId: this.targetLocationId,
      targetPlacementNote: this.note.trim(),
      amount: this.moveAll ? null : this.amount,
    }), () => (this.saving = false));
  }
}
