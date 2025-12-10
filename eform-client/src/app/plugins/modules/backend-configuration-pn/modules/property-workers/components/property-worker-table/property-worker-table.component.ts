import {Component, EventEmitter, Input, OnChanges, OnDestroy, OnInit, Output, SimpleChanges,
  inject
} from '@angular/core';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {MtxGridColumn} from '@ng-matero/extensions/grid';
import {DeviceUserModel, PropertyAssignWorkersModel} from '../../../../models';
import {TaskWizardStatusesEnum} from '../../../../enums';
import {PropertyWorkersStateService} from '../store';
import {TranslateService} from '@ngx-translate/core';
import {Subject, Subscription} from 'rxjs';
import {MatDialog} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {CommonDictionaryModel, SiteNameDto} from 'src/app/common/models';
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
import {format} from 'date-fns';
import {AuthStateService} from 'src/app/common/store';
import {AndroidIcon, iOSIcon, PasswordValidationIcon, PdfIcon} from 'src/app/common/const';
import {MatIconRegistry} from '@angular/material/icon';
import {DomSanitizer} from '@angular/platform-browser';

@AutoUnsubscribe()
@Component({
    selector: 'app-property-worker-table',
    templateUrl: './property-worker-table.component.html',
    styleUrls: ['./property-worker-table.component.scss'],
    standalone: false
})
export class PropertyWorkerTableComponent implements OnInit, OnDestroy, OnChanges {
  private store = inject(Store);
  private translateService = inject(TranslateService);
  private authStateService = inject(AuthStateService);
  public propertyWorkersStateService = inject(PropertyWorkersStateService);
  private dialog = inject(MatDialog);
  private overlay = inject(Overlay);
  private iconRegistry = inject(MatIconRegistry);
  private sanitizer = inject(DomSanitizer);

  //@Input() propertyWorkers: any[] = [];
  @Input() sitesDto: any[] = [];
  @Output() updateTable: EventEmitter<void> = new EventEmitter<void>();
  @Input() availableProperties: CommonDictionaryModel[] = [];
  @Input() workersAssignments: PropertyAssignWorkersModel[] = [];
  @Input() showResigned: boolean = false;
  @Input() availableTags: CommonDictionaryModel[] = [];
  @Input() alreadyUsedEmails: string[] = [];
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



  ngOnChanges(changes: SimpleChanges): void {
    if (changes['showResigned']) {
      this.buildTableHeaders();
    }
  }


  public tableHeaders: MtxGridColumn[] = [];


  buildTableHeaders() {
    const baseHeaders: MtxGridColumn[] = [
      {
        header: this.translateService.stream('ID'),
        field: 'siteId',
        sortProp: {id: 'SiteId'},
        sortable: true,
      },
      // "Resigned" column will be conditionally inserted here
      {
        header: this.translateService.stream('Employee no'),
        field: 'employeeNo',
        sortProp: {id: 'EmployeeNo'},
        sortable: true,
      },
      {
        header: this.translateService.stream('Property'),
        field: 'propertyNames',
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
      },
      {
        header: this.translateService.stream('Timeregistration'),
        sortProp: {id: 'TimeRegistrationEnabled'},
        field: 'timeRegistrationEnabled',
        sortable: true,
      },
      {
        header: this.translateService.stream('Web'),
        sortProp: {id: 'WebAccessEnabled'},
        field: 'webAccessEnabled',
        sortable: true,
      },
      {
        header: this.translateService.stream('Archive'),
        sortProp: {id: 'ArchiveEnabled'},
        field: 'archiveEnabled',
        sortable: true,
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
      {header: this.translateService.stream('Tags'), field: 'tags',},
      {
        header: this.translateService.stream('Actions'),
        field: 'actions',
        width: '160px',
        pinned: 'right',
        disabled: this.deviceUsersDelete || this.deviceUsersUpdate,
      },
    ];

    // const hasResigned = this.sitesDto && this.sitesDto.some((row: DeviceUserModel) => row.resigned);

    if (this.showResigned) {
      baseHeaders.splice(1, 0, {
        header: this.translateService.stream('Resigned'),
        field: 'resignedAtDate',
        type: 'date',
        formatter: (rowData: DeviceUserModel) => rowData.resigned ? this.getFormattedDate(rowData.resignedAtDate) : '-',
        sortProp: {id: 'ResignedAtDate'},
        sortable: true,
      });
    }

    this.tableHeaders = baseHeaders;
  }


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
    selectedSimpleSite.resigned = simpleSiteDto.resigned;
    selectedSimpleSite.resignedAtDate = simpleSiteDto.resignedAtDate;
    selectedSimpleSite.tags = simpleSiteDto.tags;
    selectedSimpleSite.webAccessEnabled = simpleSiteDto.webAccessEnabled;
    selectedSimpleSite.archiveEnabled = simpleSiteDto.archiveEnabled;
    selectedSimpleSite.enableMobileAccess = simpleSiteDto.enableMobileAccess;

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
          availableTags: this.availableTags,
          alreadyUsedEmails: this.alreadyUsedEmails
        }), minWidth: 1024
      })

      .afterClosed().subscribe(data => data ? this.updateTable.emit() : undefined);
    //.afterClosed().subscribe(data => data ? this.searchSubject.next('') : undefined);
  }

  openDeleteDeviceUserModal(simpleSiteDto: DeviceUserModel) {
    this.propertyWorkerOtpModalComponentAfterClosedSub$ = this.dialog.open(PropertyWorkerDeleteModalComponent,
      {...dialogConfigHelper(this.overlay, simpleSiteDto)})
      .afterClosed().subscribe(data => data ? this.updateTable.emit() : undefined);
  }

  getTagsBySiteDto(site: SiteNameDto): CommonDictionaryModel[] {
    return this.availableTags.filter(x => site.tags.some(y => y === x.id));
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
    this.iconRegistry.addSvgIconLiteral('password-validation', this.sanitizer.bypassSecurityTrustHtml(PasswordValidationIcon));
    this.iconRegistry.addSvgIconLiteral('android-icon', this.sanitizer.bypassSecurityTrustHtml(AndroidIcon));
    this.iconRegistry.addSvgIconLiteral('ios-icon', this.sanitizer.bypassSecurityTrustHtml(iOSIcon));
    this.searchSubject.pipe(debounceTime(500)).subscribe((val) => {
      this.propertyWorkersStateService.updateNameFilter(val);
    });
    this.selectCurrentUserClaimsDeviceUsersDelete$.subscribe((data) => {
      this.deviceUsersDelete = data;
    });
    this.selectCurrentUserClaimsDeviceUsersUpdate$.subscribe((data) => {
      this.deviceUsersUpdate = data;
    });

    this.buildTableHeaders();
    //this.getDeviceUsersFiltered();
  }

  ngOnDestroy(): void {
  }

  getFormattedDate(date: Date) {
    return format(date, 'P', {locale: this.authStateService.dateFnsLocale});
  }
}
