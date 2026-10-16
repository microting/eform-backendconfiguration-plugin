import {Component, inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {ChemicalRemovalReason} from '../../../../models';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {ChemicalPlacementDialogData, cancelPlacementDialog, closeWhenSaved, discardContext, reachedThreshold} from '../../helpers';

export interface ChemicalRemoveDialogData extends ChemicalPlacementDialogData {
  /** yyyy-MM-dd; injected so the warning is testable. */
  today: string;
}

/**
 * "Fjern placering…": reason Opbrugt or Bortskaffet is required (Flyttet only
 * happens through a move). Warns "Skal bortskaffes som farligt affald" once the
 * use-and-possession deadline has passed (spec §2 decision 12). Remaining stock
 * is written off by the server.
 */
@Component({
  selector: 'app-chemical-remove-dialog',
  templateUrl: './chemical-remove-dialog.component.html',
  styleUrls: ['./chemical-remove-dialog.component.scss'],
  standalone: false,
})
export class ChemicalRemoveDialogComponent {
  private chemicalsService = inject(BackendConfigurationPnChemicalsService);
  private dialogRef = inject(MatDialogRef<ChemicalRemoveDialogComponent, boolean>);
  private discardContext = discardContext(this.dialogRef);
  readonly data: ChemicalRemoveDialogData = inject(MAT_DIALOG_DATA);

  readonly reasons = ChemicalRemovalReason;
  readonly hazardousWaste = reachedThreshold(this.data.row.entry?.useAndPossessionDeadline ?? null, this.data.today) === 'passed';
  reason: ChemicalRemovalReason | null = null;
  note = '';
  saving = false;

  get canSave(): boolean {
    return (this.reason === ChemicalRemovalReason.Used || this.reason === ChemicalRemovalReason.Disposed) && !this.saving;
  }

  get dirty(): boolean {
    return this.reason !== null || this.note.trim() !== '';
  }

  cancel(): void {
    cancelPlacementDialog(this.discardContext, {
      busy: this.saving, dirty: this.dirty, productName: this.data.row.productName, confirmId: 'chemicalRemoveDiscardConfirm',
    });
  }

  save(): void {
    if (!this.canSave) {
      return;
    }
    this.saving = true;
    closeWhenSaved(this.dialogRef, this.chemicalsService.removePlacement(this.data.row.placement.id, {
      reason: this.reason, removedAt: null, note: this.note.trim(),
    }), () => (this.saving = false));
  }
}
