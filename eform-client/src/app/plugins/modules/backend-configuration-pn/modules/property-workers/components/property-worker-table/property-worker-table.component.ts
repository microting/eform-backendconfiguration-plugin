import {Component, EventEmitter, Input, OnDestroy, OnInit, Output} from '@angular/core';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {MtxGridColumn} from '@ng-matero/extensions/grid';
import {DeviceUserModel, PropertyAssignWorkersModel} from '../../../../models';
import {BackendConfigurationPnPropertiesService} from '../../../../services';
import {TaskWizardStatusesEnum} from '../../../../enums';
import {PropertyWorkersStateService} from '../store';
import {TranslateService} from '@ngx-translate/core';
import {ActivatedRoute, Router} from '@angular/router';
import {AuthStateService} from 'src/app/common/store';
import {Subject, Subscription} from 'rxjs';
import {MatDialog} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {CommonDictionaryModel} from 'src/app/common/models';
import {Sort} from '@angular/material/sort';
import {debounceTime} from 'rxjs/operators';
import {
  PropertyWorkerCreateEditModalComponent,
  PropertyWorkerDeleteModalComponent,
  PropertyWorkerOtpModalComponent
} from '../';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {Store} from '@ngrx/store';
import {selectCurrentUserClaimsDeviceUsersDelete, selectCurrentUserClaimsDeviceUsersUpdate} from 'src/app/state';
import {
  selectPropertyWorkersNameFilters,
  selectPropertyWorkersPaginationIsSortDsc,
  selectPropertyWorkersPaginationSort
} from '../../../../state';

@AutoUnsubscribe()
@Component({
  selector: 'app-property-worker-table',
  templateUrl: './property-worker-table.component.html',
  styleUrls: ['./property-worker-table.component.scss'],
})
export class PropertyWorkerTableComponent implements OnInit, OnDestroy {
  //@Input() propertyWorkers: any[] = [];
  @Input() sitesDto: any[] = [];
  @Output() updateTable: EventEmitter<void> = new EventEmitter<void>();
  @Input() availableProperties: CommonDictionaryModel[] = [];
  @Input() workersAssignments: PropertyAssignWorkersModel[] = [];
  propertyWorkerOtpModalComponentAfterClosedSub$: Subscription;
  propertyWorkerEditModalComponentAfterClosedSub$: Subscription;
  getSites$: Subscription;
  deviceUserAssignments$: Subscription;
  //availableProperties: CommonDictionaryModel[];
  searchSubject = new Subject();
  deviceUsersDelete: boolean = false;
  deviceUsersUpdate: boolean = false;
  public selectCurrentUserClaimsDeviceUsersDelete$ = this.store.select(selectCurrentUserClaimsDeviceUsersDelete);
  public selectCurrentUserClaimsDeviceUsersUpdate$ = this.store.select(selectCurrentUserClaimsDeviceUsersUpdate);
  public selectPropertyWorkersPaginationSort$ = this.store.select(selectPropertyWorkersPaginationSort);
  public selectPropertyWorkersPaginationIsSortDsc$ = this.store.select(selectPropertyWorkersPaginationIsSortDsc);
  public selectPropertyWorkersNameFilters$ = this.store.select(selectPropertyWorkersNameFilters);

  constructor(
    private store: Store,
    private authStateService: AuthStateService,
    private translateService: TranslateService,
    public propertyWorkersStateService: PropertyWorkersStateService,
    private propertiesService: BackendConfigurationPnPropertiesService,
    private router: Router,
    private dialog: MatDialog,
    private overlay: Overlay,
    private route: ActivatedRoute,) {
    this.searchSubject.pipe(debounceTime(500)).subscribe((val) => {
      // @ts-ignore
      this.propertyWorkersStateService.updateNameFilter(val);
    });
    this.selectCurrentUserClaimsDeviceUsersDelete$.subscribe((data) => {
      this.deviceUsersDelete = data;
    });
    this.selectCurrentUserClaimsDeviceUsersUpdate$.subscribe((data) => {
      this.deviceUsersUpdate = data;
    });
  }


