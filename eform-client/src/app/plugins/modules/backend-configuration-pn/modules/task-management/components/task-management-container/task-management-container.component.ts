import {
  Component, OnDestroy, OnInit,
  inject
} from '@angular/core';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {AdHocTaskPrioritiesModel, AdHocTaskWorkers, WorkOrderCaseModel} from '../../../../models';
import {TaskManagementStateService} from '../store';
import {TaskManagementCreateShowModalComponent} from '../';
import {BackendConfigurationPnPropertiesService} from '../../../../services';
import {saveAs} from 'file-saver';
import {ToastrService} from 'ngx-toastr';
import {Subscription, take} from 'rxjs';
import {CommonDictionaryModel} from 'src/app/common/models';
import {MatDialog} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {LoaderService} from 'src/app/common/services';
import {catchError, skip, tap} from 'rxjs/operators';
import {StatisticsStateService} from '../../../statistics/store';
import {ActivatedRoute} from '@angular/router';
import {Store} from '@ngrx/store';
import {
  selectTaskManagementFilters,
  selectTaskManagementPropertyId,
  selectTaskManagementPropertyIdIsNullOrUndefined
} from '../../../../state/task-management/task-management.selector';

@AutoUnsubscribe()
@Component({
  selector: 'app-task-management-container',
  templateUrl: './task-management-container.component.html',
  styleUrls: ['./task-management-container.component.scss'],
  standalone: false
})
export class TaskManagementContainerComponent implements OnInit, OnDestroy {
  private store = inject(Store);
  private loaderService = inject(LoaderService);
  public taskManagementStateService = inject(TaskManagementStateService);
  private toasterService = inject(ToastrService);
  private propertyService = inject(BackendConfigurationPnPropertiesService);
  public dialog = inject(MatDialog);
  private overlay = inject(Overlay);
  private statisticsStateService = inject(StatisticsStateService);
  private route = inject(ActivatedRoute);

  workOrderCases: WorkOrderCaseModel[] = [];
  properties: CommonDictionaryModel[] = [];
  adHocTaskPrioritiesModel: AdHocTaskPrioritiesModel;
  adHocTaskWorkers: AdHocTaskWorkers;
  selectedPropertyId: number | null = null;
  selectedPriority: number | null = null;
  selectedStatuses: number[] = [];
  selectedWorkerId: number | null = null;
  view = [1000, 300];
  diagramForShow: string = '';
  highlightedId: number | null = null;

  downloadWordReportSub$: Subscription;
  downloadExcelReportSub$: Subscription;
  getAllWorkOrderCasesSub$: Subscription;
  taskCreatedSub$: Subscription;
  getPropertyIdAsyncSub$: Subscription;

  get propertyName(): string {
    if (this.properties && this.selectedPropertyId) {
      const index = this.properties.findIndex(x => x.id === this.selectedPropertyId);
      if (index !== -1) {
        return this.properties[index].name;
      }
    }
    return '';
  }

  public selectTaskManagementPropertyIdIsNullOrUndefined$ = this.store.select(selectTaskManagementPropertyIdIsNullOrUndefined);
  public selectTaskManagementPropertyId$ = this.store.select(selectTaskManagementPropertyId);
  private selectTaskManagementFilters$ = this.store.select(selectTaskManagementFilters);

  ngOnInit() {
    this.route.queryParams.subscribe(x => {
      if (x && x.diagramForShow) {
        this.diagramForShow = x.diagramForShow;
        this.selectTaskManagementFilters$.subscribe((filters) => {
          if (filters.propertyId) {
            this.selectedPropertyId = filters.propertyId;
          }
          this.selectedPriority = filters.priority;
          this.selectedStatuses = filters.statuses;
          this.selectedWorkerId = filters.lastAssignedTo;
        });
        this.updateTable();
        this.getStats();
      } else {
        this.diagramForShow = '';
      }
    });
    this.getPropertyIdAsyncSub$ = this.selectTaskManagementFilters$
      .pipe(skip(1))
      .subscribe(() => {
        if (this.diagramForShow) {
          this.getStats();
        }
        this.updateTable();
      });

    this.loaderService.setLoading(false);
    this.getProperties();
  }

