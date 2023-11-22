import {
  Component,
  EventEmitter, Input,
  OnDestroy,
  OnInit,
  Output,
} from '@angular/core';
import {FormControl, FormGroup} from '@angular/forms';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {Subscription, take} from 'rxjs';
import {
  BackendConfigurationPnPropertiesService,
  BackendConfigurationPnTaskManagementService,
} from '../../../../services';
import {CommonDictionaryModel, EntityItemModel, Paged} from 'src/app/common/models';
import {EntitySelectService, SitesService} from 'src/app/common/services';
import {format, parse, set} from 'date-fns';
import {TranslateService} from '@ngx-translate/core';
import {PARSING_DATE_FORMAT} from 'src/app/common/const';
import {PropertyModel} from 'src/app/plugins/modules/backend-configuration-pn/models';
import {MatDialog, MatDialogRef} from '@angular/material/dialog';
import {AreaRuleEntityListModalComponent} from 'src/app/plugins/modules/backend-configuration-pn/components';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {Overlay} from '@angular/cdk/overlay';
import {Store} from '@ngrx/store';
import {
  selectTaskManagementFilters,
} from '../../../../state/task-management/task-management.selector';

@AutoUnsubscribe()
@Component({
  selector: 'app-task-management-filters',
  templateUrl: './task-management-filters.component.html',
  styleUrls: ['./task-management-filters.component.scss'],
})
export class TaskManagementFiltersComponent implements OnInit, OnDestroy {
  @Output()
  updateTable: EventEmitter<void> = new EventEmitter<void>();
  @Output()
  showEditEntityListModal: EventEmitter<PropertyModel> = new EventEmitter<PropertyModel>();
  filtersForm: FormGroup;
  propertyAreas: string[] = [];
  properties: CommonDictionaryModel[] = [];
  propertiesModel: Paged<PropertyModel> = new Paged<PropertyModel>();
  assignedSitesToProperty: CommonDictionaryModel[] = [];
  propertyUpdateSub$: Subscription;
  getEntitySelectableGroupSub$: Subscription;
  updateEntitySelectableGroupSub$: Subscription;

  selectFiltersSub$: Subscription;
  propertyIdValueChangesSub$: Subscription;
  areaNameValueChangesSub$: Subscription;
  public selectTaskManagementFilters$ = this.store.select(selectTaskManagementFilters);

  constructor(
    private store: Store,
    private translate: TranslateService,
    private propertyService: BackendConfigurationPnPropertiesService,
    private sitesService: SitesService,
    public dialog: MatDialog,
    private entitySelectService: EntitySelectService,
    private overlay: Overlay,
    private taskManagementService: BackendConfigurationPnTaskManagementService,
  ) {
  }

