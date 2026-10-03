import {Component, EventEmitter, Input, OnChanges, OnDestroy, OnInit, Output, SimpleChanges,
  inject, ElementRef, AfterViewChecked
} from '@angular/core';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {MtxGridColumn} from '@ng-matero/extensions/grid';
import {AppInstallModel, DeviceUserModel, PropertyAssignWorkersModel} from '../../../../models';
import {APP_INSTALL_STATE_CLASS, APP_INSTALL_STATE_LABEL, appInstallState, AppInstallState} from './app-install-state';
import {PropertyWorkersStateService} from '../store';
import {TranslateService} from '@ngx-translate/core';
import {combineLatest, Subject, Subscription, of} from 'rxjs';
import {MatDialog} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {CommonDictionaryModel, SiteNameDto} from 'src/app/common/models';
import {Sort} from '@angular/material/sort';
import {debounceTime, map} from 'rxjs/operators';
import {
  PropertyWorkerCreateEditModalComponent,
  PropertyWorkerDeleteModalComponent,
  PropertyWorkerOtpModalComponent,
  PropertyWorkerQrModalComponent,
} from '../';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {UserSetPasswordComponent} from 'src/app/modules/account-management/components';
import {AuthService} from 'src/app/common/services';
import {Store} from '@ngrx/store';
import {
  selectAuthIsAdmin,
  selectCurrentUserClaimsDeviceUsersDelete,
  selectCurrentUserClaimsDeviceUsersUpdate,
  selectCurrentUserIsFirstUser
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
export class PropertyWorkerTableComponent implements OnInit, OnDestroy, OnChanges, AfterViewChecked {
  private store = inject(Store);
  private translateService = inject(TranslateService);
  private authStateService = inject(AuthStateService);
  public propertyWorkersStateService = inject(PropertyWorkersStateService);
  private authService = inject(AuthService);
  private dialog = inject(MatDialog);
  private overlay = inject(Overlay);
  private el = inject(ElementRef);

  //@Input() propertyWorkers: any[] = [];
  @Input() sitesDto: any[] = [];
  @Output() updateTable: EventEmitter<void> = new EventEmitter<void>();
  @Input() availableProperties: CommonDictionaryModel[] = [];
  @Input() workersAssignments: PropertyAssignWorkersModel[] = [];
  @Input() showResigned: boolean = false;
  @Input() availableTags: CommonDictionaryModel[] = [];
  @Input() alreadyUsedEmails: string[] = [];
  @Input() highlightedSiteId: number | null = null;
  private pendingScrollToSiteId: number | null = null;
  private waitingForFreshData = false;
  @Output() updateTableWithHighlight: EventEmitter<number> = new EventEmitter<number>();
  @Output() highlightedRowRendered: EventEmitter<void> = new EventEmitter<void>();
  /**
   * #1380 — rows ticked in the checkbox column (only rendered for users who may
   * edit workers). Row clicks do not select (`disableRowClickSelection`), so the
   * row's own buttons keep working. Bound back as `[rowSelected]` because
   * mtx-grid rebuilds its selection from that input on every input change.
   */
  @Output() selectionChanged: EventEmitter<DeviceUserModel[]> = new EventEmitter<DeviceUserModel[]>();
  selectedRows: DeviceUserModel[] = [];
  propertyWorkerOtpModalComponentAfterClosedSub$: Subscription;
  propertyWorkerEditModalComponentAfterClosedSub$: Subscription;
  canDeleteWorkerSub$: Subscription;
  //availableProperties: CommonDictionaryModel[];
  searchSubject: Subject<string> = new Subject();
  canDeleteWorker: boolean = false;
  deviceUsersUpdate: boolean = false;
  // Only the first user may delete a worker, and only while also holding the device_users_delete
  // claim that core's delete endpoint requires.
  public canDeleteWorker$ = combineLatest([
    this.store.select(selectCurrentUserIsFirstUser),
    this.store.select(selectCurrentUserClaimsDeviceUsersDelete),
  ]).pipe(map(([isFirstUser, deviceUsersDelete]) => isFirstUser && deviceUsersDelete));
  public selectCurrentUserClaimsDeviceUsersUpdate$ = this.store.select(selectCurrentUserClaimsDeviceUsersUpdate);
  public selectPropertyWorkersPaginationSort$ = this.store.select(selectPropertyWorkersPaginationSort);
  public selectPropertyWorkersPaginationIsSortDsc$ = this.store.select(selectPropertyWorkersPaginationIsSortDsc);
  public selectPropertyWorkersNameFilters$ = this.store.select(selectPropertyWorkersNameFilters);
  public selectAuthIsAdmin$ = this.store.select(selectAuthIsAdmin);


  ngOnChanges(changes: SimpleChanges): void {
    if (changes['sitesDto'] && this.selectedRows.length > 0) {
      // A reload hands mtx-grid new row objects, so the old selection no longer
      // matches anything on screen: start over. Emitted after this change
      // detection pass, as the page's toolbar button has already read it.
      this.selectedRows = [];
      Promise.resolve().then(() => this.selectionChanged.emit([]));
    }
    if (changes['showResigned']) {
      this.buildTableHeaders();
    }
    if (changes['highlightedSiteId'] && this.highlightedSiteId) {
      // Mark that we need to wait for fresh sitesDto before scrolling
      this.waitingForFreshData = true;
      this.pendingScrollToSiteId = null;
    }
    if (changes['sitesDto'] && this.waitingForFreshData && this.highlightedSiteId) {
      // Fresh data has arrived after highlight was requested — now we can scroll
      this.waitingForFreshData = false;
      this.pendingScrollToSiteId = this.highlightedSiteId;
    }
  }

  ngAfterViewChecked(): void {
    if (this.pendingScrollToSiteId && this.sitesDto?.length) {
      const siteId = this.pendingScrollToSiteId;
      const index = this.sitesDto.findIndex(s => s.siteId === siteId);
      if (index >= 0) {
        this.pendingScrollToSiteId = null;
        setTimeout(() => {
          const row = this.el.nativeElement.querySelector(`#deviceUserId-${index}`);
          if (row) {
            const tr = row.closest('tr') || row.closest('mat-row') || row.parentElement;
            if (tr) {
              tr.scrollIntoView({behavior: 'smooth', block: 'center'});
              tr.classList.add('highlight-row');
              setTimeout(() => tr.classList.remove('highlight-row'), 5000);
            }
          }
          this.highlightedRowRendered.emit();
        });
      }
    }
  }

  rowClassFormatter = (rowData: any) => {
    if (this.highlightedSiteId && rowData.siteId === this.highlightedSiteId) {
      return {'highlight-row': true};
    }
    return {};
  };


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
      {header: this.translateService.stream('Tags'), field: 'tags',},
      {
        header: this.translateService.stream('Language'),
        field: 'language',
        sortable: true,
        sortProp: {id: 'LanguageId'},
      },
      // #1335: one column per mobile app. They replace the eForm / Ad hoc / Time /
      // Archive permission chips (the toggles stay in the edit dialog) and the
      // Model & OS / Software version columns (device details are the tooltip).
      // App names are the same in every language (Time is the app, not "Tid").
      {header: of('Compliance'), field: 'complianceApp'},
      {header: of('Ad-hoc'), field: 'adHocApp'},
      {header: of('Time'), field: 'timeApp'},
      {header: of('Archive'), field: 'archiveApp'},
      {
        header: this.translateService.stream('Customer no & OTP'),
        field: 'customerOtp',
      },
      {
        header: this.translateService.stream('Actions'),
        field: 'actions',
        width: '160px',
        pinned: 'right',
        disabled: this.canDeleteWorker || this.deviceUsersUpdate,
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


  onRowSelected(rows: DeviceUserModel[]) {
    this.selectedRows = rows;
    this.selectionChanged.emit(rows);
  }

  appState(app: AppInstallModel | undefined): AppInstallState {
    return appInstallState(app);
  }

  appStateLabel(app: AppInstallModel | undefined): string {
    return APP_INSTALL_STATE_LABEL[appInstallState(app)];
  }

  appStateClass(app: AppInstallModel | undefined): string {
    return APP_INSTALL_STATE_CLASS[appInstallState(app)];
  }

  /** Model and OS of the device that reported the version; empty when unknown. */
  appDeviceTooltip(app: AppInstallModel | undefined): string {
    const parts: string[] = [];
    // Model and manufacturer are reported independently; show whichever is known.
    const device = [app?.model, app?.manufacturer ? `(${app.manufacturer})` : null].filter(Boolean).join(' ');
    if (device) {
      parts.push(`${this.translateService.instant('Model')}: ${device}`);
    }
    if (app?.osVersion) {
      parts.push(`${this.translateService.instant('OS version')}: ${app.osVersion}`);
    }
    return parts.join(' · ');
  }

  // get userClaims() {
  //   return this.authStateService.currentUserClaims;
  // }

  openOtpModal(siteDto: DeviceUserModel) {
    if (!siteDto.unitId) {
      return;
    }
    this.propertyWorkerOtpModalComponentAfterClosedSub$ =
      this.dialog.open(PropertyWorkerOtpModalComponent,
      {...dialogConfigHelper(this.overlay, siteDto)})
      .afterClosed().subscribe(data => data ? this.updateTable.emit() : undefined);
  }

  openSetPasswordModal(row: DeviceUserModel) {
    const dialogRef = this.dialog.open(UserSetPasswordComponent,
      dialogConfigHelper(this.overlay, { email: row.workerEmail }));
    dialogRef.componentInstance.userPasswordSet?.subscribe(() => {
      dialogRef.close();
    });
  }

  sendResetPasswordEmail(row: DeviceUserModel) {
    this.authService.sendEmailRecoveryLink({ email: row.workerEmail }).subscribe();
  }

  openQrModal(row: DeviceUserModel) {
    this.dialog.open(PropertyWorkerQrModalComponent, {
      ...dialogConfigHelper(this.overlay, {
        customerNo: row.customerNo,
        otpCode: row.otpCode,
      }),
    });
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

    this.propertyWorkerEditModalComponentAfterClosedSub$ =
      this.dialog.open(PropertyWorkerCreateEditModalComponent,
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

      .afterClosed().subscribe(data => {
        if (data) {
          if (typeof data === 'number') {
            this.updateTableWithHighlight.emit(data);
          } else {
            this.updateTable.emit();
          }
        }
      });
  }

  openDeleteDeviceUserModal(simpleSiteDto: DeviceUserModel) {
    this.propertyWorkerOtpModalComponentAfterClosedSub$ =
      this.dialog.open(PropertyWorkerDeleteModalComponent,
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
    this.searchSubject.pipe(debounceTime(500)).subscribe((val) => {
      this.propertyWorkersStateService.updateNameFilter(val);
    });
    this.canDeleteWorkerSub$ = this.canDeleteWorker$.subscribe((data) => {
      this.canDeleteWorker = data;
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
