import {Component, OnDestroy, OnInit} from '@angular/core';
import {
  AdHocTaskPrioritiesModel,
  AdHocTaskWorkers,
  DocumentUpdatedDaysModel,
  PlannedTaskDaysModel,
  PlannedTaskWorkers
} from '../../../../models';
import {Subscription} from 'rxjs';
import {StatisticsStateService} from '../../store';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {tap} from 'rxjs/operators';
import {CommonDictionaryModel} from 'src/app/common/models';
import {BackendConfigurationPnPropertiesService} from '../../../../services';
import {Router} from '@angular/router';
import {TaskTrackerStateService} from '../../../task-tracker/components/store';
import {DocumentsExpirationFilterEnum, TaskWizardStatusesEnum} from '../../../../enums';
import {selectSideMenuOpened} from 'src/app/state';
import {Store} from '@ngrx/store';
import {
  selectStatisticsPropertyId,
  taskManagementUpdateFilters, taskWizardUpdateFilters,
  updateDocumentsFilters
} from '../../../../state';

@AutoUnsubscribe()
@Component({
  selector: 'app-statistics-container',
  templateUrl: './statistics-container.component.html',
  styleUrls: ['./statistics-container.component.scss'],
})
export class StatisticsContainerComponent implements OnInit, OnDestroy {
  plannedTaskDays: PlannedTaskDaysModel;
  adHocTaskPrioritiesModel: AdHocTaskPrioritiesModel;
  adHocTaskWorkers: AdHocTaskWorkers;
  documentUpdatedDaysModel: DocumentUpdatedDaysModel;
  plannedTaskWorkers: PlannedTaskWorkers;
  properties: CommonDictionaryModel[] = [];
  selectedPropertyId: number | null = null;
  sideMenuOpened: boolean = true;
  viewTopLineFull: number[] = [500, 300];
  viewTopLineShort: number[] = [470, 300];
  viewBottomLineFull: number[] = [775, 300];
  viewBottomLineShort: number[] = [725, 300];

  getAdHocTaskPrioritiesSub$: Subscription;
  getDocumentUpdatedDaysSub$: Subscription;
  getPlannedTaskWorkersSub$: Subscription;
  getPlannedTaskDaysSub$: Subscription;
  getAdHocTaskWorkersSub$: Subscription;
  getPropertiesSub$: Subscription;
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

  get viewTopLine(): number[] {
    if (this.sideMenuOpened) {
      return this.viewTopLineShort;
    } else {
      return this.viewTopLineFull;
    }
  }

  get viewBottomLine(): number[] {
    if (this.sideMenuOpened) {
      return this.viewBottomLineShort;
    } else {
      return this.viewBottomLineFull;
    }
  }
  public selectSideMenuOpened$ = this.store.select(selectSideMenuOpened);
  public selectStatisticsPropertyId$ = this.store.select(selectStatisticsPropertyId);

  constructor(
    private store: Store,
    public statisticsStateService: StatisticsStateService,
    private backendConfigurationPnPropertiesService: BackendConfigurationPnPropertiesService,
    private router: Router,
    private taskTrackerStateService: TaskTrackerStateService,
  ) {
    this.getPropertyIdAsyncSub$ = this.selectStatisticsPropertyId$
      .subscribe(propertyId => this.selectedPropertyId = propertyId);
    this.selectSideMenuOpened$.subscribe((sideMenuOpened) => {
      this.sideMenuOpened = sideMenuOpened;
    });
  }

  ngOnInit(): void {
    this.getAllStatistics();
    this.getProperties();
  }

  getAllStatistics() {
    this.getPlannedTaskDays();
    this.getAdHocTaskPriorities();
    this.getDocumentUpdatedDays();
    this.getPlannedTaskWorkers();
    this.getAdHocTaskWorkers();
  }

  getPlannedTaskDays() {
    this.getPlannedTaskDaysSub$ = this.statisticsStateService.getPlannedTaskDays()
      .pipe(tap(model => {
        if (model && model.success && model.model) {
          this.plannedTaskDays = model.model;
        }
      }))
      .subscribe();
  }

  getAdHocTaskPriorities() {
    this.getAdHocTaskPrioritiesSub$ = this.statisticsStateService.getAdHocTaskPriorities()
      .pipe(tap(model => {
        if (model && model.success && model.model) {
          this.adHocTaskPrioritiesModel = model.model;
        }
      }))
      .subscribe();
  }

