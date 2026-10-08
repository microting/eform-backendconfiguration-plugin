import {Overlay} from '@angular/cdk/overlay';
import {Component, EventEmitter, inject} from '@angular/core';
import {FormsModule} from '@angular/forms';
import {MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef} from '@angular/material/dialog';
import {MatFormFieldModule} from '@angular/material/form-field';
import {MatInputModule} from '@angular/material/input';
import {TranslateModule} from '@ngx-translate/core';
import {Observable} from 'rxjs';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {OperationResult} from 'src/app/common/models';
import {closeOnSuccess} from '../../shared/tail-bite-confirm';

/** `title` is already translated (it may name a location); `label` is a translation key. */
export interface TailBiteTextDialogData {
  title: string;
  label: string;
  value?: string;
  maxLength?: number;
}

/**
 * The one text input of the tail-bite pages: a name or a reason. The text is required and is handed on trimmed
 * through `saved`; the dialog stays open (the text with it) until askText closes it after the action succeeded.
 * Cancel closes with nothing. Pure confirmations use the platform's DeleteModalComponent instead.
 */
@Component({
  selector: 'app-tail-bite-text-dialog',
  templateUrl: './tail-bite-text-dialog.component.html',
  imports: [FormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, TranslateModule],
})
export class TailBiteTextDialogComponent {
  public data = inject<TailBiteTextDialogData>(MAT_DIALOG_DATA);
  private dialogRef = inject(MatDialogRef<TailBiteTextDialogComponent, string>);

  text = this.data.value ?? '';
  /** The trimmed text, each time the user saves a valid one. */
  readonly saved = new EventEmitter<string>();

  get valid(): boolean {
    return this.text.trim().length > 0;
  }

  cancel(): void {
    this.dialogRef.close();
  }

  save(): void {
    if (this.valid) {
      this.saved.emit(this.text.trim());
    }
  }
}

/**
 * Opens the dialog and runs `action` with the trimmed text on save, as openConfirm does for a confirmation. Emits once,
 * after the action succeeded and the dialog closed; completes without emitting when the user cancels. A refused or
 * failed action leaves the dialog open with the typed text (the toast shows why), so the user can fix it and retry;
 * a failed request's error is absorbed here so Save keeps working.
 */
export function askText(dialog: MatDialog, overlay: Overlay, data: TailBiteTextDialogData,
  action: (text: string) => Observable<OperationResult>): Observable<void> {
  const ref = dialog.open<TailBiteTextDialogComponent, TailBiteTextDialogData>(TailBiteTextDialogComponent,
    dialogConfigHelper(overlay, data));
  return closeOnSuccess(ref, ref.componentInstance.saved, action);
}
