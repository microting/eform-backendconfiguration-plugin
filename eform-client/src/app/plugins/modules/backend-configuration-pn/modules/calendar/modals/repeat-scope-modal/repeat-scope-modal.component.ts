import {Component, Inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {RepeatEditScope} from '../../../../models/calendar';

export interface RepeatScopeModalData {
  mode: 'edit' | 'delete';
}

@Component({
  standalone: false,
  selector: 'app-repeat-scope-modal',
  templateUrl: './repeat-scope-modal.component.html',
})
export class RepeatScopeModalComponent {
  scope: RepeatEditScope = 'this';

  constructor(
    private dialogRef: MatDialogRef<RepeatScopeModalComponent>,
    @Inject(MAT_DIALOG_DATA) public data: RepeatScopeModalData,
  ) {}

  onConfirm() {
    this.dialogRef.close(this.scope);
  }

  onCancel() {
    this.dialogRef.close(null);
  }
}
