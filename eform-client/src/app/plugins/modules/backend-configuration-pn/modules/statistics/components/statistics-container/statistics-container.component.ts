import {Component, ElementRef, OnDestroy, OnInit} from '@angular/core';
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

  ngOnDestroy(): void {
  }

  changeProperty(property: CommonDictionaryModel | null) {
    if (property) {
      this.statisticsStateService.updatePropertyId(property.id);
    } else {
      this.statisticsStateService.updatePropertyId(null);
    }
    this.getAllStatistics();
  }
}