  getDocumentUpdatedDays() {
    this.getDocumentUpdatedDaysSub$ = this.statisticsStateService.getDocumentUpdatedDays()
      .pipe(tap(model => {
        if (model && model.success && model.model) {
          this.documentUpdatedDaysModel = model.model;
        }
      }))
      .subscribe();
  }

  getPlannedTaskWorkers() {
    this.getPlannedTaskWorkersSub$ = this.statisticsStateService.getPlannedTaskWorkers()
      .pipe(tap(model => {
        if (model && model.success && model.model) {
          this.plannedTaskWorkers = model.model;
        }
      }))
      .subscribe();
  }

  getAdHocTaskWorkers() {
    this.getAdHocTaskWorkersSub$ = this.statisticsStateService.getAdHocTaskWorkers()
      .pipe(tap(model => {
        if (model && model.success && model.model) {
          this.adHocTaskWorkers = model.model;
        }
      }))
      .subscribe();
  }

  getProperties() {
    this.getPropertiesSub$ = this.backendConfigurationPnPropertiesService.getAllPropertiesDictionary()
      .pipe(
        tap(model => {
          if (model && model.success && model.model) {
            this.properties = model.model;
          }
        })
      )
      .subscribe();
  }

  changeProperty(property: CommonDictionaryModel | null) {
    if (property) {
      this.statisticsStateService.updatePropertyId(property.id);
    } else {
      this.statisticsStateService.updatePropertyId(null);
    }
    this.getAllStatistics();
  }

  clickOnPlannedTaskDays() {
    if (this.selectedPropertyId) {
      this.taskTrackerStateService.updateFilters({propertyIds: [this.selectedPropertyId], tagIds: [], workerIds: []});
    } else {
      this.taskTrackerStateService.updateFilters({propertyIds: [], tagIds: [], workerIds: []});
    }
    this.router.navigate(['/plugins/backend-configuration-pn/task-tracker'], {queryParams: {showDiagram: true}}).then();
  }

  clickOnAdHocTaskPriorities(event: any) {
    this.store.dispatch(taskManagementUpdateFilters({
      propertyId: this.selectedPropertyId || -1,
      areaName: null,
      dateFrom: null,
      dateTo: null,
      status: null,
      createdBy: null,
      lastAssignedTo: null,
      priority: event, // Setting this to null, since the value is not working as expected
      delayed: false,
    }));
    this.router.navigate(['/plugins/backend-configuration-pn/task-management'], {queryParams: {diagramForShow: 'ad-hoc-task-priorities'}}).then();
  }

  clickOnDocumentUpdatedDays(filter?: DocumentsExpirationFilterEnum) {
    this.store.dispatch(updateDocumentsFilters({
      propertyId: this.selectedPropertyId || null,
      expiration: filter,
      documentId: null,
      folderId: null,
    }));
    this.router.navigate(['/plugins/backend-configuration-pn/documents'], {queryParams: {showDiagram: true}}).then();
  }

  clickOnPlannedTaskWorkers(workerId: number | null) {
    this.store.dispatch(taskWizardUpdateFilters({
      propertyIds: this.selectedPropertyId ? [this.selectedPropertyId] : [],
      assignToIds: this.selectedPropertyId && workerId ? [workerId] : [],
      tagIds: [],
      status: this.selectedPropertyId && workerId ? TaskWizardStatusesEnum.Active : null,
      folderIds: [],
    }));
    this.router.navigate(['/plugins/backend-configuration-pn/task-wizard'], {queryParams: {showDiagram: true}}).then();
  }

  clickOnAdHocTaskWorkers(workerId: number | null) {
    this.store.dispatch(taskManagementUpdateFilters({
      propertyId: this.selectedPropertyId || -1,
      areaName: null,
      dateFrom: null,
      dateTo: null,
      status: 1,
      createdBy: null,
      lastAssignedTo: this.selectedPropertyId && workerId ? workerId : null,
      priority: null,
      delayed: false,
    }));
    this.router.navigate(['/plugins/backend-configuration-pn/task-management'], {queryParams: {diagramForShow: 'ad-hoc-task-workers'}}).then();
  }

  ngOnDestroy(): void {
  }
}
