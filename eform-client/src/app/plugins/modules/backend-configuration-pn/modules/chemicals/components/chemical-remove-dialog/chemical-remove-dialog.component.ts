import {Component, inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {finalize} from 'rxjs/operators';
import {ChemicalRemovalReason} from '../../../../models';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {ChemicalInventoryRow, reachedThreshold} from '../../helpers';

export interface ChemicalRemoveDialogData {
  row: ChemicalInventoryRow;
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
  private dialogRef = inject(MatDialogRef<ChemicalRemoveDialogComponent>);
  readonly data: ChemicalRemoveDialogData = inject(MAT_DIALOG_DATA);

  readonly reasons = ChemicalRemovalReason;
  readonly hazardousWaste = reachedThreshold(this.data.row.entry?.useAndPossessionDeadline ?? null, this.data.today) === 'passed';
  reason: ChemicalRemovalReason | null = null;
  note = '';
  saving = false;

  get canSave(): boolean {
    return (this.reason === ChemicalRemovalReason.Used || this.reason === ChemicalRemovalReason.Disposed) && !this.saving;
  }

  cancel(): void {
    this.dialogRef.close(false);
  }

  save(): void {
    if (!this.canSave) {
      return;
    }
    this.saving = true;
    this.chemicalsService.removePlacement(this.data.row.placement.id, {reason: this.reason, removedAt: null, note: this.note.trim()})
      .pipe(finalize(() => (this.saving = false)))
      .subscribe((res) => {
        if (res && res.success) {
          this.dialogRef.close(true);
        }
      });
  }
}
