import {Component, OnDestroy, OnInit} from '@angular/core';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {
  TaskModel,
  Columns,
} from '../../../../models';
import {TaskTrackerStateService} from '../store';
import {
  TaskTrackerCreateShowModalComponent,
  TaskTrackerShownColumnsComponent
} from '../';
import {
  BackendConfigurationPnPropertiesService,
  BackendConfigurationPnTaskTrackerService
} from '../../../../services';
import {ToastrService} from 'ngx-toastr';
import {Subscription} from 'rxjs';
import {MatDialog} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {LoaderService} from 'src/app/common/services';

@AutoUnsubscribe()
@Component({
  selector: 'app-task-tracker-container',
  templateUrl: './task-tracker-container.component.html',
  styleUrls: ['./task-tracker-container.component.scss'],
})
export class TaskTrackerContainerComponent implements OnInit, OnDestroy {
  tasks: TaskModel[] = [];
  columns: Columns = {
    deadline: true,
    property: true,
    repeat: true,
    start: true,
    tags: true,
    task: true,
    workers: true,
  };

  getAllTasksSub$: Subscription;
  taskCreatedSub$: Subscription;
  columnsChangedSub$: Subscription;
  updateColumnsSub$: Subscription;
  getColumnsSub$: Subscription;

  constructor(
    private loaderService: LoaderService,
    public taskTrackerStateService: TaskTrackerStateService,
    public taskTrackerService: BackendConfigurationPnTaskTrackerService,
    private toasterService: ToastrService,
    private propertyService: BackendConfigurationPnPropertiesService,
    public dialog: MatDialog,
    private overlay: Overlay,
  ) {
  }

  ngOnInit() {
    this.updateTable();
    this.getColumns();
  }

  ngOnDestroy(): void {
  }

  openColumnsModal(): void {
    const dialogRef = this.dialog.open(TaskTrackerShownColumnsComponent, dialogConfigHelper(this.overlay, this.columns));
    this.columnsChangedSub$ = dialogRef.componentInstance.columnsChanged.subscribe((data: Columns) => {
      let updateModal = [];
      if (data) {
        for (let [key, value] of Object.entries(data)) {
          if (data.hasOwnProperty(key)) {
            updateModal = [...updateModal, {columnName: key, isColumnEnabled: value}];
          }
        }
      }
      this.updateColumnsSub$ = this.taskTrackerService.updateColumns(updateModal).subscribe(response => {
        if (response && response.success) {
          dialogRef.close();
          this.getColumns();
        }
      });
    });
  }

  getColumns() {
    this.getColumnsSub$ = this.taskTrackerService.getColumns().subscribe(data => {
      if (data && data.success && data.model && data.model.length) {
        this.columns = data.model.reduce((acc, {columnName, isColumnEnabled}) => {
          acc[columnName] = isColumnEnabled;
          return acc;
        }, {} as Columns);
      }
    });
  }

  updateTable() {
    this.getAllTasksSub$ = this.taskTrackerStateService
      .getAllTasks()
      .subscribe((data) => {
        if (data && data.success && data.model) {
          this.tasks = data.model;
        }
      });
  }

  openCreateModal() {
    const createModal = this.dialog.open(TaskTrackerCreateShowModalComponent, dialogConfigHelper(this.overlay));
    this.taskCreatedSub$ = createModal.componentInstance.taskCreated.subscribe(() => this.updateTable());
  }
}
