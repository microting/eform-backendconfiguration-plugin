import {Component, OnDestroy, OnInit, ViewChild} from '@angular/core';
import { AutoUnsubscribe } from 'ngx-auto-unsubscribe';
import { TaskModel } from '../../../../models';
import {TaskTrackerStateService} from '../store';
import {TaskTrackerCreateShowModalComponent} from '../';
import {
  BackendConfigurationPnPropertiesService,
  BackendConfigurationPnTaskManagementService
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

@AutoUnsubscribe()
@Component({
  selector: 'app-task-tracker-container',
  templateUrl: './task-tracker-container.component.html',
  styleUrls: ['./task-tracker-container.component.scss'],
})
export class TaskTrackerContainerComponent implements OnInit, OnDestroy {
  @ViewChild('showCreateModal', { static: true })
  showCreateModal: TaskTrackerCreateShowModalComponent;
  tasks: TaskModel[] = [];

  getAllTasksSub$: Subscription;
  deleteWorkOrderCaseSub$: Subscription;
  taskCreatedSub$: Subscription;

  constructor(
    private loaderService: LoaderService,
    public taskTrackerStateService: TaskTrackerStateService,
    public taskManagementService: BackendConfigurationPnTaskManagementService,
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
  }

  ngOnDestroy(): void {}

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
