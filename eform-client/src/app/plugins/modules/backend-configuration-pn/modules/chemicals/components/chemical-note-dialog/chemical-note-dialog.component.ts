import {Component, inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {ChemicalPlacementDialogData, cancelPlacementDialog, closeWhenSaved, discardContext} from '../../helpers';

/** "Ret placeringsnote". */
@Component({
  selector: 'app-chemical-note-dialog',
  templateUrl: './chemical-note-dialog.component.html',
  styleUrls: ['./chemical-note-dialog.component.scss'],
  standalone: false,
})
export class ChemicalNoteDialogComponent {
  private chemicalsService = inject(BackendConfigurationPnChemicalsService);
  private dialogRef = inject(MatDialogRef<ChemicalNoteDialogComponent, boolean>);
  private discardContext = discardContext(this.dialogRef);
  readonly data: ChemicalPlacementDialogData = inject(MAT_DIALOG_DATA);

  private readonly original = this.data.row.placement.placementNote ?? '';
  note = this.original;
  saving = false;

  get canSave(): boolean {
    return !this.saving;
  }

  get dirty(): boolean {
    return this.note.trim() !== this.original.trim();
  }

  cancel(): void {
    cancelPlacementDialog(this.discardContext, {
      busy: this.saving, dirty: this.dirty, productName: this.data.row.productName, confirmId: 'chemicalNoteDiscardConfirm',
    });
  }

  save(): void {
    if (!this.canSave) {
      return;
    }
    this.saving = true;
    closeWhenSaved(this.dialogRef, this.chemicalsService.updatePlacementNote(this.data.row.placement.id, {placementNote: this.note.trim()}),
      () => (this.saving = false));
  }
}
