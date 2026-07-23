import {Component, Inject, OnDestroy, OnInit} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {ToastrService} from 'ngx-toastr';
import {TranslateService} from '@ngx-translate/core';
import {Subscription} from 'rxjs';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {AdhocWorkerModel} from '../../../../models';
import {BackendConfigurationPnAdhocService} from '../../../../services';
import {AdhocStateService} from '../store';

export interface AdhocCompleteModalData {
  id: number;
  propertyId: number;
  title: string;
}

/**
 * "Udfør opgave" modal (M5/F8) - performer select (workers of the task's
 * property) -> `setCompleted(id, true, completedByWorkerId)`. No PIN field
 * (deviation from the mockup's `#udfør-modal`, which has an optional PIN
 * input - out of scope for the dashboard per the M5 plan). Success toast
 * via `ToastrService` (repo convention across every plugin - confirmed no
 * plugin under `plugins/modules` uses `MatSnackBar`).
 */
@AutoUnsubscribe()
@Component({
  selector: 'app-adhoc-complete-modal',
  templateUrl: './adhoc-complete-modal.component.html',
  styleUrls: ['./adhoc-complete-modal.component.scss'],
  standalone: false,
})
export class AdhocCompleteModalComponent implements OnInit, OnDestroy {
  workers: AdhocWorkerModel[] = [];
  completedByWorkerId: number | null = null;

  workersSub$: Subscription;
  completeSub$: Subscription;

  constructor(
    public dialogRef: MatDialogRef<AdhocCompleteModalComponent>,
    @Inject(MAT_DIALOG_DATA) public data: AdhocCompleteModalData,
    private adhocService: BackendConfigurationPnAdhocService,
    private adhocStateService: AdhocStateService,
    private toastrService: ToastrService,
    private translate: TranslateService,
  ) {
  }

  ngOnInit(): void {
    this.workersSub$ = this.adhocStateService.getWorkersForProperty(this.data.propertyId).subscribe((workers) => (this.workers = workers));
  }

  ngOnDestroy(): void {
  }

  cancel(): void {
    this.dialogRef.close(false);
  }

  complete(): void {
    this.completeSub$ = this.adhocService.setCompleted(this.data.id, true, this.completedByWorkerId).subscribe((res) => {
      if (res && res.success) {
        this.toastrService.success(`${this.translate.instant('Complete task')}: ${this.data.title}`);
        this.dialogRef.close(true);
      }
    });
  }
}
