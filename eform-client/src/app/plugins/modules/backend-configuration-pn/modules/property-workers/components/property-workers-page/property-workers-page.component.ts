import {
  Component, OnDestroy, OnInit,
  inject, ViewChild
} from '@angular/core';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {FormControl, FormGroup, FormsModule, ReactiveFormsModule} from '@angular/forms';
import {BehaviorSubject, combineLatest, forkJoin, Subscription, take} from 'rxjs';
import {
  CommonDictionaryModel,
} from 'src/app/common/models';
import {AppMenuStateService, AuthStateService} from 'src/app/common/store';
import {PropertyAssignWorkersModel, DeviceUserModel, TaskWizardModel,} from '../../../../models';
import {BackendConfigurationPnPropertiesService} from '../../../../services';
import {PropertyWorkerBulkTagsModalComponent, PropertyWorkerBulkTagsModalData} from '../property-worker-bulk-tags-modal/property-worker-bulk-tags-modal.component';
import {PropertyWorkerCreateEditModalComponent} from '../property-worker-create-edit-modal/property-worker-create-edit-modal.component';
import {
  TailBiteManagersDialogComponent,
  TailBiteManagersDialogData,
} from '../tail-bite-managers-dialog/tail-bite-managers-dialog.component';
import {PropertyWorkersStateService} from '../store';
import {Sort} from '@angular/material/sort';
import {TranslateService, TranslatePipe} from '@ngx-translate/core';
import {ActivatedRoute, Router} from '@angular/router';
import {MatDialog} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {map, tap} from 'rxjs/operators';
import * as R from 'ramda';
import {
  selectCurrentUserClaimsDeviceUsersCreate,
  selectCurrentUserClaimsDeviceUsersUpdate,
  selectCurrentUserIsAdmin,
  selectCurrentUserIsFirstUser
} from 'src/app/state/auth/auth.selector';
import {Store} from '@ngrx/store';
import {
  selectPropertyWorkersFilters, selectPropertyWorkersNameFilters
} from '../../../../state/property-workers/property-workers.selector';
import {EformTagService} from 'src/app/common/services';
import {EformsTagsComponent} from 'src/app/common/modules/eform-shared-tags/components';
import {CommonTagModel} from '../../../../models';
import {NgIf, AsyncPipe} from '@angular/common';
import {MatIcon} from '@angular/material/icon';
import {MatCard} from '@angular/material/card';
import {MatFormField, MatLabel, MatSuffix} from '@angular/material/form-field';
import {MatInput} from '@angular/material/input';
import {MtxSelect} from '@ng-matero/extensions/select';
import {MatTooltip} from '@angular/material/tooltip';
import {MatSlideToggle} from '@angular/material/slide-toggle';
import {PropertyWorkerTableComponent} from '../property-worker-table/property-worker-table.component';
import {CallerFlags, NO_CALLER_FLAGS, callerFlags as readCallerFlags, newWorkerRow} from '../../leader.helper';

@AutoUnsubscribe()
@Component({
    selector: 'app-property-workers-page',
    imports: [NgIf, AsyncPipe, FormsModule, ReactiveFormsModule, TranslatePipe, MatIcon, MatCard, MatFormField, MatLabel, MatSuffix, MatInput, MtxSelect, MatTooltip, MatSlideToggle, PropertyWorkerTableComponent, EformsTagsComponent],
    templateUrl: './property-workers-page.component.html',
})
export class PropertyWorkersPageComponent implements OnInit, OnDestroy {
  private store = inject(Store);
  private propertiesService = inject(BackendConfigurationPnPropertiesService);
  public propertyWorkersStateService = inject(PropertyWorkersStateService);
  private appMenuStateService = inject(AppMenuStateService);
  private router = inject(Router);
  private dialog = inject(MatDialog);
  private overlay = inject(Overlay);
  private eFormTagService = inject(EformTagService);
  // Resolved once in ngOnInit — getTitleByUrl subscribes to the store
  // internally without unsubscribing, so calling it from the template would
  // leak one subscription per change-detection tick.
  public pageTitle: string = '';
  @ViewChild('modalTags', {static: true}) modalSiteTags: EformsTagsComponent;

