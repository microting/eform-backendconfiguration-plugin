import {Component, OnDestroy, OnInit, inject} from '@angular/core';
import {WorkOrderCaseModel} from '../../../../../models';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {BackendConfigurationPnTaskManagementService} from '../../../../../services';
import {Subscription} from 'rxjs';

@AutoUnsubscribe()
@Component({
    selector: 'app-task-management-delete-modal',
    templateUrl: './task-management-delete-modal.component.html',
    styleUrls: ['./task-management-delete-modal.component.scss'],
    standalone: false
})
export class TaskManagementDeleteModalComponent implements OnInit, OnDestroy {
  public dialogRef = inject(MatDialogRef<TaskManagementDeleteModalComponent>);
  public workOrderCase = inject<WorkOrderCaseModel>(MAT_DIALOG_DATA);
  private taskManagementService = inject(BackendConfigurationPnTaskManagementService);

  deleteWorkOrderCaseSub$: Subscription;

  ngOnInit(): void {
  }

  hide() {
    this.dialogRef.close();
  }

  delete() {
    this.deleteWorkOrderCaseSub$ = this.taskManagementService
      .deleteWorkOrderCase(this.workOrderCase.id)
      .subscribe((data) => {
        if (data && data.success) {
          this.dialogRef.close(true);
        }
      });
  }

  ngOnDestroy(): void {
  }
}
