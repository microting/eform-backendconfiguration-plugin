import {Component, inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef, MatDialogTitle, MatDialogContent, MatDialogActions} from '@angular/material/dialog';
import {ArchiveFolderOption} from '../../../../helpers';
import {TranslatePipe} from '@ngx-translate/core';
import {ArchiveFolderPickerComponent} from '../archive-folder-picker/archive-folder-picker.component';

export interface ArchiveFolderChooseDialogData {
  title: string;
  titleParams?: Record<string, string | number>;
  options: ArchiveFolderOption[];
  selectedId: number | null;
}

/** "Flyt til mappe" for documents and "Flyt" for a folder. Closes with the chosen id (ARCHIVE_TOP_LEVEL_ID = top level). */
@Component({
  selector: 'app-archive-folder-choose-dialog',
  imports: [TranslatePipe, MatDialogTitle, MatDialogContent, MatDialogActions, ArchiveFolderPickerComponent],
  templateUrl: './archive-folder-choose-dialog.component.html',
})
export class ArchiveFolderChooseDialogComponent {
  readonly data: ArchiveFolderChooseDialogData = inject(MAT_DIALOG_DATA);
  private dialogRef = inject(MatDialogRef<ArchiveFolderChooseDialogComponent, number>);

  selectedId: number | null = this.data.selectedId;

  choose(): void {
    if (this.selectedId !== null) {
      this.dialogRef.close(this.selectedId);
    }
  }

  cancel(): void {
    this.dialogRef.close();
  }
}
