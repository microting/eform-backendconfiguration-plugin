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
    { name: 'Id', sortable: true },
    { name: 'CaseInitiated', visibleName: 'CreatedDate', sortable: true },
    { name: 'PropertyName', visibleName: 'Property', sortable: true },
    { name: 'SelectedAreaName', visibleName: 'Area',sortable: true },
    { name: 'CreatedByName', visibleName: 'Created by 1', sortable: true },
    { name: 'CreatedByText', visibleName: 'Created by 2', sortable: true },
    { name: 'LastAssignedToName', visibleName: 'LastAssignedTo',sortable: true },
    { name: 'Actions', elementId: '', sortable: false },
    { name: 'Description', sortable: false },
    { name: 'UpdatedAt', visibleName: 'LastUpdateDate', sortable: true },
    { name: 'LastUpdatedByName', visibleName: 'LastUpdateBy', sortable: false },
    { name: 'CaseStatusesEnum', visibleName: 'Status', sortable: true },
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
