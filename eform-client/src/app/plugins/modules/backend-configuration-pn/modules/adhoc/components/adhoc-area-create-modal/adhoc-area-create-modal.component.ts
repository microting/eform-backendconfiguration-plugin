import {Component, Inject, OnDestroy} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {Subscription} from 'rxjs';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {AdhocStateService} from '../store';

export interface AdhocAreaCreateModalData {
  propertyId: number;
  propertyName: string;
}

/**
 * "Opret områder" batch modal (mockup #omraade-batch-modal): textarea, one
 * name per line, scoped to the currently selected property. Client trims
 * and drops empties; the server case-insensitively dedupes (silent skip,
 * idempotent). Closes with `true` when areas were submitted.
 */
@AutoUnsubscribe()
@Component({
  selector: 'app-adhoc-area-create-modal',
  templateUrl: './adhoc-area-create-modal.component.html',
  styleUrls: ['./adhoc-area-create-modal.component.scss'],
  standalone: false,
})
export class AdhocAreaCreateModalComponent implements OnDestroy {
  namesText = '';
  saving = false;
  createSub$: Subscription;

  constructor(
    public dialogRef: MatDialogRef<AdhocAreaCreateModalComponent>,
    @Inject(MAT_DIALOG_DATA) public data: AdhocAreaCreateModalData,
    private adhocStateService: AdhocStateService,
  ) {
  }

  ngOnDestroy(): void {
  }

  get parsedNames(): string[] {
    return this.namesText
      .split('\n')
      .map((n) => n.trim())
      .filter((n) => n.length > 0);
  }

  hide(): void {
    this.dialogRef.close(false);
  }

  save(): void {
    const names = this.parsedNames;
    if (names.length === 0 || this.saving) {
      return;
    }
    this.saving = true;
    this.createSub$ = this.adhocStateService
      .createAreas(this.data.propertyId, names)
      .subscribe({
        next: () => this.dialogRef.close(true),
        error: () => (this.saving = false),
      });
  }
}
