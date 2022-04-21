import {Component, OnDestroy, OnInit, ViewChild} from '@angular/core';
import { AutoUnsubscribe } from 'ngx-auto-unsubscribe';
import { WorkOrderCaseModel } from '../../../../models';
import {TaskManagementStateService} from '../store';
import {TaskManagementCreateShowModalComponent, TaskManagementDeleteModalComponent} from '../';
import {
  BackendConfigurationPnPropertiesService,
  BackendConfigurationPnTaskManagementService
} from 'src/app/plugins/modules/backend-configuration-pn/services';
import {saveAs} from 'file-saver';
import {ToastrService} from 'ngx-toastr';
import { Subscription } from 'rxjs';
import {CommonDictionaryModel} from 'src/app/common/models';

@AutoUnsubscribe()
@Component({
  selector: 'app-task-management-container',
  templateUrl: './task-management-container.component.html',
  styleUrls: ['./task-management-container.component.scss'],
})
export class TaskManagementContainerComponent implements OnInit, OnDestroy {
  @ViewChild('showCreateModal', { static: true })
  showCreateModal: TaskManagementCreateShowModalComponent;
  @ViewChild('deleteModal', { static: true })
  deleteModal: TaskManagementDeleteModalComponent;

  workOrderCases: WorkOrderCaseModel[] = [];
  properties: CommonDictionaryModel[] = [];

  downloadWordReportSub$: Subscription;
  downloadExcelReportSub$: Subscription;
  getAllWorkOrderCasesSub$: Subscription;
  getWorkOrderCaseSub$: Subscription;
  deleteWorkOrderCaseSub$: Subscription;

  constructor(
    public taskManagementStateService: TaskManagementStateService,
    public taskManagementService: BackendConfigurationPnTaskManagementService,
    private toasterService: ToastrService,
    private propertyService: BackendConfigurationPnPropertiesService
  ) {}

  ngOnInit() {
    this.getProperties();
  }

  ngOnDestroy(): void {}

  updateTable() {
    this.getAllWorkOrderCasesSub$ = this.taskManagementStateService
      .getAllWorkOrderCases()
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
          this.showCreateModal.show(data.model);
        }
      });
  }

  openCreateModal() {
    this.showCreateModal.show();
  }

  openDeleteModal(workOrderCaseModel: WorkOrderCaseModel) {
    this.deleteModal.show(workOrderCaseModel);
  }

  deleteWorkOrderCaseModel(workOrderCaseId: number) {
    this.deleteWorkOrderCaseSub$ = this.taskManagementService
      .deleteWorkOrderCase(workOrderCaseId)
      .subscribe((data) => {
        if (data && data.success) {
          this.deleteModal.hide();
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
          saveAs(data, `${this.properties.find(x => x.id === filters.propertyId).name}_${filters.areaName}_report.docx`);
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
          saveAs(data, `${this.properties.find(x => x.id === filters.propertyId).name}_${filters.areaName}_report.xlsx`);
        },
        (_) => {
          this.toasterService.error('Error downloading report');
        }
      );
  }
}
