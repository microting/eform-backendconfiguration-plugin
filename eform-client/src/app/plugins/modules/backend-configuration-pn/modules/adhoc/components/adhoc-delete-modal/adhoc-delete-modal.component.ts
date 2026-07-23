import {Component, Inject, OnDestroy, OnInit} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {Subscription} from 'rxjs';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {BackendConfigurationPnAdhocService} from '../../../../services';

export interface AdhocDeleteModalData {
  id: number;
  title: string;
}

/**
 * Delete confirmation (M5/F8) - pattern:
 * `task-management-delete-modal.component.ts`. Only needs the task's id +
 * title (not the full `AdhocTaskModel`) so both the table (which has the
 * full model) and the Historik timeline (which only has
 * `AdhocTaskHistoryEventModel.taskId`/`taskTitle`) can open this the same
 * way.
 */
@AutoUnsubscribe()
@Component({
  selector: 'app-adhoc-delete-modal',
  templateUrl: './adhoc-delete-modal.component.html',
  styleUrls: ['./adhoc-delete-modal.component.scss'],
  standalone: false,
})
export class AdhocDeleteModalComponent implements OnInit, OnDestroy {
  deleteTaskSub$: Subscription;

  constructor(
    public dialogRef: MatDialogRef<AdhocDeleteModalComponent>,
    @Inject(MAT_DIALOG_DATA) public data: AdhocDeleteModalData,
    private adhocService: BackendConfigurationPnAdhocService,
  ) {
  }

  ngOnInit(): void {
  }

  ngOnDestroy(): void {
  }

  hide(): void {
    this.dialogRef.close(false);
  }

  delete(): void {
    this.deleteTaskSub$ = this.adhocService.deleteTask(this.data.id).subscribe((res) => {
      if (res && res.success) {
        this.dialogRef.close(true);
      }
    });
  }
}
