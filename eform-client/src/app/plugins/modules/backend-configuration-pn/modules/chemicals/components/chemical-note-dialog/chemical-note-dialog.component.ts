import {Component, inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {finalize} from 'rxjs/operators';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {ChemicalInventoryRow} from '../../helpers';

/** "Ret placeringsnote". */
@Component({
  selector: 'app-chemical-note-dialog',
  templateUrl: './chemical-note-dialog.component.html',
  styleUrls: ['./chemical-note-dialog.component.scss'],
  standalone: false,
})
export class ChemicalNoteDialogComponent {
  private chemicalsService = inject(BackendConfigurationPnChemicalsService);
  private dialogRef = inject(MatDialogRef<ChemicalNoteDialogComponent>);
  readonly data: {row: ChemicalInventoryRow} = inject(MAT_DIALOG_DATA);

  note = this.data.row.placement.placementNote ?? '';
  saving = false;

  cancel(): void {
    this.dialogRef.close(false);
  }

  save(): void {
    if (this.saving) {
      return;
    }
    this.saving = true;
    this.chemicalsService.updatePlacementNote(this.data.row.placement.id, {placementNote: this.note.trim()})
      .pipe(finalize(() => (this.saving = false)))
      .subscribe((res) => {
        if (res && res.success) {
          this.dialogRef.close(true);
        }
      });
  }
}
