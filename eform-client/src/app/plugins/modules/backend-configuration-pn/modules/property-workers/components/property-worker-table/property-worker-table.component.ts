import {Component, EventEmitter, Input, OnDestroy, OnInit, Output} from '@angular/core';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {MtxGridColumn} from '@ng-matero/extensions/grid';
import {DeviceUserModel, PropertyAssignWorkersModel} from '../../../../models';
import {TaskWizardStatusesEnum} from '../../../../enums';
import {PropertyWorkersStateService} from '../store';
import {TranslateService} from '@ngx-translate/core';
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
import {
  selectAuthIsAdmin,
  selectCurrentUserClaimsDeviceUsersDelete,
  selectCurrentUserClaimsDeviceUsersUpdate
} from 'src/app/state';
import {
  selectPropertyWorkersNameFilters,
  selectPropertyWorkersPaginationIsSortDsc,
  selectPropertyWorkersPaginationSort
} from '../../../../state';
import {format} from "date-fns";
import {AuthStateService} from "src/app/common/store";

@AutoUnsubscribe()
@Component({
    selector: 'app-property-worker-table',
    templateUrl: './property-worker-table.component.html',
    styleUrls: ['./property-worker-table.component.scss'],
    standalone: false
})
export class PropertyWorkerTableComponent implements OnInit, OnDestroy {
  //@Input() propertyWorkers: any[] = [];
  @Input() sitesDto: any[] = [];
  @Output() updateTable: EventEmitter<void> = new EventEmitter<void>();
  @Input() availableProperties: CommonDictionaryModel[] = [];
  @Input() workersAssignments: PropertyAssignWorkersModel[] = [];
  propertyWorkerOtpModalComponentAfterClosedSub$: Subscription;
  propertyWorkerEditModalComponentAfterClosedSub$: Subscription;
  //availableProperties: CommonDictionaryModel[];
  searchSubject: Subject<string> = new Subject();
  deviceUsersDelete: boolean = false;
  deviceUsersUpdate: boolean = false;
  public selectCurrentUserClaimsDeviceUsersDelete$ = this.store.select(selectCurrentUserClaimsDeviceUsersDelete);
  public selectCurrentUserClaimsDeviceUsersUpdate$ = this.store.select(selectCurrentUserClaimsDeviceUsersUpdate);
  public selectPropertyWorkersPaginationSort$ = this.store.select(selectPropertyWorkersPaginationSort);
  public selectPropertyWorkersPaginationIsSortDsc$ = this.store.select(selectPropertyWorkersPaginationIsSortDsc);
  public selectPropertyWorkersNameFilters$ = this.store.select(selectPropertyWorkersNameFilters);
  public selectAuthIsAdmin$ = this.store.select(selectAuthIsAdmin);

  get TaskWizardStatusesEnum() {
    return TaskWizardStatusesEnum;
  }

  constructor(
    private store: Store,
    private translateService: TranslateService,
    private authStateService: AuthStateService,
    public propertyWorkersStateService: PropertyWorkersStateService,
    private dialog: MatDialog,
    private overlay: Overlay,) {
    this.searchSubject.pipe(debounceTime(500)).subscribe((val) => {
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
      sortProp: {id: 'SiteId'},
      sortable: true,
    },
    {
      header: this.translateService.stream('Employee no'),
      field: 'employeeNo',
      sortProp: {id: 'EmployeeNo'},
      sortable: true,
    },
    {
      header: this.translateService.stream('Property'),
      field: 'propertyNames',
      formatter: (rowData: DeviceUserModel) => rowData.propertyNames.replace(',', '<br>'),
    },
    {
      header: this.translateService.stream('Name'),
      sortProp: {id: 'SiteName'},
      field: 'siteName',
      sortable: true,
      formatter: (rowData: DeviceUserModel) => rowData.siteName ? `${rowData.siteName}` : `N/A`,
    },
    {
      header: this.translateService.stream('Email'),
      sortProp: {id: 'WorkerEmail'},
      field: 'workerEmail',
      sortable: true,
      formatter: (rowData: DeviceUserModel) => rowData.workerEmail ? `${rowData.workerEmail}` : `N/A`,
    },
    {
      header: this.translateService.stream('Phone number'),
      sortProp: {id: 'PhoneNumber'},
      field: 'phoneNumber',
      sortable: true,
      formatter: (rowData: DeviceUserModel) => rowData.phoneNumber ? `${rowData.phoneNumber}` : `N/A`,
    },
    {
      header: this.translateService.stream('Task management'),
      sortProp: {id: 'TaskManagementEnabled'},
      field: 'taskManagementEnabled',
      sortable: true,
      //formatter: (rowData: DeviceUserModel) => rowData.siteName ? `${rowData.siteName}` : `N/A`,
    },
    {
      header: this.translateService.stream('Timeregistration'),
      sortProp: {id: 'TimeRegistrationEnabled'},
      field: 'timeRegistrationEnabled',
      sortable: true,
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
      header: this.translateService.stream('Model & OS version'),
      field: 'manufacturer',
      sortable: true,
      sortProp: {id: 'Manufacturer'},
    },
    {
      header: this.translateService.stream('Software version'),
      field: 'version',
      sortable: true,
      sortProp: {id: 'Version'},
    },
    {
      header: this.translateService.stream('Actions'),
      field: 'actions',
      width: '160px',
      pinned: 'right',
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
    selectedSimpleSite.pinCode = simpleSiteDto.pinCode;
    selectedSimpleSite.employeeNo = simpleSiteDto.employeeNo;
    selectedSimpleSite.startMonday = simpleSiteDto.startMonday;
    selectedSimpleSite.endMonday = simpleSiteDto.endMonday;
    selectedSimpleSite.breakMonday = simpleSiteDto.breakMonday;
    selectedSimpleSite.startTuesday = simpleSiteDto.startTuesday;
    selectedSimpleSite.endTuesday = simpleSiteDto.endTuesday;
    selectedSimpleSite.breakTuesday = simpleSiteDto.breakTuesday;
    selectedSimpleSite.startWednesday = simpleSiteDto.startWednesday;
    selectedSimpleSite.endWednesday = simpleSiteDto.endWednesday;
    selectedSimpleSite.breakWednesday = simpleSiteDto.breakWednesday;
    selectedSimpleSite.startThursday = simpleSiteDto.startThursday;
    selectedSimpleSite.endThursday = simpleSiteDto.endThursday;
    selectedSimpleSite.breakThursday = simpleSiteDto.breakThursday;
    selectedSimpleSite.startFriday = simpleSiteDto.startFriday;
    selectedSimpleSite.endFriday = simpleSiteDto.endFriday;
    selectedSimpleSite.breakFriday = simpleSiteDto.breakFriday;
    selectedSimpleSite.startSaturday = simpleSiteDto.startSaturday;
    selectedSimpleSite.endSaturday = simpleSiteDto.endSaturday;
    selectedSimpleSite.breakSaturday = simpleSiteDto.breakSaturday;
    selectedSimpleSite.startSunday = simpleSiteDto.startSunday;
    selectedSimpleSite.endSunday = simpleSiteDto.endSunday;
    selectedSimpleSite.breakSunday = simpleSiteDto.breakSunday;
    selectedSimpleSite.workerEmail = simpleSiteDto.workerEmail;
    selectedSimpleSite.phoneNumber = simpleSiteDto.phoneNumber;

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

  getFormattedDate(date: Date) {
    return format(date, 'P', {locale: this.authStateService.dateFnsLocale});
  }
}
