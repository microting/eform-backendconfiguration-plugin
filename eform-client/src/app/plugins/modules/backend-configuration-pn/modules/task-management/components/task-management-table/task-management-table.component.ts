import {
  Component,
  EventEmitter,
  Input,
  OnInit,
  Output,
} from '@angular/core';
import {WorkOrderCaseModel} from '../../../../models';
import {
  TaskManagementStateService
} from '../store';
import {Sort} from '@angular/material/sort';
import {MtxGridColumn} from '@ng-matero/extensions/grid';
import {TranslateService} from '@ngx-translate/core';
import {
  TaskManagementPrioritiesEnum
} from '../../../../enums';

@Component({
  selector: 'app-task-management-table',
  templateUrl: './task-management-table.component.html',
  styleUrls: ['./task-management-table.component.scss'],
})
export class TaskManagementTableComponent implements OnInit {
  tableHeaders: MtxGridColumn[] = [
    {header: this.translateService.stream('Id'), field: 'id', sortProp: {id: 'Id'}, sortable: true, class: 'id'},
    {
      header: this.translateService.stream('Created date'),
      field: 'caseInitiated',
      sortProp: {id: 'CaseInitiated'},
      sortable: true,
      type: 'date',
      typeParameter: {format: 'dd.MM.yyyy HH:mm'},
      class: 'createdDate'
    },
    {header: this.translateService.stream('Property'), field: 'propertyName', sortProp: {id: 'PropertyName'}, sortable: true, class: 'propertyName'},
    {header: this.translateService.stream('Area'), field: 'areaName', sortProp: {id: 'SelectedAreaName'}, sortable: true, class: 'areaName'},
    {header: this.translateService.stream('Created by'), field: 'createdByName', sortProp: {id: 'CreatedByName'}, sortable: true, class: 'createdByName'},
    {header: this.translateService.stream('Created by text'), field: 'createdByText', sortProp: {id: 'CreatedByText'}, sortable: true, class: 'createdByText'},
    {header: this.translateService.stream('Last assigned to'), field: 'lastAssignedTo', sortProp: {id: 'LastAssignedToName'}, sortable: true, class: 'lastAssignedTo'},
    {
      header: this.translateService.stream('Description'),
      field: 'description',
      formatter: (rowData: WorkOrderCaseModel) => rowData.description,
      class: 'description',
    },
    {
      header: this.translateService.stream('Last update date'),
      field: 'lastUpdateDate',
      sortProp: {id: 'UpdatedAt'},
      sortable: true,
      type: 'date',
      typeParameter: {format: 'dd.MM.yyyy HH:mm'},
      class: 'lastUpdateDate'
    },
    {header: this.translateService.stream('Last update by'), field: 'lastUpdatedBy', sortProp: {id: 'LastUpdatedByName'}, sortable: true, class: 'lastUpdatedBy'},
    {header: this.translateService.stream('Priority'),
      field: 'priority',
      sortProp: {id: 'Priority'},
      sortable: true,
      class: 'priority',
      formatter: (rowData: WorkOrderCaseModel) => this.translateService.instant(TaskManagementPrioritiesEnum[rowData.priority])},
    {
      header: this.translateService.stream('Status'),
      field: 'status',
      sortProp: {id: 'CaseStatusesEnum'},
      sortable: true,
      formatter: (rowData: WorkOrderCaseModel) => `<span>${this.translateService.instant(rowData.status)}</span>`,
      class: 'status'
    },
    {
      header: this.translateService.stream('Actions'),
      field: 'actions',
      type: 'button',
      buttons: [
        {
          type: 'icon',
          icon: 'edit',
          click: (rowData: WorkOrderCaseModel) => this.onOpenViewModal(rowData.id),
          tooltip: this.translateService.stream('Edit task'),
          class: 'taskManagementViewBtn',
        },
        {
          type: 'icon',
          icon: 'delete',
          color: 'warn',
          click: (rowData: WorkOrderCaseModel) => this.onOpenDeleteModal(rowData),
          tooltip: this.translateService.stream('Delete task'),
          class: 'taskManagementDeleteTaskBtn',
        },
      ]
    },
  ];

  @Input() workOrderCases: WorkOrderCaseModel[];
  @Output() updateTable: EventEmitter<void> = new EventEmitter<void>();
  @Output() openViewModal: EventEmitter<number> = new EventEmitter<number>();
  @Output() openDeleteModal: EventEmitter<WorkOrderCaseModel> = new EventEmitter<WorkOrderCaseModel>();

  constructor(
    public taskManagementStateService: TaskManagementStateService,
    private translateService: TranslateService,
  ) {
  }

  ngOnInit(): void {
  }

  sortTable(sort: Sort) {
    this.taskManagementStateService.onSortTable(sort.active);
    this.updateTable.emit();
  }

  onOpenViewModal(id: number) {
    this.openViewModal.emit(id);
  }

  onOpenDeleteModal(workOrderCaseModel: WorkOrderCaseModel) {
    this.openDeleteModal.emit(workOrderCaseModel);
  }
}
