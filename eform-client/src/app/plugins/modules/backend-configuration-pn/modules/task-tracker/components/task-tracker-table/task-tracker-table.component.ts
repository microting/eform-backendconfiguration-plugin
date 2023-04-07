import {
  Component,
  EventEmitter,
  Input,
  OnInit,
  Output,
} from '@angular/core';
import {TaskModel} from '../../../../models';
import {
  TaskTrackerStateService
} from '../store';
import {MtxGridColumn} from '@ng-matero/extensions/grid';
import {TranslateService} from '@ngx-translate/core';

@Component({
  selector: 'app-task-tracker-table',
  templateUrl: './task-tracker-table.component.html',
  styleUrls: ['./task-tracker-table.component.scss'],
})
export class TaskTrackerTableComponent implements OnInit {
  tableHeaders: MtxGridColumn[] = [
    {header: this.translateService.stream('Property'), field: 'propertyName'},
    {header: this.translateService.stream('Task'), field: 'taskName'},
    {header: this.translateService.stream('Tags'), field: 'tags'},
    {header: this.translateService.stream('Workers'), field: 'workers'},
    {
      header: this.translateService.stream('Start'),
      field: 'startTask',
      type: 'date',
      typeParameter: {format: 'dd.MM.yyyy'},
    },
    {header: this.translateService.stream('Repeat'), field: 'repeatTypeTask'},
    {
      header: this.translateService.stream('Deadline'),
      field: 'deadlineTask',
      type: 'date',
      typeParameter: {format: 'dd.MM.yyyy'},
    },
  ];

  @Input() tasks: TaskModel[];
  @Output() updateTable: EventEmitter<void> = new EventEmitter<void>();
/*  @Output() openViewModal: EventEmitter<number> = new EventEmitter<number>();
  @Output() openDeleteModal: EventEmitter<WorkOrderCaseModel> = new EventEmitter<WorkOrderCaseModel>();*/

  constructor(
    public taskManagementStateService: TaskTrackerStateService,
    private translateService: TranslateService,
  ) {
  }

  ngOnInit(): void {
  }
}
