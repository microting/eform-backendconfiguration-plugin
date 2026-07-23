import {Component, Inject, OnDestroy, OnInit} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {Subscription} from 'rxjs';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {AdhocTaskModel} from '../../../../models';
import {BackendConfigurationPnAdhocService} from '../../../../services';

export interface AdhocCopyModalData {
  id: number;
  title: string;
}

/**
 * "Kopier opgave" confirmation (M5/F8) - mockup: Annuller / Nej (uden
 * kommentarer) / Ja (med kommentarer). On success the dialog closes with
 * the newly-created copy (`AdhocTaskModel`) so the caller can open it
 * straight into the edit drawer (mockup behavior: a copy always opens in
 * rediger mode for the user to adjust before it's "real").
 */
@AutoUnsubscribe()
@Component({
  selector: 'app-adhoc-copy-modal',
  templateUrl: './adhoc-copy-modal.component.html',
  styleUrls: ['./adhoc-copy-modal.component.scss'],
  standalone: false,
})
export class AdhocCopyModalComponent implements OnInit, OnDestroy {
  copyTaskSub$: Subscription;

  constructor(
    public dialogRef: MatDialogRef<AdhocCopyModalComponent, AdhocTaskModel | false>,
    @Inject(MAT_DIALOG_DATA) public data: AdhocCopyModalData,
    private adhocService: BackendConfigurationPnAdhocService,
  ) {
  }

  ngOnInit(): void {
  }

  ngOnDestroy(): void {
  }

  cancel(): void {
    this.dialogRef.close(false);
  }

  copy(includeComments: boolean): void {
    this.copyTaskSub$ = this.adhocService.copyTask(this.data.id, includeComments).subscribe((res) => {
      if (res && res.success && res.model) {
        this.dialogRef.close(res.model);
      }
    });
  }
}
