import {Component, OnDestroy, OnInit, ViewChild} from '@angular/core';
import { AutoUnsubscribe } from 'ngx-auto-unsubscribe';
import { WorkOrderCaseModel } from '../../../../models';
import {TaskManagementStateService} from '../store';
import {TaskManagementCreateShowModalComponent, TaskManagementDeleteModalComponent} from '../';
import {
  BackendConfigurationPnPropertiesService,
  BackendConfigurationPnTaskManagementService
} from '../../../../services';
import {saveAs} from 'file-saver';
import {ToastrService} from 'ngx-toastr';
import {Subscription} from 'rxjs';
import {CommonDictionaryModel} from 'src/app/common/models';
import {MatIconRegistry} from '@angular/material/icon';
import {DomSanitizer} from '@angular/platform-browser';
import {ExcelIcon, WordIcon} from 'src/app/common/const';
import {MatDialog, MatDialogRef} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {LoaderService} from 'src/app/common/services';

@AutoUnsubscribe()
@Component({
  selector: 'app-task-management-container',
  templateUrl: './task-management-container.component.html',
  styleUrls: ['./task-management-container.component.scss'],
})
export class TaskManagementContainerComponent implements OnInit, OnDestroy {
  @ViewChild('showCreateModal', { static: true })
  showCreateModal: TaskManagementCreateShowModalComponent;

  workOrderCases: WorkOrderCaseModel[] = [];
  properties: CommonDictionaryModel[] = [];

  downloadWordReportSub$: Subscription;
  downloadExcelReportSub$: Subscription;
  getAllWorkOrderCasesSub$: Subscription;
  getWorkOrderCaseSub$: Subscription;
  deleteWorkOrderCaseSub$: Subscription;
  workOrderCaseDeleteSub$: Subscription;
  taskCreatedSub$: Subscription;
  taskUpdatedSub$: Subscription;

  constructor(
    private loaderService: LoaderService,
    public taskManagementStateService: TaskManagementStateService,
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
    this.loaderService.setLoading(false);
    this.getProperties();
  }

  ngOnDestroy(): void {}

  updateTable(delayed: boolean = false) {
    this.getAllWorkOrderCasesSub$ = this.taskManagementStateService
      .getAllWorkOrderCases(delayed)
      .subscribe((data) => {
        if (data && data.success && data.model) {
          this.workOrderCases = data.model;
        }
      });
  }

  openViewModal(workOrderCaseId: number) {
    this.getWorkOrderCaseSub$ = this.taskManagementService
      .getWorkOrderCase(workOrderCaseId)
      .subscribe((data) => {
        if (data && data.success && data.model) {
          const updateModal = this.dialog.open(TaskManagementCreateShowModalComponent, {...dialogConfigHelper(this.overlay, data.model)});
          this.taskUpdatedSub$ = updateModal.componentInstance.taskCreated.subscribe(() => this.updateTable(true));
        }
      });
  }

  openCreateModal() {
    const createModal = this.dialog.open(TaskManagementCreateShowModalComponent, dialogConfigHelper(this.overlay));
    this.taskCreatedSub$ = createModal.componentInstance.taskCreated.subscribe(() => this.updateTable(false));
  }

  openDeleteModal(workOrderCaseModel: WorkOrderCaseModel) {
    const deleteModal = this.dialog.open(TaskManagementDeleteModalComponent, dialogConfigHelper(this.overlay, workOrderCaseModel));
    this.workOrderCaseDeleteSub$ = deleteModal.componentInstance.workOrderCaseDelete
      .subscribe(x => this.deleteWorkOrderCaseModel(x, deleteModal));
  }

  deleteWorkOrderCaseModel(workOrderCaseId: number, deleteModal: MatDialogRef<TaskManagementDeleteModalComponent>) {
    this.deleteWorkOrderCaseSub$ = this.taskManagementService
      .deleteWorkOrderCase(workOrderCaseId)
      .subscribe((data) => {
        if (data && data.success) {
          deleteModal.close();
          this.updateTable();
        }
      });
  }

  getProperties() {
    this.propertyService.getAllPropertiesDictionary()
      .subscribe((data) => {
        if (data && data.success && data.model) {
          this.properties = data.model;
        }
    });
  }

  onDownloadWordReport() {
    const filters = this.taskManagementStateService.store.getValue().filters;
    this.downloadWordReportSub$ = this.taskManagementStateService
      .downloadWordReport()
      .subscribe(
        (data) => {
          saveAs(data, `${this.properties.find(x => x.id === filters.propertyId).name}${ filters.areaName ? '_' + filters.areaName : ''}_report.docx`);
        },
        (_) => {
          this.toasterService.error('Error downloading report');
        }
      );
  }

  onDownloadExcelReport() {
    const filters = this.taskManagementStateService.store.getValue().filters;
    this.downloadExcelReportSub$ = this.taskManagementStateService
      .downloadExcelReport()
      .subscribe(
        (data) => {
          saveAs(data, `${this.properties.find(x => x.id === filters.propertyId).name}${ filters.areaName ? '_' + filters.areaName : ''}_report.xlsx`);
        },
        (_) => {
          this.toasterService.error('Error downloading report');
        }
      );
  }
}