  ngOnInit(): void {
    this.getProperties();
    this.selectFiltersSub$ = this.selectTaskManagementFilters$
      .pipe(take(1)) // get only one time
      .subscribe((filters) => {
        if (!this.filtersForm) {
          this.filtersForm = new FormGroup({
            propertyId: new FormControl(filters.propertyId),
            areaName: new FormControl({
              value: filters.areaName,
              disabled: !filters.propertyId || filters.propertyId === -1,
            }),
            createdBy: new FormControl({
              value: filters.createdBy,
              disabled: !filters.propertyId || filters.propertyId === -1,
            }),
            lastAssignedTo: new FormControl({
              value: filters.lastAssignedTo,
              disabled: !filters.propertyId || filters.propertyId === -1,
            }),
            status: new FormControl({
              value: filters.status,
              disabled: !filters.propertyId,
            }),
            date: new FormGroup({
              dateFrom: new FormControl({
                value: parse(filters.dateFrom as string, PARSING_DATE_FORMAT, new Date),
                disabled: !filters.propertyId,
              }),
              dateTo: new FormControl({
                value: parse(filters.dateTo as string, PARSING_DATE_FORMAT, new Date),
                disabled: !filters.propertyId,
              }),
            }),
            priority: new FormControl({
              value: filters.priority,
              disabled: false,
            }),
          });
          if (filters.propertyId && filters.propertyId !== -1) {
            this.getPropertyAreas(filters.propertyId);
            this.getSites(filters.propertyId);
          }
        }
      });
    this.propertyIdValueChangesSub$ = this.filtersForm
      .get('propertyId')
      .valueChanges.subscribe((value: number) => {
        let currentFilters: any;
        this.selectTaskManagementFilters$.subscribe((filters) => {
          currentFilters = filters;
        }).unsubscribe();
        if (
          currentFilters
            .propertyId !== value
        ) {
          if(value !== -1) {
            this.getPropertyAreas(value);
            this.getSites(value);
          }
          this.store.dispatch({
            type: '[TaskManagement] Update filters',
            payload: {
              filters: {
                ...currentFilters,
                propertyId: value,
                areaName: null,
                dateFrom: null,
                dateTo: null,
                status: null,
                createdBy: null,
                lastAssignedTo: null,
              }
            },
          });
          this.filtersForm
            .get('areaName')
            .setValue(null, {emitEvent: false});
          this.filtersForm
            .get('createdBy')
            .setValue(null, {emitEvent: false});
          this.filtersForm
            .get('lastAssignedTo')
            .setValue(null, {emitEvent: false});
          this.filtersForm
            .get('status')
            .setValue(null, {emitEvent: false});
          this.filtersForm.get('date.dateFrom').setValue(null, {emitEvent: false});
          this.filtersForm.get('date.dateTo').setValue(null, {emitEvent: false});
          if(value !== -1) {
            this.filtersForm.get('areaName').enable({emitEvent: false});
          } else {
            this.filtersForm.get('areaName').disable({emitEvent: false});
          }
          if(value !== -1) {
            this.filtersForm.get('createdBy').enable({emitEvent: false});
          } else {
            this.filtersForm.get('createdBy').disable({emitEvent: false});}
          if(value !== -1) {
            this.filtersForm.get('lastAssignedTo').enable({emitEvent: false});
          } else {
            this.filtersForm.get('lastAssignedTo').disable({emitEvent: false});}
          this.filtersForm.get('status').enable({emitEvent: false});
          this.filtersForm.get('date').enable({emitEvent: false});
        }
      });
    this.areaNameValueChangesSub$ = this.filtersForm
      .get('areaName')
      .valueChanges.subscribe((value: string) => {
        let currentFilters: any;
        this.selectTaskManagementFilters$.subscribe((filters) => {
          currentFilters = filters;
        }).unsubscribe();
        if (
          currentFilters.areaName !==
          value
        ) {
          this.store.dispatch({
            type: '[TaskManagement] Update filters',
            payload: {
              filters: {
                ...currentFilters,
                areaName: value,
              }
            },
          });
        }
      });

    this.filtersForm
      .get('createdBy')
      .valueChanges.subscribe((value: string) => {
      let currentFilters: any;
      this.selectTaskManagementFilters$.subscribe((filters) => {
        currentFilters = filters;
      }).unsubscribe();
      this.store.dispatch({
        type: '[TaskManagement] Update filters',
        payload: {
          filters: {
            ...currentFilters,
            createdBy: value,
          }
        },
      });
    });
    this.filtersForm
      .get('lastAssignedTo')
      .valueChanges.subscribe((value: number) => {
      let currentFilters: any;
      this.selectTaskManagementFilters$.subscribe((filters) => {
        currentFilters = filters;
      }).unsubscribe();
        this.store.dispatch(
          {
            type: '[TaskManagement] Update filters',
            payload: {
              filters: {
                ...currentFilters,
                lastAssignedTo: value,
              }
            },
          });
    });
    this.filtersForm.get('status').valueChanges.subscribe((value: number) => {
      let currentFilters: any;
      this.selectTaskManagementFilters$.subscribe((filters) => {
        currentFilters = filters;
      }).unsubscribe();
      this.store.dispatch(
        {
          type: '[TaskManagement] Update filters',
          payload: {
            filters: {
              ...currentFilters,
              status: value,
            }
          },
        });
    });
    this.filtersForm.get('date.dateFrom').valueChanges.subscribe((value: Date) => {
      // @ts-ignore
      if (!isNaN(value) && value) { // invalid date - it's NaN.
        const dateFrom = set(value, {
          hours: 0,
          minutes: 0,
          seconds: 0,
          milliseconds: 0,
        });
        let currentFilters: any;
        this.selectTaskManagementFilters$.subscribe((filters) => {
          currentFilters = filters;
        }).unsubscribe();
        this.store.dispatch(
          {
            type: '[TaskManagement] Update filters',
            payload: {
              filters: {
                ...currentFilters,
                dateFrom: format(dateFrom, PARSING_DATE_FORMAT),
              }
            },
          });
      } else {
        let currentFilters: any;
        this.selectTaskManagementFilters$.subscribe((filters) => {
          currentFilters = filters;
        }).unsubscribe();
        this.store.dispatch(
          {
            type: '[TaskManagement] Update filters',
            payload: {
              filters: {
                ...currentFilters,
                dateFrom: null,
              }
            },
          });
      }
    });
    this.filtersForm.get('date.dateTo').valueChanges.subscribe((value: Date) => {
      // @ts-ignore
      if (!isNaN(value) && value) { // invalid date - it's NaN.
        const dateTo = set(value, {
          hours: 0,
          minutes: 0,
          seconds: 0,
          milliseconds: 0,
        });
        let currentFilters: any;
        this.selectTaskManagementFilters$.subscribe((filters) => {
          currentFilters = filters;
        }).unsubscribe();
        this.store.dispatch(
          {
            type: '[TaskManagement] Update filters',
            payload: {
              filters: {
                ...currentFilters,
                dateTo: format(dateTo, PARSING_DATE_FORMAT),
              }
            },
          });
      } else {
        let currentFilters: any;
        this.selectTaskManagementFilters$.subscribe((filters) => {
          currentFilters = filters;
        }).unsubscribe();
        this.store.dispatch(
          {
            type: '[TaskManagement] Update filters',
            payload: {
              filters: {
                ...currentFilters,
                dateTo: null,
              }
            },
          });
      }
    });
    this.filtersForm.get('priority').valueChanges.subscribe((value: number) => {
        let currentFilters: any;
        this.selectTaskManagementFilters$.subscribe((filters) => {
          currentFilters = filters;
        }).unsubscribe();
      this.store.dispatch(
        {
          type: '[TaskManagement] Update filters',
          payload: {
            filters: {
              ...currentFilters,
              priority: value,
            }
          },
        });
    }
    );
  }

