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
    { name: 'CreatedDate', sortable: false },
    { name: 'Property', sortable: false },
    { name: 'Area', sortable: false },
    { name: 'CreatedByName', visibleName: 'Created by 1', sortable: false },
    { name: 'CreatedByText', visibleName: 'Created by 2', sortable: false },
    { name: 'Last assigned to', sortable: false },
    { name: 'Actions', elementId: '', sortable: false },
    { name: 'Description', sortable: false },
    { name: 'LastUpdateDate', sortable: false },
    { name: 'LastUpdateBy', sortable: false },
    { name: 'Status', sortable: false },
  ];
  @Input() workOrderCases: WorkOrderCaseModel[];
  @Output() updateTable: EventEmitter<void> = new EventEmitter<void>();
  @Output() openViewModal: EventEmitter<number> = new EventEmitter<number>();

  constructor(public taskManagementStateService: TaskManagementStateService) {}

  ngOnInit(): void {}

  sortTable(sort: string) {
    this.taskManagementStateService.onSortTable(sort);
    this.updateTable.emit();
  }

  onOpenViewModal(id: number) {
    this.openViewModal.emit(id);
  }
}
