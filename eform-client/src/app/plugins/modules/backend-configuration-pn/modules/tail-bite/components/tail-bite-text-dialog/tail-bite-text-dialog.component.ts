import {Overlay} from '@angular/cdk/overlay';
import {Component, inject} from '@angular/core';
import {FormsModule} from '@angular/forms';
import {MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef} from '@angular/material/dialog';
import {MatFormFieldModule} from '@angular/material/form-field';
import {MatInputModule} from '@angular/material/input';
import {TranslateModule} from '@ngx-translate/core';
import {Observable, filter} from 'rxjs';
import {dialogConfigHelper} from 'src/app/common/helpers';

/** `title` is already translated (it may name a location); `label` is a translation key. */
export interface TailBiteTextDialogData {
  title: string;
  label: string;
  value?: string;
  maxLength?: number;
}

/**
 * The one text input of the tail-bite pages: a name or a reason. The text is required and returned trimmed;
 * cancel closes with nothing. Pure confirmations use the platform's DeleteModalComponent instead.
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

  get valid(): boolean {
    return this.text.trim().length > 0;
  }

  cancel(): void {
    this.dialogRef.close();
  }

  save(): void {
    if (this.valid) {
      this.dialogRef.close(this.text.trim());
    }
  }
}

/** Opens the dialog; emits the trimmed text once when the user saves, nothing when they cancel. */
export function askText(dialog: MatDialog, overlay: Overlay, data: TailBiteTextDialogData): Observable<string> {
  return dialog
    .open<TailBiteTextDialogComponent, TailBiteTextDialogData, string>(TailBiteTextDialogComponent, dialogConfigHelper(overlay, data))
    .afterClosed()
    .pipe(filter((text): text is string => !!text));
}
