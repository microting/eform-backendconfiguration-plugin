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
import {TranslateService} from '@ngx-translate/core';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {tap} from 'rxjs/operators';
import {CommonDictionaryModel} from 'src/app/common/models';
import {BackendConfigurationPnPropertiesService} from '../../../../services';
import {AuthStateService} from 'src/app/common/store';
import {Router} from '@angular/router';
import {TaskTrackerStateService} from '../../../task-tracker/components/store';
import {TaskManagementStateService} from '../../../task-management/components/store';
import {TaskWizardStateService} from '../../../task-wizard/components/store';
import {DocumentsStateService} from '../../../documents/store';
import {DocumentsExpirationFilterEnum, TaskWizardStatusesEnum} from '../../../../enums';

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
  sideMenuOpenedAsyncSub$: Subscription;

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

  constructor(
    public statisticsStateService: StatisticsStateService,
    private translateService: TranslateService,
    private backendConfigurationPnPropertiesService: BackendConfigurationPnPropertiesService,
    private authStateService: AuthStateService,
    private router: Router,
    private taskTrackerStateService: TaskTrackerStateService,
    private taskManagementStateService: TaskManagementStateService,
    private documentsStateService: DocumentsStateService,
    private taskWizardStateService: TaskWizardStateService,
  ) {
    this.getPropertyIdAsyncSub$ = statisticsStateService.getPropertyIdAsync()
      .subscribe(propertyId => this.selectedPropertyId = propertyId);
    this.sideMenuOpenedAsyncSub$ = authStateService.sideMenuOpenedAsync
      .subscribe(sideMenuOpened => this.sideMenuOpened = sideMenuOpened);
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

  clickOnAdHocTaskPriorities() {
    this.taskManagementStateService.store.update((state) => ({
      filters: {
        ...state.filters,
        propertyId: this.selectedPropertyId || null,
        areaName: null,
        dateFrom: null,
        dateTo: null,
        status: null,
        createdBy: null,
        lastAssignedTo: null,
      },
    }));
    this.router.navigate(['/plugins/backend-configuration-pn/task-management'], {queryParams: {diagramForShow: 'ad-hoc-task-priorities'}}).then();
  }

  clickOnDocumentUpdatedDays(filter?: DocumentsExpirationFilterEnum) {
    this.documentsStateService.store.update((state) => ({
      filters: {
        ...state.filters,
        propertyId: this.selectedPropertyId || null,
        expiration: filter,
        documentId: null,
        folderId: null,
      },
    }));
    this.router.navigate(['/plugins/backend-configuration-pn/documents'], {queryParams: {showDiagram: true}}).then();
  }

  clickOnPlannedTaskWorkers(workerId: number | null) {
    this.taskWizardStateService.store.update((state) => ({
      filters: {
        ...state.filters,
        propertyIds: this.selectedPropertyId ? [this.selectedPropertyId] : [],
        assignToIds: this.selectedPropertyId && workerId ? [workerId] : [],
        tagIds: [],
        status: this.selectedPropertyId && workerId ? TaskWizardStatusesEnum.Active : null,
        folderIds: [],
      },
    }));
    this.router.navigate(['/plugins/backend-configuration-pn/task-wizard'], {queryParams: {showDiagram: true}}).then();
  }

  clickOnAdHocTaskWorkers(workerId: number | null) {
    this.taskManagementStateService.store.update((state) => ({
      filters: {
        ...state.filters,
        propertyId: this.selectedPropertyId || null,
        areaName: null,
        dateFrom: null,
        dateTo: null,
        status: null,
        createdBy: null,
        lastAssignedTo: this.selectedPropertyId && workerId ? workerId : null,
      },
    }));
    this.router.navigate(['/plugins/backend-configuration-pn/task-management'], {queryParams: {diagramForShow: 'ad-hoc-task-workers'}}).then();
  }

  ngOnDestroy(): void {
  }
}