  tableHeaders: MtxGridColumn[] = [
    {
      header: this.translateService.stream('ID'),
      field: 'siteId',
      sortProp: {id: 'Id'},
      sortable: true,
    },
    {
      header: this.translateService.stream('Property'),
      field: 'propertyNames',
      formatter: (rowData: DeviceUserModel) => rowData.propertyNames.replace(',', '<br>'),
    },
    {
      header: this.translateService.stream('Name'),
      sortProp: {id: 'Name'},
      field: 'siteName',
      sortable: true,
      formatter: (rowData: DeviceUserModel) => rowData.siteName ? `${rowData.siteName}` : `N/A`,
    },
    {
      header: this.translateService.stream('Task management'),
      sortProp: {id: 'TaskManagementEnabled'},
      field: 'taskManagementEnabled',
      sortable: false,
      formatter: (model: DeviceUserModel) => this.translateService.instant(TaskWizardStatusesEnum[model.taskManagementEnabled ? 1 : 2]),
      //formatter: (rowData: DeviceUserModel) => rowData.siteName ? `${rowData.siteName}` : `N/A`,
    },
    {
      header: this.translateService.stream('Timeregistration'),
      sortProp: {id: 'TimeRegistrationEnabled'},
      field: 'timeRegistrationEnabled',
      sortable: false,
      formatter: (model: DeviceUserModel) => this.translateService.instant(TaskWizardStatusesEnum[model.timeRegistrationEnabled ? 1 : 2]),
      //formatter: (rowData: DeviceUserModel) => rowData.siteName ? `${rowData.siteName}` : `N/A`,
    },
    {
      header: this.translateService.stream('Language'),
      field: 'language',
      sortable: true,
      sortProp: {id: 'LanguageId'},
    },
    {
      header: this.translateService.stream('Customer no & OTP'),
      field: 'customerOtp',
    },
    {
      header: this.translateService.stream('Actions'),
      field: 'actions',
      disabled: this.deviceUsersDelete || this.deviceUsersUpdate,
    },
  ];


  // get userClaims() {
  //   return this.authStateService.currentUserClaims;
  // }

  openOtpModal(siteDto: DeviceUserModel) {
    if (!siteDto.unitId) {
      return;
    }
    this.propertyWorkerOtpModalComponentAfterClosedSub$ = this.dialog.open(PropertyWorkerOtpModalComponent,
      {...dialogConfigHelper(this.overlay, siteDto)})
      .afterClosed().subscribe(data => data ? this.updateTable.emit() : undefined);
  }

  // openDeleteDeviceUserModal(simpleSiteDto: DeviceUserModel) {
  //   // this.propertyWorkerOtpModalComponentAfterClosedSub$ = this.dialog.open(PropertyWorkerDeleteModalComponent,
  //   //   {...dialogConfigHelper(this.overlay, simpleSiteDto)})
  //   //   .afterClosed().subscribe(data => data ? this.getDeviceUsersFiltered() : undefined);
  // }

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
          //assignments: [],
          availableProperties: this.availableProperties,
        }), minWidth: 500
      })

      .afterClosed().subscribe(data => data ? this.updateTable.emit() : undefined);
    //.afterClosed().subscribe(data => data ? this.searchSubject.next('') : undefined);
  }

  openDeleteDeviceUserModal(simpleSiteDto: DeviceUserModel) {
    this.propertyWorkerOtpModalComponentAfterClosedSub$ = this.dialog.open(PropertyWorkerDeleteModalComponent,
      {...dialogConfigHelper(this.overlay, simpleSiteDto)})
      .afterClosed().subscribe(data => data ? this.updateTable.emit() : undefined);
  }

  // getDeviceUsersFiltered() {
  //   this.getSites$ = this.propertyWorkersStateService
  //     .getDeviceUsersFiltered()
  //     .subscribe((data) => {
  //       if (data && data.model) {
  //         this.sitesDto = data.model;
  //         //this.getWorkerPropertiesAssignments();
  //       }
  //     });
  // }

  // getWorkerPropertiesAssignments() {
  //   this.deviceUserAssignments$ = this.propertiesService
  //     .getPropertiesAssignments()
  //     .subscribe((operation) => {
  //       if (operation && operation.success) {
  //         this.workersAssignments = [...operation.model];
  //       }
  //     });
  // }
  // getWorkerPropertyNames(siteId: number) {
  //   let resultString = '';
  //   if (this.workersAssignments) {
  //     const obj = this.workersAssignments.find((x) => x.siteId === siteId);
  //     if (obj) {
  //       obj.assignments
  //         .filter((x) => x.isChecked)
  //         .forEach((assignment) => {
  //           if (resultString.length !== 0) {
  //             resultString += '<br>';
  //           }
  //           resultString += this.availableProperties.find(
  //             (prop) => prop.id === assignment.propertyId
  //           ).name;
  //         });
  //     }
  //   }
  //
  //   return resultString ?
  //     // @ts-ignore
  //     `<span title="${resultString.replaceAll('<br>', '\n')}">${resultString}</span>` :
  //     '--';
  // }

  onSortTable(sort: Sort) {
    this.propertyWorkersStateService.onSortTable(sort.active);
    this.updateTable.emit();
  }

  onSearchChanged(name: string) {
    //this.updateTable.emit();
    this.searchSubject.next(name);
  }


  ngOnInit() {
    //this.getDeviceUsersFiltered();
  }

  ngOnDestroy(): void {
  }
}