  sitesDto: Array<DeviceUserModel>;
  availableProperties: CommonDictionaryModel[];
  workersAssignments: PropertyAssignWorkersModel[];
  alreadyUsedEmails: string[] = [];

  getSites$: Subscription;
  propertyWorkerCreateModalComponentAfterClosedSub$: Subscription;
  bulkTagsModalAfterClosedSub$: Subscription;
  getFiltersAsyncSub$: Subscription;
  filtersInitSub$: Subscription;
  valueChangesPropertyIdsSub$: Subscription;
  valueChangesShowResignedSub$: Subscription;
  titleSub$: Subscription;
  filtersForm = new FormGroup({
    propertyIds: new FormControl<number[]>([]),
    showResigned: new FormControl<boolean>(false),
  });
  showResigned: boolean = false;
  availableTags: CommonDictionaryModel[] = [];
  selectedTagIds: number[] = [];
  highlightedSiteId: number | null = null;
  /** #1380 — workers ticked in the table, the target of "Tildel tags". */
  selectedWorkers: DeviceUserModel[] = [];
  /** From index-device-user (every row carries the caller's flags); the role decides while the list is empty. */
  private readonly callerFlags$ = new BehaviorSubject<CallerFlags>(NO_CALLER_FLAGS);
  get callerFlags(): CallerFlags {
    return this.callerFlags$.value;
  }
  public selectCurrentUserClaimsDeviceUsersCreate$ = this.store.select(selectCurrentUserClaimsDeviceUsersCreate);
  public selectCurrentUserClaimsDeviceUsersUpdate$ = this.store.select(selectCurrentUserClaimsDeviceUsersUpdate);
  /** "Assign tags" (button and row selection): the update claim plus worker-group rights. */
  public canAssignTags$ = combineLatest([this.selectCurrentUserClaimsDeviceUsersUpdate$, this.callerFlags$])
    .pipe(map(([canUpdate, flags]) => canUpdate && flags.canEditWorkerTags));
  // #1425 - TEMPORARY: tail-bite managers is first-user only until the owner opens it up (the API enforces it too,
  // see TailBiteSetupController). Remove together with the [FirstUserOnly] attributes there.
  public canManageTailBiteManagers$ = combineLatest([
    this.store.select(selectCurrentUserIsFirstUser),
    this.selectCurrentUserClaimsDeviceUsersUpdate$,
  ]).pipe(map(([isFirstUser, deviceUsersUpdate]) => isFirstUser && deviceUsersUpdate));
  private selectPropertyWorkersFilters$ = this.store.select(selectPropertyWorkersFilters);
  public selectPropertyWorkersNameFilters$ = this.store.select(selectPropertyWorkersNameFilters);