  ngOnDestroy(): void {
  }

  updateTable() {
    this.getAllWorkOrderCasesSub$ = this.taskManagementStateService
      .getAllWorkOrderCases(false)
      .subscribe((data) => {
        if (data && data.success && data.model) {
          this.workOrderCases = data.model;
        }
      });
  }

  updateTableWithHighlight(rowIndex: number) {
    setTimeout(() => {
      this.getAllWorkOrderCasesSub$ = this.taskManagementStateService
        .getAllWorkOrderCases(false)
        .subscribe((data) => {
          if (data && data.success && data.model) {
            this.workOrderCases = data.model;
            this.highlightedId = rowIndex;
          }
        });
    }, 3000);
  }

  openCreateModal() {
    this.taskCreatedSub$ = this.dialog.open(TaskManagementCreateShowModalComponent, {
      ...dialogConfigHelper(this.overlay),
      panelClass: 'task-management-modal'
    }).afterClosed().subscribe(data => {
      if (data) {
        this.updateTable();
      }
    });
  }

  onHighlightedRowRendered(): void {
    setTimeout(() => {
      this.highlightedId = null;
    }, 3500);
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
    let currentFilters: any;
    this.selectTaskManagementFilters$.subscribe((filters) => {
      currentFilters = filters;
    }).unsubscribe();
    this.downloadWordReportSub$ = this.taskManagementStateService
      .downloadWordReport()
      .pipe(
        tap((data) => {
          if (currentFilters.propertyId === -1 || currentFilters.propertyId === null) {
            saveAs(data, `report.docx`);
          } else {
            saveAs(data, `${this.properties.find(x =>
              x.id === currentFilters.propertyId).name}${currentFilters.areaName ? '_' + currentFilters.areaName : ''}_report.docx`);
          }
        }),
        catchError((_, caught) => {
          this.toasterService.error('Error downloading report');
          return caught;
        })
      )
      .subscribe();
  }

  onDownloadExcelReport() {
    let currentFilters: any;
    this.selectTaskManagementFilters$.subscribe((filters) => {
      currentFilters = filters;
    }).unsubscribe();
    this.downloadExcelReportSub$ = this.taskManagementStateService
      .downloadExcelReport()
      .pipe(
        tap((data) => {
          if (currentFilters.propertyId === -1 || currentFilters.propertyId === null) {
            saveAs(data, `report.xlsx`);
          } else {
            saveAs(data, `${this.properties.find(x =>
              x.id === currentFilters.propertyId).name}${currentFilters.areaName ? '_' + currentFilters.areaName : ''}_report.xlsx`);
          }
        }),
        catchError((_, caught) => {
          this.toasterService.error('Error downloading report');
          return caught;
        }),
      )
      .subscribe();
  }

  getStats() {
    let filters: any;
    this.selectTaskManagementFilters$
      .pipe(take(1))
      .subscribe(f => filters = f);

    if (this.diagramForShow === 'ad-hoc-task-priorities') {
      this.statisticsStateService.getAdHocTaskPrioritiesByFilters({
        propertyId: filters.propertyId,
        statuses: filters.statuses,
        priority: filters.priority,
        lastAssignedTo: filters.lastAssignedTo,
        dateFrom: filters.dateFrom,
        dateTo: filters.dateTo,
      })
        .subscribe(res => {
          if (res?.success) {
            this.adHocTaskPrioritiesModel = res.model;
          }
        });
    } else if (this.diagramForShow === 'ad-hoc-task-workers') {
      if (!filters) {
        return;
      }
      this.statisticsStateService
        .getAdHocTaskWorkersByFilters({
          propertyId: filters.propertyId ? [filters.propertyId] : [],
          areaName: filters.areaName ? [filters.areaName] : [],
          createdBy: [],
          lastAssignedTo: filters.lastAssignedTo ? [filters.lastAssignedTo] : [],
          statuses: filters.statuses ?? [],
          priority: [],
          dateFrom: filters.dateFrom ? [filters.dateFrom] : [],
          dateTo: filters.dateTo ? [filters.dateTo] : [],
        })
        .subscribe(res => {
          if (res?.success) {
            this.adHocTaskWorkers = res.model;
          }
        });
    }
  }
}
