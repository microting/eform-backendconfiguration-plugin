import {Component, inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef, MatDialogTitle, MatDialogContent, MatDialogActions} from '@angular/material/dialog';
import {NgIf} from '@angular/common';
import {TranslatePipe} from '@ngx-translate/core';

export interface ArchiveMoveConfirmData {
  /** "3 dokumenter kan nu læses af Ledelse" — already translated. */
  text: string;
  /** False for a non-admin: the move would change access, which only an admin may do (D17). */
  canConfirm: boolean;
}

/** D11: a move that changes who can read the documents is confirmed first. Closes with true only on confirm. */
@Component({
  selector: 'app-archive-move-confirm-dialog',
  imports: [NgIf, TranslatePipe, MatDialogTitle, MatDialogContent, MatDialogActions],
  templateUrl: './archive-move-confirm-dialog.component.html',
})
export class ArchiveMoveConfirmDialogComponent {
  readonly data: ArchiveMoveConfirmData = inject(MAT_DIALOG_DATA);
  private dialogRef = inject(MatDialogRef<ArchiveMoveConfirmDialogComponent, boolean>);

  confirm(): void {
    this.dialogRef.close(true);
  }

  cancel(): void {
    this.dialogRef.close(false);
  }
}