  ngOnInit() {
    // Resolve the page title once menus hydrate; recompute when they emit.
    // getTitleByUrl reads from the menu store internally — we drive it from
    // the menu emission instead of calling it per template change-detection.
    this.titleSub$ = this.appMenuStateService.leftAppMenus$.subscribe(() => {
      this.pageTitle = this.appMenuStateService.getTitleByUrl(this.router.url);
    });
    let propertyIds: number[] = [];
    this.getFiltersAsyncSub$ = this.selectPropertyWorkersFilters$
      .pipe(
        tap(filters => {
          if (filters.propertyIds.length !== 0 && !R.equals(propertyIds, filters.propertyIds)) {
            propertyIds = filters.propertyIds;
          }
          this.updateTable(propertyIds);
          // else {
          //   propertyIds = [];
          //   this.updateTable();
          // }
        },),
        tap(_ => {
          // if (this.showDiagram) {
          //   this.selectedPropertyId = this.taskWizardStateService.store.getValue().filters.propertyIds[0] || null;
          //   this.getPlannedTaskWorkers();
          // }
        })
      )
      .subscribe();
    // Hydrate Lokation filter from store (previously handled in
    // app-property-worker-filters; consolidated into the page).
    this.filtersInitSub$ = this.selectPropertyWorkersFilters$
      .pipe(take(1))
      .subscribe(filters => {
        this.filtersForm.get('propertyIds').patchValue(filters.propertyIds);
      });
    this.valueChangesPropertyIdsSub$ = this.filtersForm.get('propertyIds').valueChanges
      .subscribe((value: number[]) => {
        this.propertyWorkersStateService.updatePropertyIds(value);
        this.updateTable();
      });
    this.valueChangesShowResignedSub$ = this.filtersForm.get('showResigned').valueChanges
      .subscribe((value: boolean) => {
        this.propertyWorkersStateService.updateShowResigned(value);
        this.updateTable();
      });
    this.loadAllTags();
    //this.getWorkerPropertiesAssignments();
  }

  openCreateModal() {
    this.propertyWorkerCreateModalComponentAfterClosedSub$ = this.dialog.open(PropertyWorkerCreateEditModalComponent,
      {
        ...dialogConfigHelper(this.overlay, {
          deviceUser: newWorkerRow(this.callerFlags),
          assignments: [],
          availableProperties: this.availableProperties,
          availableTags: this.availableTags,
          alreadyUsedEmails: this.alreadyUsedEmails,
        }), minWidth: 1024
      })
      .afterClosed().subscribe(data => data ? this.updateTable() : undefined);
  }

  openBulkTagsModal() {
    if (this.selectedWorkers.length === 0) {
      return;
    }
    const data: PropertyWorkerBulkTagsModalData = {
      workers: this.selectedWorkers,
      availableTags: this.availableTags,
    };
    this.bulkTagsModalAfterClosedSub$ = this.dialog.open(PropertyWorkerBulkTagsModalComponent,
      dialogConfigHelper(this.overlay, data))
      .afterClosed().subscribe(saved => saved ? this.updateTable() : undefined);
  }

  openEditTagsModal() {
    this.modalSiteTags.show();
  }

  /** Halebid-ansvarlige: preselects the property when the filter holds exactly one. */
  openTailBiteManagersDialog() {
    const propertyIds = this.filtersForm.get('propertyIds').value ?? [];
    const data: TailBiteManagersDialogData = {propertyId: propertyIds.length === 1 ? propertyIds[0] : null};
    this.dialog.open(TailBiteManagersDialogComponent, dialogConfigHelper(this.overlay, data));
  }

  getPropertiesDictionary() {
    return this.propertiesService
      .getAllPropertiesDictionary();
  }

