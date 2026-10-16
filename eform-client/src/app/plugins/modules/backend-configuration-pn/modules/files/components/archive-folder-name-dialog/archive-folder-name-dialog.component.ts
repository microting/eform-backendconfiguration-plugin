import {Component, inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';

export interface ArchiveFolderNameDialogData {
  /** Translation key, e.g. 'New subfolder in {{folder}}'. */
  title: string;
  titleParams?: Record<string, string | number>;
  /** The current name when renaming. */
  name?: string;
}

/** Ny mappe / Ny undermappe / Omdøb. Closes with the trimmed name; the server checks uniqueness and depth. */
@Component({
  selector: 'app-archive-folder-name-dialog',
  templateUrl: './archive-folder-name-dialog.component.html',
  standalone: false,
})
export class ArchiveFolderNameDialogComponent {
  readonly data: ArchiveFolderNameDialogData = inject(MAT_DIALOG_DATA);
  private dialogRef = inject(MatDialogRef<ArchiveFolderNameDialogComponent, string>);

  /** ArchiveFolder.Name is at most 250 characters (spec section 3). */
  readonly maxLength = 250;
  name = this.data.name ?? '';

  get canSave(): boolean {
    const name = this.name.trim();
    return name.length > 0 && name.length <= this.maxLength && name !== (this.data.name ?? '').trim();
  }

  save(): void {
    if (this.canSave) {
      this.dialogRef.close(this.name.trim());
    }
  }

  cancel(): void {
    this.dialogRef.close();
  }
}
