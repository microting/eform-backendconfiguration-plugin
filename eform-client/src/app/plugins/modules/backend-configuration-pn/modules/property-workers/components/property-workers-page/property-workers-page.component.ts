import { Component, OnDestroy, OnInit, ViewChild } from '@angular/core';
import { AutoUnsubscribe } from 'ngx-auto-unsubscribe';
import { Subscription } from 'rxjs';
import { DeviceUserModel } from 'src/app/common/models/device-users';
import { SiteDto } from 'src/app/common/models/dto';
import { DeviceUserService } from 'src/app/common/services/device-users';
import {
  CommonDictionaryModel,
  TableHeaderElementModel,
} from 'src/app/common/models';
import { AuthStateService } from 'src/app/common/store';
import {
  PropertyAssignmentWorkerModel,
  PropertyAssignWorkersModel
} from '../../../../models/properties/property-workers-assignment.model';
import { BackendConfigurationPnPropertiesService } from '../../../../services';

@AutoUnsubscribe()
@Component({
  selector: 'app-property-workers-page',
  templateUrl: './property-workers-page.component.html',
})
export class PropertyWorkersPageComponent implements OnInit, OnDestroy {
  @ViewChild('editDeviceUserModal', { static: true }) editDeviceUserModal;
  @ViewChild('newOtpModal', { static: true }) newOtpModal;
  @ViewChild('deleteDeviceUserModal', { static: true }) deleteDeviceUserModal;

  selectedSimpleSiteDto: SiteDto = new SiteDto();
  selectedSimpleSite: DeviceUserModel = new DeviceUserModel();
  sitesDto: Array<SiteDto>;
  availableProperties: CommonDictionaryModel[];
  workersAssignments: PropertyAssignWorkersModel[];

  tableHeaders: TableHeaderElementModel[] = [
    { name: 'Site ID', sortable: false, elementId: '' },
    { name: 'First name', sortable: false, elementId: '' },
    { name: 'Last name', sortable: false, elementId: '' },
    { name: 'Device ID', sortable: false, elementId: '' },
    { name: 'Language', sortable: false, elementId: '' },
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
    private deviceUsersService: DeviceUserService,
    private authStateService: AuthStateService,
    private propertiesService: BackendConfigurationPnPropertiesService
  ) {}

  ngOnInit() {
    this.loadAllSimpleSites();
    this.getPropertiesDictionary();
  }

  openEditModal(simpleSiteDto: SiteDto) {
    this.selectedSimpleSite.userFirstName = simpleSiteDto.firstName;
    this.selectedSimpleSite.userLastName = simpleSiteDto.lastName;
    this.selectedSimpleSite.id = simpleSiteDto.siteUid;
    this.selectedSimpleSite.languageCode = simpleSiteDto.languageCode;

    this.editDeviceUserModal.show(simpleSiteDto);
  }

  openOtpModal(siteDto: SiteDto) {
    if (!siteDto.unitId) {
      return;
    }
    this.selectedSimpleSiteDto = siteDto;
    this.newOtpModal.show();
  }

  openDeleteDeviceUserModal(simpleSiteDto: SiteDto) {
    this.selectedSimpleSiteDto = simpleSiteDto;
    this.deleteDeviceUserModal.show();
  }

  loadAllSimpleSites() {
    this.getSites$ = this.deviceUsersService
      .getAllDeviceUsers()
      .subscribe((operation) => {
        if (operation && operation.success) {
          this.sitesDto = operation.model;
          this.getWorkerPropertiesAssignments();
        }
      });
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

  ngOnDestroy(): void {}
}