  getPropertyAreas(propertyId: number) {
    // get entity items
    this.taskManagementService
      .getEntityItemsListByPropertyId(propertyId)
      .subscribe((data) => {
        if (data && data.success && data.model) {
          this.propertyAreas = data.model;
        }
      });
  }

  getProperties() {
    this.propertyService.getAllProperties({
      nameFilter: '',
      sort: 'Id',
      isSortDsc: true,
      pageSize: 100000,
      offset: 0,
      pageIndex: 0
    }).subscribe((data) => {
      if (data && data.success && data.model) {
        this.propertiesModel = data.model;
        this.properties = [{id: -1, name: this.translate.instant('All'), description: ''}, ...data.model.entities.filter((x) => x.workorderEnable)
          .map((x) => {
            return {name: `${x.name}`, description: '', id: x.id};
          })];
      }
    });
  }

  getSites(propertyId: number) {
    this.sitesService.getAllSitesDictionary().subscribe((result) => {
      if (result && result.success && result.success) {
        const sites = result.model;
        this.propertyService.getPropertiesAssignments().subscribe((data) => {
          if (data && data.success && data.model) {
            data.model.forEach(
              (x) =>
                (x.assignments = x.assignments.filter(
                  (x) => x.isChecked && x.propertyId === propertyId
                ))
            );
            data.model = data.model.filter((x) => x.assignments.length > 0 && x.taskManagementEnabled);
            this.assignedSitesToProperty = data.model.map((x) => {
              const site = sites.find((y) => y.id === x.siteId);
              return {
                id: x.siteId,
                name: site ? site.name : '',
                description: '',
              };
            });
            this.assignedSitesToProperty = this.assignedSitesToProperty.sort((a, b) => {
              if (a.name > b.name) {
                return 1;
              }
              if (a.name < b.name) {
                return -1;
              }
              return 0;
            });
          }
        });
      }
    });
  }

  ngOnDestroy(): void {
  }

  onShowEditEntityListModal() {
    const propertyId = this.filtersForm
      .get('propertyId').value;
    if (propertyId !== -1) {
      const propertyModel = this.propertiesModel.entities.find(x => x.id === propertyId);
      this.ShowEditEntityListModal(propertyModel);
      // this.showEditEntityListModal.emit(propertyModel);
    }
  }

  ShowEditEntityListModal(propertyModel: PropertyModel) {
    const modal = this.dialog
      .open(AreaRuleEntityListModalComponent, {...dialogConfigHelper(this.overlay, propertyModel.workorderEntityListId)});
    this.propertyUpdateSub$ = modal.componentInstance.entityListChanged.subscribe(x => this.updateEntityList(propertyModel, x, modal));
  }

  updateEntityList(propertyModel: PropertyModel, model: Array<EntityItemModel>, modal: MatDialogRef<AreaRuleEntityListModalComponent>) {
    if(propertyModel.workorderEntityListId) {
      this.getEntitySelectableGroupSub$ = this.entitySelectService.getEntitySelectableGroup(propertyModel.workorderEntityListId)
        .subscribe(data => {
          if (data.success) {
            this.updateEntitySelectableGroupSub$ = this.entitySelectService.updateEntitySelectableGroup({
              entityItemModels: model,
              groupUid: +data.model.microtingUUID,
              ...data.model
            }).subscribe(x => {
              if (x.success) {
                modal.close();
                this.getPropertyAreas(propertyModel.id);
              }
            });
          }
        });
    }
  }
}
