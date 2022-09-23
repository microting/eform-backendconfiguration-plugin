import {
  Component,
  EventEmitter,
  OnDestroy,
  OnInit,
  Output,
} from '@angular/core';
// import {TaskManagementStateService} from '../store';
import {FormControl, FormGroup} from '@angular/forms';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {Subscription} from 'rxjs';
import {
  BackendConfigurationPnPropertiesService,
  BackendConfigurationPnTaskManagementService,
} from '../../../../services';
import {CommonDictionaryModel} from 'src/app/common/models';
import {SitesService} from 'src/app/common/services';
import {format, set} from 'date-fns';
import {TranslateService} from '@ngx-translate/core';
import {DateTimeAdapter} from '@danielmoncada/angular-datetime-picker';
import {AuthStateService} from 'src/app/common/store';

@AutoUnsubscribe()
@Component({
  selector: 'app-documents-filters',
  templateUrl: './documents-filters.component.html',
  styleUrls: ['./documents-filters.component.scss'],
})
export class DocumentsFiltersComponent implements OnInit, OnDestroy {
  @Output() updateTable: EventEmitter<void> = new EventEmitter<void>();
  private standartDateTimeFormat = `yyyy-MM-dd'T'HH:mm:ss.SSS'Z'`;
  filtersForm: FormGroup;
  propertyAreas: string[] = [];
  documentNames: string[] = [];
  folders: CommonDictionaryModel[] = [];
  properties: CommonDictionaryModel[] = [];
  assignedSitesToProperty: CommonDictionaryModel[] = [];
  expirationTimes: CommonDictionaryModel[] = [
    {id: 1, name: 'Udløber om mindre end 1 måned', description: 'sfsdf'},
    {id: 1, name: 'Udløber om 1-3 måneder', description: 'sfsdf'},
    {id: 1, name: 'Udløber om 4-6 måneder', description: 'sfsdf'},
    {id: 1, name: 'Udløber om 6-12 måneder', description: 'sfsdf'},
    {id: 1, name: 'Udløber om over 12 måneder', description: 'sfsdf'},
  ];

  selectFiltersSub$: Subscription;
  propertyIdValueChangesSub$: Subscription;
  areaNameValueChangesSub$: Subscription;

  constructor(
    dateTimeAdapter: DateTimeAdapter<any>,
    private translate: TranslateService,
    // public taskManagementStateService: TaskManagementStateService,
    private propertyService: BackendConfigurationPnPropertiesService,
    // private sitesService: SitesService,
    // private taskManagementService: BackendConfigurationPnTaskManagementService,
    authStateService: AuthStateService
  ) {
    dateTimeAdapter.setLocale(authStateService.currentUserLocale);
  }

