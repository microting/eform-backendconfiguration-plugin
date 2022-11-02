import {Component, OnDestroy, OnInit} from '@angular/core';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {Subscription} from 'rxjs';
import {
  CommonDictionaryModel,
} from 'src/app/common/models';
import {AuthStateService} from 'src/app/common/store';
import {PropertyAssignWorkersModel, DeviceUserModel,} from '../../../../models';
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

@AutoUnsubscribe()
@Component({
  selector: 'app-property-workers-page',
  templateUrl: './property-workers-page.component.html',
})
export class PropertyWorkersPageComponent implements OnInit, OnDestroy {
  selectedSimpleSiteDto: DeviceUserModel = new DeviceUserModel();
  selectedSimpleSite: DeviceUserModel = new DeviceUserModel();
  sitesDto: Array<DeviceUserModel>;
  availableProperties: CommonDictionaryModel[];
  workersAssignments: PropertyAssignWorkersModel[];
  tableHeaders: MtxGridColumn[] = [
    {
      header: this.translateService.stream('ID'),
      field: 'siteId',
      sortProp: {id: 'Id'},
      sortable: true,
    },
    {
      header: this.translateService.stream('Name'),
      sortProp: {id: 'Name'},
      field: 'siteName',
      sortable: true,
      formatter: (rowData: DeviceUserModel) => rowData.siteName ? `${rowData.siteName}` : `N/A`,
    },
    {
      header: this.translateService.stream('Language'),
      field: 'language',
      sortable: true,
      sortProp: {id: 'LanguageId'},
    },
    {
      header: this.translateService.stream('Property'),
      field: 'property',
      // formatter: (rowData: DeviceUserModel) => this.getWorkerPropertyNames(rowData.siteId),
    },
    {
      header: this.translateService.stream('Customer no & OTP'),
      field: 'customerOtp',
    },
    {
      header: this.translateService.stream('Actions'),
      field: 'actions',
      disabled: this.authStateService.currentUserClaims.deviceUsersDelete || this.authStateService.currentUserClaims.deviceUsersDelete,
      type: 'button',
      buttons: [
        {
          type: 'icon',
          click: (rowData: DeviceUserModel) => this.router
            .navigate(['/plugins/backend-configuration-pn/task-worker-assignments/', rowData.siteId], {relativeTo: this.route}),
          icon: 'visibility',
          tooltip: this.translateService.stream('Edit assignments')
        },
        {
          type: 'icon',
          color: 'accent',
          icon: 'edit',
          click: (rowData: DeviceUserModel) => this.openEditModal(rowData),
          tooltip: this.translateService.stream('Edit Device User'),
          iif: () => this.userClaims.deviceUsersUpdate,
        },
        {
          type: 'icon',
          color: 'warn',
          icon: 'delete',
          click: (rowData: DeviceUserModel) => this.openDeleteDeviceUserModal(rowData),
          tooltip: this.translateService.stream('Delete Device User'),
          iif: (rowData: DeviceUserModel) => this.userClaims.deviceUsersDelete && !rowData.isLocked,
        },
      ],
    },
  ];

  getSites$: Subscription;
  getPropertiesDictionary$: Subscription;
  deviceUserAssignments$: Subscription;
  propertyWorkerOtpModalComponentAfterClosedSub$: Subscription;
  propertyWorkerEditModalComponentAfterClosedSub$: Subscription;
  propertyWorkerCreateModalComponentAfterClosedSub$: Subscription;

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
    this.getDeviceUsersFiltered();
    this.getPropertiesDictionary();
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

  openOtpModal(siteDto: DeviceUserModel) {
    if (!siteDto.unitId) {
      return;
    }
    this.propertyWorkerOtpModalComponentAfterClosedSub$ = this.dialog.open(PropertyWorkerOtpModalComponent,
      {...dialogConfigHelper(this.overlay, siteDto)})
      .afterClosed().subscribe(data => data ? this.getDeviceUsersFiltered() : undefined);
  }

  openDeleteDeviceUserModal(simpleSiteDto: DeviceUserModel) {
    this.propertyWorkerOtpModalComponentAfterClosedSub$ = this.dialog.open(PropertyWorkerDeleteModalComponent,
      {...dialogConfigHelper(this.overlay, simpleSiteDto)})
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
          this.getWorkerPropertiesAssignments();
        }
      });
  }

  ngOnDestroy(): void {
  }
}
