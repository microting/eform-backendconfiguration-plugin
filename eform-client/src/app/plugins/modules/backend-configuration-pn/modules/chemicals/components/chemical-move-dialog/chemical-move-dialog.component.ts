import {Component, inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {finalize} from 'rxjs/operators';
import {ChemicalLocationModel} from '../../../../models';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {ChemicalInventoryRow} from '../../helpers';

export interface ChemicalMoveDialogData {
  row: ChemicalInventoryRow;
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
  private dialogRef = inject(MatDialogRef<ChemicalMoveDialogComponent>);
  readonly data: ChemicalMoveDialogData = inject(MAT_DIALOG_DATA);

  readonly targets = this.data.locations.filter((l) => !l.archived && l.id !== this.data.row.placement.locationId);
  /** Part of the balance can move only when stock is on and the placement has a counted balance. */
  readonly canPartial = this.data.stockEnabled && !!this.data.row.placement.unit && this.data.row.placement.balance > 0;
  targetLocationId: number | null = null;
  note = '';
  moveAll = true;
  amount: number | null = null;
  saving = false;

  get canSave(): boolean {
    // A part is less than the whole balance; the whole balance is "move everything".
    const partialOk = this.moveAll || (!!this.amount && this.amount > 0 && this.amount < this.data.row.placement.balance);
    return this.targetLocationId !== null && partialOk && !this.saving;
  }

  cancel(): void {
    this.dialogRef.close(false);
  }

  save(): void {
    if (!this.canSave) {
      return;
    }
    this.saving = true;
    this.chemicalsService.movePlacement(this.data.row.placement.id, {
      targetLocationId: this.targetLocationId,
      targetPlacementNote: this.note.trim(),
      amount: this.moveAll ? null : this.amount,
    }).pipe(finalize(() => (this.saving = false))).subscribe((res) => {
      if (res && res.success) {
        this.dialogRef.close(true);
      }
    });
  }
}
