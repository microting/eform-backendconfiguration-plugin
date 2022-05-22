import { Component, OnDestroy, OnInit, ViewChild } from '@angular/core';
import { AutoUnsubscribe } from 'ngx-auto-unsubscribe';
import { Subscription } from 'rxjs';
import {
  CommonDictionaryModel,
  SiteDto,
  TableHeaderElementModel,
} from 'src/app/common/models';
import { AuthStateService } from 'src/app/common/store';
import { PropertyAssignWorkersModel } from '../../../../models';
import { BackendConfigurationPnPropertiesService } from '../../../../services';
import { PropertyWorkersStateService } from '../store';
import {DeviceUserModel} from 'src/app/plugins/modules/backend-configuration-pn/models/device-users';

@AutoUnsubscribe()
@Component({
  selector: 'app-property-workers-page',
  templateUrl: './property-workers-page.component.html',
})
export class PropertyWorkersPageComponent implements OnInit, OnDestroy {
  @ViewChild('editDeviceUserModal', { static: true }) editDeviceUserModal;
  @ViewChild('newOtpModal', { static: true }) newOtpModal;
  @ViewChild('deleteDeviceUserModal', { static: true }) deleteDeviceUserModal;

  selectedSimpleSiteDto: DeviceUserModel = new DeviceUserModel();
  selectedSimpleSite: DeviceUserModel = new DeviceUserModel();
  sitesDto: Array<DeviceUserModel>;
  availableProperties: CommonDictionaryModel[];
  workersAssignments: PropertyAssignWorkersModel[];

  tableHeaders: TableHeaderElementModel[] = [
    {
      name: 'Id',
      visibleName: 'ID',
      sortable: true,
      elementId: '',
    },
    { name: 'Name', sortable: true, elementId: '' },
    // { name: 'Device ID', sortable: false, elementId: '' },
    {
      name: 'LanguageId',
      visibleName: 'Language',
      sortable: true,
      elementId: '',
    },
    { name: 'Property', sortable: false, elementId: '' },
    { name: 'Customer no & OTP', sortable: false, elementId: '' },
    this.authStateService.currentUserClaims.deviceUsersDelete ||
    this.authStateService.currentUserClaims.deviceUsersDelete
      ? { name: 'Actions', sortable: false, elementId: '' }
      : null,
  ];

  getSites$: Subscription;
  getPropertiesDictionary$: Subscription;
  deviceUserAssignments$: Subscription;

  get userClaims() {
    return this.authStateService.currentUserClaims;
  }

  constructor(
    private authStateService: AuthStateService,
    private propertiesService: BackendConfigurationPnPropertiesService,
    public propertyWorkersStateService: PropertyWorkersStateService
  ) {}

  ngOnInit() {
    this.getDeviceUsersFiltered();
    this.getPropertiesDictionary();
  }

  openEditModal(simpleSiteDto: DeviceUserModel) {
    this.selectedSimpleSite.userFirstName = simpleSiteDto.userFirstName;
    this.selectedSimpleSite.userLastName = simpleSiteDto.userLastName;
    this.selectedSimpleSite.id = simpleSiteDto.siteUid;
    this.selectedSimpleSite.languageCode = simpleSiteDto.languageCode;
    this.selectedSimpleSite.normalId = simpleSiteDto.siteId;
    this.selectedSimpleSite.isLocked = simpleSiteDto.isLocked;
    this.selectedSimpleSite.timeRegistrationEnabled = simpleSiteDto.timeRegistrationEnabled;

    const workersAssignments = this.workersAssignments.find(
      (x) => x.siteId === simpleSiteDto.siteId
    );
    this.editDeviceUserModal.show(
      this.selectedSimpleSite,
      workersAssignments ? workersAssignments.assignments : []
    );
  }

  openOtpModal(siteDto: DeviceUserModel) {
    if (!siteDto.unitId) {
      return;
    }
    this.selectedSimpleSiteDto = siteDto;
    this.newOtpModal.show();
  }

  openDeleteDeviceUserModal(simpleSiteDto: DeviceUserModel) {
    this.selectedSimpleSiteDto = simpleSiteDto;
    this.deleteDeviceUserModal.show();
  }

  // loadAllSimpleSites() {
  //   this.getSites$ = this.deviceUsersService
  //     .getAllDeviceUsers()
  //     .subscribe((operation) => {
  //       if (operation && operation.success) {
  //         this.sitesDto = operation.model;
  //         this.getWorkerPropertiesAssignments();
  //       }
  //     });
  // }

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

    return resultString;
  }

  onSearchChanged(name: string) {
    this.propertyWorkersStateService.updateNameFilter(name);
    this.getDeviceUsersFiltered();
  }

  sortTable(sort: string) {
    this.propertyWorkersStateService.onSortTable(sort);
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

  ngOnDestroy(): void {}
}
