import {Component, inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {CommonDictionaryModel} from 'src/app/common/models';

export interface ArchivePropertiesDialogData {
  properties: CommonDictionaryModel[];
  count: number;
}

/** "Ejendomme" in the selection bar: replaces the properties of every selected document. */
@Component({
  selector: 'app-archive-properties-dialog',
  templateUrl: './archive-properties-dialog.component.html',
  standalone: false,
})
export class ArchivePropertiesDialogComponent {
  readonly data: ArchivePropertiesDialogData = inject(MAT_DIALOG_DATA);
  private dialogRef = inject(MatDialogRef<ArchivePropertiesDialogComponent, number[]>);

  readonly replacesKey = this.data.count === 1
    ? 'Replaces the properties of {{count}} document.'
    : 'Replaces the properties of {{count}} documents.';
  propertyIds: number[] = [];

  save(): void {
    if (this.propertyIds.length) {
      this.dialogRef.close(this.propertyIds);
    }
  }

  cancel(): void {
    this.dialogRef.close();
  }
}
