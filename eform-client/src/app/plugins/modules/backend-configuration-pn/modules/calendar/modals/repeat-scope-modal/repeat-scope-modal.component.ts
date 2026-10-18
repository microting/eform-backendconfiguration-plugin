import {Component, Inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef, MatDialogTitle, MatDialogContent, MatDialogActions} from '@angular/material/dialog';
import {RepeatDeleteScope, RepeatEditScope} from '../../../../models/calendar';
import {NgIf} from '@angular/common';
import {FormsModule} from '@angular/forms';
import {TranslatePipe} from '@ngx-translate/core';
import {MatRadioGroup, MatRadioButton} from '@angular/material/radio';

export interface RepeatScopeModalData {
  mode: 'edit' | 'delete' | 'move';
}

@Component({
  selector: 'app-repeat-scope-modal',
  imports: [NgIf, FormsModule, TranslatePipe, MatDialogTitle, MatDialogContent, MatDialogActions, MatRadioGroup, MatRadioButton],
  templateUrl: './repeat-scope-modal.component.html',
})
export class RepeatScopeModalComponent {
  scope: RepeatEditScope | RepeatDeleteScope = 'this';

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