  getWorkerPropertiesAssignments(propertyIds?: number[]) {
    return this.propertiesService
      .getPropertiesAssignments(propertyIds);
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

  loadAllTags() {
    this.eFormTagService.getAvailableTags().subscribe((data) => {
      if (data && data.success) {
        this.availableTags = data.model;
      }
    });
  }

  onSearchChanged(name: string) {
    this.propertyWorkersStateService.updateNameFilter(name);
    this.updateTable();
  }

  sortTable(sort: Sort) {
    this.propertyWorkersStateService.onSortTable(sort.active);
    this.updateTable();
  }

  getDeviceUsersFiltered(propertyIds?: number[]) {
    return this.propertyWorkersStateService
      .getDeviceUsersFiltered();
  }

  private applyDeviceUsersData(data: any, isAdmin: boolean) {
    let anyIsResigned = false;
    if (data && data.model) {
      data.model.forEach(site => {
        if (!this.alreadyUsedEmails.includes(site.workerEmail)) {
          this.alreadyUsedEmails.push(site.workerEmail);
        }
        if (site.resigned) {
          anyIsResigned = true;
        }
      });
      this.showResigned = anyIsResigned;
      this.sitesDto = data.model.map(site => ({
        ...site,
        propertyNames: Array.isArray(site.propertyNames)
          ? site.propertyNames
          : (typeof site.propertyNames === 'string' && site.propertyNames.length > 0
            ? site.propertyNames.split(',').map((name: string) => name.trim())
            : [])
      }));
      this.callerFlags$.next(readCallerFlags(data.model, isAdmin));
    }
  }



  updateTable(propertyIds?: number[]) {
    this.getSites$ = forkJoin([
      this.getPropertiesDictionary(),
      this.getDeviceUsersFiltered(propertyIds),
      this.getWorkerPropertiesAssignments(propertyIds),
      this.store.select(selectCurrentUserIsAdmin).pipe(take(1)),
    ]).subscribe(([propertiesResult, devicesResult, assignmentsResult, isAdmin]) => {
      if (propertiesResult && propertiesResult.success) {
        this.availableProperties = propertiesResult.model;
      }
      this.applyDeviceUsersData(devicesResult, isAdmin);
      if (assignmentsResult && assignmentsResult.success) {
        this.workersAssignments = [...assignmentsResult.model];
      }
    });
  }

  updateTableWithHighlight(siteId: number) {
    this.highlightedSiteId = siteId;
    this.updateTable();
  }

  /**
   * Called by the table component when it has finished rendering
   * the highlighted row in the DOM. This guarantees both the service
   * calls and the mtx-grid rendering are complete before we clean up.
   */
  onHighlightedRowRendered(): void {
    setTimeout(() => {
      this.highlightedSiteId = null;
    }, 3500);
  }

  ngOnDestroy(): void {
  }

  onTagsChanged($event: number[]) {
    this.selectedTagIds = $event;
    this.propertyWorkersStateService.updateTagIds($event);
    this.updateTable();
  }

  private escapeCsvValue(value: any): string {
    if (value === null || value === undefined) {
      return '';
    }

    const stringValue = String(value);

    if (stringValue.includes(';') || stringValue.includes('"') || stringValue.includes('\n')) {
      return `"${stringValue.replace(/"/g, '""')}"`;
    }

    return stringValue;
  }

  private formatDate(date: Date | string): string {
    const d = new Date(date);

    const day = ('0' + d.getDate()).slice(-2);
    const month = ('0' + (d.getMonth() + 1)).slice(-2);
    const year = d.getFullYear();

    return `${day}-${month}-${year}`;
  }


  exportCsv(): void {

    if (!this.sitesDto || this.sitesDto.length === 0) {
      return;
    }

    const headers = [
      'Id',
      'Employee No',
      'Property',
      'Name',
      'Email',
      'Phone number',
      'Task management',
      'Time registration',
      'Web',
      'Archive',
      'Language'
    ];

    const rows = this.sitesDto.map(site => {

      const propertyNames = Array.isArray(site.propertyNames)
        ? site.propertyNames.join(', ')
        : site.propertyNames ?? '';

      const name = `${site.userFirstName ?? ''} ${site.userLastName ?? ''}`.trim();

      return [
        site.siteId,
        site.employeeNo ?? '',
        propertyNames,
        name,
        site.workerEmail ?? '',
        site.phoneNumber ?? '',
        site.taskManagementEnabled ? 'Yes' : 'No',
        site.timeRegistrationEnabled ? 'Yes' : 'No',
        site.webAccessEnabled ? 'Yes' : 'No',
        site.archiveEnabled ? 'Yes' : 'No',
        site.language ?? ''
      ];
    });

    const csvContent =
      headers.join(';') +
      '\n' +
      rows.map(r => r.map(v => this.escapeCsvValue(v)).join(';')).join('\n');

    const blob = new Blob([csvContent], {
      type: 'text/csv;charset=utf-8;'
    });

    const fileName = `property_workers_${this.formatDate(new Date())}.csv`;

    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = fileName;
    link.click();

    URL.revokeObjectURL(link.href);
  }

}
