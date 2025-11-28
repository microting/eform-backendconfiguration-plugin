import {Component, EventEmitter, OnDestroy, OnInit, Output,
  inject
} from '@angular/core';
import {
  WorkOrderCaseModel,
} from '../../../../../models';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';

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

  @Output() workOrderCaseDelete: EventEmitter<number> = new EventEmitter<number>();

  

  ngOnInit(): void {
  }

  hide() {
    this.dialogRef.close();
  }

  delete() {
    this.workOrderCaseDelete.emit(this.workOrderCase.id);
  }

  ngOnDestroy(): void {
  }
}
