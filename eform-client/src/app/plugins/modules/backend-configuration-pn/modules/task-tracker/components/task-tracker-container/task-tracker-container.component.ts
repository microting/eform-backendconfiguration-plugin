import {Component, OnDestroy, OnInit, SimpleChanges, ViewChild, OnChanges} from '@angular/core';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {TaskModel} from '../../../../models';
import {TaskTrackerStateService} from '../store';
import {TaskTrackerCreateShowModalComponent, TaskTrackerShownColumnsComponent} from '../';
import {
  BackendConfigurationPnPropertiesService,
  BackendConfigurationPnTaskManagementService, BackendConfigurationPnTaskTrackerService
} from '../../../../services';
import {ToastrService} from 'ngx-toastr';
import {Subscription} from 'rxjs';
import {MatIconRegistry} from '@angular/material/icon';
import {DomSanitizer} from '@angular/platform-browser';
import {ExcelIcon, WordIcon} from 'src/app/common/const';
import {MatDialog} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {LoaderService} from 'src/app/common/services';
import {
  IColumns,
  IPostColumns
} from 'src/app/plugins/modules/backend-configuration-pn/models/task-tracker/columns.model';

@AutoUnsubscribe()
@Component({
  selector: 'app-task-tracker-container',
  templateUrl: './task-tracker-container.component.html',
  styleUrls: ['./task-tracker-container.component.scss'],
})
export class TaskTrackerContainerComponent implements OnInit, OnDestroy {
  @ViewChild('showCreateModal', {static: true})
  showCreateModal: TaskTrackerCreateShowModalComponent;
  tasks: TaskModel[] = [];

  getAllTasksSub$: Subscription;
  deleteWorkOrderCaseSub$: Subscription;
  taskCreatedSub$: Subscription;

  columns: IColumns;

  columnsPostRequestSuccess: boolean;

  constructor(
    private loaderService: LoaderService,
    public taskTrackerStateService: TaskTrackerStateService,
    public taskManagementService: BackendConfigurationPnTaskManagementService,
    public taskTrackerService: BackendConfigurationPnTaskTrackerService,
    private toasterService: ToastrService,
    private propertyService: BackendConfigurationPnPropertiesService,
    iconRegistry: MatIconRegistry,
    sanitizer: DomSanitizer,
    public dialog: MatDialog,
    private overlay: Overlay,
  ) {
    iconRegistry.addSvgIconLiteral('file-word', sanitizer.bypassSecurityTrustHtml(WordIcon));
    iconRegistry.addSvgIconLiteral('file-excel', sanitizer.bypassSecurityTrustHtml(ExcelIcon));
  }

  ngOnInit() {
    this.updateTable();
    this.updateColumns();
  }

  ngOnDestroy(): void {
  }

  openColumnsModal(): void {
    const dialogRef = this.dialog.open(TaskTrackerShownColumnsComponent, {data: this.columns});
    dialogRef.componentInstance.dataChanged.subscribe((data: IColumns) => {
      this.columns = data;
      const updateModal = [];
      if (data) {
        for (let [key, value] of Object.entries(data)) {
          if (data.hasOwnProperty(key)) {
            updateModal.push({columnName: key, isColumnEnabled: value})
          }
        }
      }
      const result = this.taskTrackerService.updateColumns(updateModal as unknown as IPostColumns).subscribe(response => {
        if(response.success) {
          dialogRef.componentInstance.hide();
        }
      })
    })
  }
  updateColumns() {
    const result = this.taskTrackerService.getColumns().subscribe(data => {
      if (data && data.success && data.model) {
        if (data.model.length !== 7) {
          this.columns = {
            Deadline: true,
            Property : true,
            Repeat : true,
            Start : true,
            Tags : true,
            Task : true,
            Workers : true
        }
        } else {
          this.columns = data.model.reduce((acc, {columnName, isColumnEnabled}) => {
            acc[columnName] = isColumnEnabled;
            return acc;
          }, {} as IColumns);
        }
      }
    })
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

  openDeleteModal(workOrderCaseModel: TaskModel) {
    /*const deleteModal = this.dialog.open(TaskManagementDeleteModalComponent, dialogConfigHelper(this.overlay, workOrderCaseModel));
    this.workOrderCaseDeleteSub$ = deleteModal.componentInstance.workOrderCaseDelete
      .subscribe(x => this.deleteWorkOrderCaseModel(x, deleteModal));*/
  }

  deleteWorkOrderCaseModel(workOrderCaseId: number, deleteModal) {
    this.deleteWorkOrderCaseSub$ = this.taskManagementService
      .deleteWorkOrderCase(workOrderCaseId)
      .subscribe((data) => {
        if (data && data.success) {
          deleteModal.close();
          this.updateTable();
        }
      });
  }
}
