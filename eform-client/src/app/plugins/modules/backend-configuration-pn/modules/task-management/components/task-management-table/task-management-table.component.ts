import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnInit,
  Output,
} from '@angular/core';
import { TableHeaderElementModel } from 'src/app/common/models';
import { WorkOrderCaseModel } from '../../../../models';
import {
  TaskManagementStateService
} from '../store';

@Component({
  selector: 'app-task-management-table',
  templateUrl: './task-management-table.component.html',
  styleUrls: ['./task-management-table.component.scss'],
})
export class TaskManagementTableComponent implements OnInit {
  tableHeaders: TableHeaderElementModel[] = [
    { name: 'Id', sortable: false },
    { name: 'CreatedAt', visibleName: 'CreatedDate', sortable: true },
    { name: 'Property', sortable: false },
    { name: 'Area', sortable: false },
    { name: 'CreatedByName', visibleName: 'Created by 1', sortable: false },
    { name: 'CreatedByText', visibleName: 'Created by 2', sortable: false },
    { name: 'LastAssignedTo', sortable: false },
    { name: 'Actions', elementId: '', sortable: false },
    { name: 'Description', sortable: false },
    { name: 'UpdatedAt', visibleName: 'LastUpdateDate', sortable: true },
    { name: 'LastUpdateBy', sortable: false },
    { name: 'Status', sortable: false },
  ];
  @Input() workOrderCases: WorkOrderCaseModel[];
  @Output() updateTable: EventEmitter<void> = new EventEmitter<void>();
  @Output() openViewModal: EventEmitter<number> = new EventEmitter<number>();
  @Output() openDeleteModal: EventEmitter<WorkOrderCaseModel> = new EventEmitter<WorkOrderCaseModel>();

  constructor(public taskManagementStateService: TaskManagementStateService) {}

  ngOnInit(): void {}

  sortTable(sort: string) {
    this.taskManagementStateService.onSortTable(sort);
    this.updateTable.emit();
  }

  onOpenViewModal(id: number) {
    this.openViewModal.emit(id);
  }

  onOpenDeleteModal(workOrderCaseModel: WorkOrderCaseModel) {
    this.openDeleteModal.emit(workOrderCaseModel);
  }
}
