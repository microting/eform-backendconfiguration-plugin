import {Component} from '@angular/core';
import {MatDialogRef, MatDialogTitle, MatDialogContent, MatDialogActions} from '@angular/material/dialog';
import {TranslatePipe} from '@ngx-translate/core';

/**
 * "Slet billede?" confirmation (#1100) - gates BOTH photo-delete flavours in
 * the task drawer (existing/server-side photos and queued create-mode
 * previews). Unlike `AdhocDeleteModalComponent` (which performs the task
 * delete itself), this modal is a pure confirm: it only closes with
 * `true`/`false` and the drawer performs the actual removal - existing-photo
 * removal is merely staged locally (`removedPhotoIds`, applied on save) and
 * queued-photo removal is a local array splice, so there is no service call
 * to share here.
 */
@Component({
  selector: 'app-adhoc-photo-delete-modal',
  imports: [TranslatePipe, MatDialogTitle, MatDialogContent, MatDialogActions],
  templateUrl: './adhoc-photo-delete-modal.component.html',
  styleUrls: ['./adhoc-photo-delete-modal.component.scss'],
})
export class AdhocPhotoDeleteModalComponent {
  constructor(public dialogRef: MatDialogRef<AdhocPhotoDeleteModalComponent, boolean>) {
  }

  hide(): void {
    this.dialogRef.close(false);
  }

  confirm(): void {
    this.dialogRef.close(true);
  }
}
