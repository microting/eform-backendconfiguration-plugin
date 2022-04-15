import {Component, EventEmitter, OnDestroy, OnInit, Output, ViewChild} from '@angular/core';
import {
  WorkOrderCaseModel,
} from '../../../../../models';
import { AutoUnsubscribe } from 'ngx-auto-unsubscribe';

@AutoUnsubscribe()
@Component({
  selector: 'app-task-management-delete-modal',
  templateUrl: './task-management-delete-modal.component.html',
  styleUrls: ['./task-management-delete-modal.component.scss'],
})
export class TaskManagementDeleteModalComponent implements OnInit, OnDestroy {
  @ViewChild('frame', { static: false }) frame;
  @Output() workOrderCaseDelete: EventEmitter<number> = new EventEmitter<number>();
  workOrderCase: WorkOrderCaseModel = new WorkOrderCaseModel();

  constructor() {}

  ngOnInit(): void {}

  show(workOrderCase: WorkOrderCaseModel) {
    this.workOrderCase = workOrderCase;
    this.frame.show();
  }

  hide() {
    this.frame.hide();
  }

  delete() {
    this.workOrderCaseDelete.emit(this.workOrderCase.id);
  }

  ngOnDestroy(): void {}
}