  ngOnInit(): void {
    this.getProperties();
    // this.selectFiltersSub$ = this.taskManagementStateService
    //   .getFiltersAsync()
    //   .subscribe((filters) => {
    //     if (!this.filtersForm) {
    //       this.filtersForm = new FormGroup({
    //         propertyId: new FormControl(filters.propertyId),
    //         areaName: new FormControl({
    //           value: filters.areaName,
    //           disabled: !filters.propertyId || filters.propertyId === -1,
    //         }),
    //         createdBy: new FormControl({
    //           value: filters.createdBy,
    //           disabled: !filters.propertyId || filters.propertyId === -1,
    //         }),
    //         lastAssignedTo: new FormControl({
    //           value: filters.lastAssignedTo,
    //           disabled: !filters.propertyId || filters.propertyId === -1,
    //         }),
    //         status: new FormControl({
    //           value: filters.status,
    //           disabled: !filters.propertyId,
    //         }),
    //         date: new FormControl({
    //           value: [filters.dateFrom, filters.dateTo],
    //           disabled: !filters.propertyId,
    //         }),
    //       });
    //       if (filters.propertyId && filters.propertyId !== -1) {
    //         this.getPropertyAreas(filters.propertyId);
    //         this.getSites(filters.propertyId);
    //       }
    //     }
    //   });
    // this.propertyIdValueChangesSub$ = this.filtersForm
    //   .get('propertyId')
    //   .valueChanges.subscribe((value: number) => {
    //     if (
    //       this.taskManagementStateService.store.getValue().filters
    //         .propertyId !== value
    //     ) {
    //       if(value !== -1) {
    //         this.getPropertyAreas(value);
    //         this.getSites(value);
    //       }
    //       this.taskManagementStateService.store.update((state) => ({
    //         filters: {
    //           ...state.filters,
    //           propertyId: value,
    //           areaName: null,
    //           dateFrom: null,
    //           dateTo: null,
    //           status: null,
    //           createdBy: null,
    //           lastAssignedTo: null,
    //         },
    //       }));
    //       this.filtersForm
    //         .get('areaName')
    //         .setValue(undefined, {emitEvent: false});
    //       this.filtersForm
    //         .get('createdBy')
    //         .setValue(undefined, {emitEvent: false});
    //       this.filtersForm
    //         .get('lastAssignedTo')
    //         .setValue(undefined, {emitEvent: false});
    //       this.filtersForm
    //         .get('status')
    //         .setValue(undefined, {emitEvent: false});
    //       this.filtersForm.get('date').setValue([], {emitEvent: false});
    //       if(value !== -1) {
    //         this.filtersForm.get('areaName').enable({emitEvent: false});
    //       } else {
    //         this.filtersForm.get('areaName').disable({emitEvent: false});
    //       }
    //       if(value !== -1) {
    //         this.filtersForm.get('createdBy').enable({emitEvent: false});
    //       } else {
    //         this.filtersForm.get('createdBy').disable({emitEvent: false});}
    //       if(value !== -1) {
    //         this.filtersForm.get('lastAssignedTo').enable({emitEvent: false});
    //       } else {
    //         this.filtersForm.get('lastAssignedTo').disable({emitEvent: false});}
    //       this.filtersForm.get('status').enable({emitEvent: false});
    //       this.filtersForm.get('date').enable({emitEvent: false});
    //     }
    //   });
    // this.areaNameValueChangesSub$ = this.filtersForm
    //   .get('areaName')
    //   .valueChanges.subscribe((value: string) => {
    //     if (
    //       this.taskManagementStateService.store.getValue().filters.areaName !==
    //       value
    //     ) {
    //       this.taskManagementStateService.store.update((state) => ({
    //         filters: {
    //           ...state.filters,
    //           areaName: value,
    //         },
    //       }));
    //     }
    //     /*if (value) {
    //       this.filtersForm.get('createdBy').enable({ emitEvent: false });
    //       this.filtersForm.get('lastAssignedTo').enable({ emitEvent: false });
    //       this.filtersForm.get('status').enable({ emitEvent: false });
    //       this.filtersForm.get('date').enable({ emitEvent: false });
    //     } else {
    //       this.filtersForm.get('createdBy').disable({ emitEvent: false });
    //       this.filtersForm.get('lastAssignedTo').disable({ emitEvent: false });
    //       this.filtersForm.get('status').disable({ emitEvent: false });
    //       this.filtersForm.get('date').disable({ emitEvent: false });
    //     }*/
    //   });
    //
    // this.filtersForm
    //   .get('createdBy')
    //   .valueChanges.subscribe((value: string) => {
    //   this.taskManagementStateService.store.update((state) => ({
    //     filters: {
    //       ...state.filters,
    //       createdBy: value,
    //     },
    //   }));
    // });
    // this.filtersForm
    //   .get('lastAssignedTo')
    //   .valueChanges.subscribe((value: string) => {
    //   this.taskManagementStateService.store.update((state) => ({
    //     filters: {
    //       ...state.filters,
    //       lastAssignedTo: value,
    //     },
    //   }));
    // });
    // this.filtersForm.get('status').valueChanges.subscribe((value: number) => {
    //   this.taskManagementStateService.store.update((state) => ({
    //     filters: {
    //       ...state.filters,
    //       status: value,
    //     },
    //   }));
    // });
    // this.filtersForm.get('date').valueChanges.subscribe((value: any[]) => {
    //   if (value && value[0] && value[1]) {
    //     let dateFrom = new Date(value[0]._d);
    //     let dateTo = new Date(value[1]._d);
    //     dateFrom = set(dateFrom, {
    //       hours: 0,
    //       minutes: 0,
    //       seconds: 0,
    //       milliseconds: 0,
    //     });
    //     dateTo = set(dateTo, {
    //       hours: 0,
    //       minutes: 0,
    //       seconds: 0,
    //       milliseconds: 0,
    //     });
    //     this.taskManagementStateService.store.update((state) => ({
    //       filters: {
    //         ...state.filters,
    //         dateFrom: format(dateFrom, this.standartDateTimeFormat),
    //         dateTo: format(dateTo, this.standartDateTimeFormat),
    //       },
    //     }));
    //   } else {
    //     this.taskManagementStateService.store.update((state) => ({
    //       filters: {
    //         ...state.filters,
    //         dateFrom: null,
    //         dateTo: null,
    //       },
    //     }));
    //   }
    // });
  }


  getProperties() {
    this.propertyService.getAllProperties({
      nameFilter: '',
      sort: 'Id',
      isSortDsc: false,
      pageSize: 100000,
      offset: 0,
      pageIndex: 0
    }).subscribe((data) => {
      if (data && data.success && data.model) {
        this.properties = [{id: -1, name: this.translate.instant('All'), description: ''}, ...data.model.entities
          .map((x) => {
            return {name: `${x.cvr ? x.cvr : ''} - ${x.chr ? x.chr : ''} - ${x.name}`, description: '', id: x.id};
          })];
      }
    });
  }

  ngOnDestroy(): void {
  }
}
