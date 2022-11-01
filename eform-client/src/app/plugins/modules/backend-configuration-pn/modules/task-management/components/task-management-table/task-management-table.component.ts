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

@Component({
  selector: 'app-task-management-table',
  templateUrl: './task-management-table.component.html',
  styleUrls: ['./task-management-table.component.scss'],
})
export class TaskManagementTableComponent implements OnInit {
  tableHeaders: MtxGridColumn[] = [
    {header: this.translateService.stream('Id'), field: 'id', sortProp: {id: 'Id'}, sortable: true},
    {
      header: this.translateService.stream('CreatedDate'),
      field: 'caseInitiated',
      sortProp: {id: 'CaseInitiated'},
      sortable: true,
      type: 'date',
      typeParameter: {format: 'dd.MM.yyyy'}
    },
    {header: this.translateService.stream('Property'), field: 'propertyName', sortProp: {id: 'PropertyName'}, sortable: true},
    {header: this.translateService.stream('Area'), field: 'areaName', sortProp: {id: 'SelectedAreaName'}, sortable: true},
    {header: this.translateService.stream('Created by 1'), field: 'createdByName', sortProp: {id: 'CreatedByName'}, sortable: true},
    {header: this.translateService.stream('Created by 2'), field: 'createdByText', sortProp: {id: 'CreatedByText'}, sortable: true},
    {header: this.translateService.stream('LastAssignedTo'), field: 'lastAssignedTo', sortProp: {id: 'LastAssignedToName'}, sortable: true},
    {
      header: this.translateService.stream('Actions'),
      field: 'actions',
      type: 'button',
      buttons: [
        {
          type: 'icon',
          icon: 'visibility',
          color: 'accent',
          click: (rowData: WorkOrderCaseModel) => this.onOpenViewModal(rowData.id),
          tooltip: this.translateService.stream('View task'),
        },
        {
          type: 'icon',
          icon: 'delete',
          color: 'warn',
          click: (rowData: WorkOrderCaseModel) => this.onOpenDeleteModal(rowData),
          tooltip: this.translateService.stream('Delete task'),
        },
      ]
    },
    {
      header: this.translateService.stream('Description'),
      field: 'description',
      formatter: (rowData: WorkOrderCaseModel) => rowData.description
    },
    {
      header: this.translateService.stream('LastUpdateDate'),
      field: 'lastUpdateDate',
      sortProp: {id: 'UpdatedAt'},
      sortable: true,
      type: 'date',
      typeParameter: {format: 'dd.MM.yyyy'}
    },
    {header: this.translateService.stream('LastUpdateBy'), field: 'lastUpdatedBy', sortProp: {id: 'LastUpdatedByName'}, sortable: true},
    {
      header: this.translateService.stream('Status'),
      field: 'status',
      sortProp: {id: 'CaseStatusesEnum'},
      sortable: true,
      formatter: (rowData: WorkOrderCaseModel) => `<p>${this.translateService.instant(rowData.status)}</p>`
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
