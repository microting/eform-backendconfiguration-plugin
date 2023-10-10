import {Component, OnDestroy, OnInit} from '@angular/core';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {Subscription} from 'rxjs';
import {
  CommonDictionaryModel,
} from 'src/app/common/models';
import {AuthStateService} from 'src/app/common/store';
import {PropertyAssignWorkersModel, DeviceUserModel, TaskWizardModel,} from '../../../../models';
import {BackendConfigurationPnPropertiesService} from '../../../../services';
import {
  PropertyWorkerDeleteModalComponent,
  PropertyWorkerOtpModalComponent,
  PropertyWorkerCreateEditModalComponent
} from '../';
import {PropertyWorkersStateService} from '../store';
import {Sort} from '@angular/material/sort';
import {MtxGridColumn} from '@ng-matero/extensions/grid';
import {TranslateService} from '@ngx-translate/core';
import {ActivatedRoute, Router} from '@angular/router';
import {MatDialog} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {tap} from 'rxjs/operators';
import * as R from 'ramda';

@AutoUnsubscribe()
@Component({
  selector: 'app-property-workers-page',
  templateUrl: './property-workers-page.component.html',
})
export class PropertyWorkersPageComponent implements OnInit, OnDestroy {
  sitesDto: Array<DeviceUserModel>;
  availableProperties: CommonDictionaryModel[];
  workersAssignments: PropertyAssignWorkersModel[];

  getSites$: Subscription;
  getPropertiesDictionary$: Subscription;
  deviceUserAssignments$: Subscription;
  propertyWorkerEditModalComponentAfterClosedSub$: Subscription;
  propertyWorkerCreateModalComponentAfterClosedSub$: Subscription;
  getFiltersAsyncSub$: Subscription;

  get userClaims() {
    return this.authStateService.currentUserClaims;
  }

  constructor(
    private authStateService: AuthStateService,
    private propertiesService: BackendConfigurationPnPropertiesService,
    public propertyWorkersStateService: PropertyWorkersStateService,
    private translateService: TranslateService,
    private router: Router,
    private route: ActivatedRoute,
    private dialog: MatDialog,
    private overlay: Overlay,
  ) {
  }

  ngOnInit() {
    let propertyIds: number[] = [];
    this.getDeviceUsersFiltered();
    this.getPropertiesDictionary();
    this.getFiltersAsyncSub$ = this.propertyWorkersStateService.getFiltersAsync()
      .pipe(
        tap(filters => {
          if (filters.propertyIds.length !== 0 && !R.equals(propertyIds, filters.propertyIds)) {
            propertyIds = filters.propertyIds;
            this.updateTable();
          } else {
            propertyIds = [];
            this.updateTable();
          }
        },),
        tap(_ => {
          // if (this.showDiagram) {
          //   this.selectedPropertyId = this.taskWizardStateService.store.getValue().filters.propertyIds[0] || null;
          //   this.getPlannedTaskWorkers();
          // }
        })
      )
      .subscribe();
    this.getWorkerPropertiesAssignments();
  }

  openEditModal(simpleSiteDto: DeviceUserModel) {
    const selectedSimpleSite = new DeviceUserModel();
    selectedSimpleSite.userFirstName = simpleSiteDto.userFirstName;
    selectedSimpleSite.userLastName = simpleSiteDto.userLastName;
    selectedSimpleSite.id = simpleSiteDto.siteUid;
    selectedSimpleSite.languageCode = simpleSiteDto.languageCode;
    selectedSimpleSite.normalId = simpleSiteDto.siteId;
    selectedSimpleSite.isLocked = simpleSiteDto.isLocked;
    selectedSimpleSite.timeRegistrationEnabled = simpleSiteDto.timeRegistrationEnabled;
    selectedSimpleSite.taskManagementEnabled = simpleSiteDto.taskManagementEnabled;
    selectedSimpleSite.hasWorkOrdersAssigned = simpleSiteDto.hasWorkOrdersAssigned;
    selectedSimpleSite.isBackendUser = simpleSiteDto.isBackendUser;

    const workersAssignments = this.workersAssignments.find(
      (x) => x.siteId === simpleSiteDto.siteId
    );

    this.propertyWorkerEditModalComponentAfterClosedSub$ = this.dialog.open(PropertyWorkerCreateEditModalComponent,
      {
        ...dialogConfigHelper(this.overlay, {
          deviceUser: selectedSimpleSite,
          assignments: workersAssignments ? workersAssignments.assignments : [],
          availableProperties: this.availableProperties,
        }), minWidth: 500
      })
      .afterClosed().subscribe(data => data ? this.getDeviceUsersFiltered() : undefined);
  }

  openCreateModal() {
    this.propertyWorkerCreateModalComponentAfterClosedSub$ = this.dialog.open(PropertyWorkerCreateEditModalComponent,
      {
        ...dialogConfigHelper(this.overlay, {
          deviceUser: {},
          assignments: [],
          availableProperties: this.availableProperties,
        }), minWidth: 500
      })
      .afterClosed().subscribe(data => data ? this.getDeviceUsersFiltered() : undefined);
  }

  getPropertiesDictionary() {
    this.getPropertiesDictionary$ = this.propertiesService
      .getAllPropertiesDictionary()
      .subscribe((operation) => {
        if (operation && operation.success) {
          this.availableProperties = operation.model;
        }
      });
  }

  getWorkerPropertiesAssignments() {
    this.deviceUserAssignments$ = this.propertiesService
      .getPropertiesAssignments()
      .subscribe((operation) => {
        if (operation && operation.success) {
          this.workersAssignments = [...operation.model];
        }
      });
  }

  getWorkerPropertyNames(siteId: number) {
    let resultString = '';
    if (this.workersAssignments) {
      const obj = this.workersAssignments.find((x) => x.siteId === siteId);
      if (obj) {
        obj.assignments
          .filter((x) => x.isChecked)
          .forEach((assignment) => {
            if (resultString.length !== 0) {
              resultString += '<br>';
            }
            resultString += this.availableProperties.find(
              (prop) => prop.id === assignment.propertyId
            ).name;
          });
      }
    }

    return resultString ?
      // @ts-ignore
      `<span title="${resultString.replaceAll('<br>', '\n')}">${resultString}</span>` :
      '--';
  }

  onSearchChanged(name: string) {
    this.propertyWorkersStateService.updateNameFilter(name);
    this.getDeviceUsersFiltered();
  }

  sortTable(sort: Sort) {
    this.propertyWorkersStateService.onSortTable(sort.active);
    this.getDeviceUsersFiltered();
  }

  getDeviceUsersFiltered() {
    this.getSites$ = this.propertyWorkersStateService
      .getDeviceUsersFiltered()
      .subscribe((data) => {
        if (data && data.model) {
          this.sitesDto = data.model;
          //this.getWorkerPropertiesAssignments();
        }
      });
  }



  updateTable() {
    this.getDeviceUsersFiltered();
    this.getWorkerPropertiesAssignments();
  }

  ngOnDestroy(): void {
  }
}
