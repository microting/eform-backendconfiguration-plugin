import {Component} from '@angular/core';
import {MatDialogRef} from '@angular/material/dialog';

/**
 * "The eForm change applies to the whole series" confirmation.
 *
 * The eForm is a series-level property: the backend re-points every
 * uncompleted occurrence (including past/overdue ones) at the new eForm,
 * regardless of the edit scope the user picked in `RepeatScopeModalComponent`.
 * Only completed occurrences keep their historical eForm. The modal is opened
 * from `TaskCreateEditModalComponent` when the eForm differs from the value
 * loaded into the dialog and the picked scope is narrower than "all", so the
 * user learns about the wider blast radius *before* the save is sent.
 *
 * Like `AdhocPhotoDeleteModalComponent` this is a pure confirm dialog: it owns
 * no service calls and only closes with `true`/`false`; the caller performs
 * (or aborts) the save.
 */
@Component({
  standalone: false,
  selector: 'app-eform-change-scope-modal',
  templateUrl: './eform-change-scope-modal.component.html',
})
export class EformChangeScopeModalComponent {
  constructor(private dialogRef: MatDialogRef<EformChangeScopeModalComponent, boolean>) {}

  onConfirm() {
    this.dialogRef.close(true);
  }

  onCancel() {
    this.dialogRef.close(false);
  }
}
