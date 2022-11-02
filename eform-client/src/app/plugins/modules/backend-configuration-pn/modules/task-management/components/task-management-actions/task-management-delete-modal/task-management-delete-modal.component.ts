import {Component, EventEmitter, Inject, OnDestroy, OnInit, Output,} from '@angular/core';
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
})
export class TaskManagementDeleteModalComponent implements OnInit, OnDestroy {
  @Output() workOrderCaseDelete: EventEmitter<number> = new EventEmitter<number>();

  constructor(
    public dialogRef: MatDialogRef<TaskManagementDeleteModalComponent>,
    @Inject(MAT_DIALOG_DATA) public workOrderCase: WorkOrderCaseModel = new WorkOrderCaseModel(),
  ) {
  }

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
