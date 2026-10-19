import {Component, inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef, MatDialogTitle, MatDialogContent, MatDialogActions} from '@angular/material/dialog';
import {CommonDictionaryModel} from 'src/app/common/models';
import {FormsModule} from '@angular/forms';
import {TranslatePipe} from '@ngx-translate/core';
import {MatFormField, MatLabel} from '@angular/material/form-field';
import {MtxSelect} from '@ng-matero/extensions/select';

export interface ArchivePropertiesDialogData {
  properties: CommonDictionaryModel[];
  count: number;
}

/** "Ejendomme" in the selection bar: replaces the properties of every selected document. */
@Component({
  selector: 'app-archive-properties-dialog',
  imports: [FormsModule, TranslatePipe, MatFormField, MatLabel, MtxSelect, MatDialogTitle, MatDialogContent, MatDialogActions],
  templateUrl: './archive-properties-dialog.component.html',
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
