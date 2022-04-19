import {Component, OnDestroy, OnInit, ViewChild} from '@angular/core';
import { AutoUnsubscribe } from 'ngx-auto-unsubscribe';
import { WorkOrderCaseModel } from '../../../../models';
import {TaskManagementStateService} from '../store';
import {TaskManagementCreateShowModalComponent, TaskManagementDeleteModalComponent} from '../';
import {BackendConfigurationPnTaskManagementService} from 'src/app/plugins/modules/backend-configuration-pn/services';

@AutoUnsubscribe()
@Component({
  selector: 'app-task-management-container',
  templateUrl: './task-management-container.component.html',
  styleUrls: ['./task-management-container.component.scss'],
})
export class TaskManagementContainerComponent implements OnInit, OnDestroy {
  @ViewChild('showCreateModal', {static: true}) showCreateModal: TaskManagementCreateShowModalComponent;
  @ViewChild('deleteModal', {static: true}) deleteModal: TaskManagementDeleteModalComponent;

  workOrderCases: WorkOrderCaseModel[] = [];

  constructor(
    public taskManagementStateService: TaskManagementStateService,
    public taskManagementService: BackendConfigurationPnTaskManagementService,
  ) {}

  ngOnInit() {
  }

  ngOnDestroy(): void {}

  updateTable() {
    this.taskManagementStateService.getAllWorkOrderCases()
      .subscribe((data) => {
        if(data && data.success && data.model){
          this.workOrderCases = data.model;
        }
      })
  }

  openViewModal(workOrderCaseId: number) {
    this.taskManagementService.getWorkOrderCase(workOrderCaseId)
      .subscribe((data) => {
        if(data && data.success && data.model){
          this.showCreateModal.show(data.model);
        }
      })
  }

  openCreateModal() {
    this.showCreateModal.show();
  }

  openDeleteModal(workOrderCaseModel: WorkOrderCaseModel) {
    this.deleteModal.show(workOrderCaseModel);
  }

  deleteWorkOrderCaseModel(workOrderCaseId: number){
    this.taskManagementService.deleteWorkOrderCase(workOrderCaseId)
      .subscribe((data) => {
        if(data && data.success) {
          this.deleteModal.hide();
          this.updateTable();
        }
    })
  }
}
