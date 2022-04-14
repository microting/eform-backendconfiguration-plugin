import {
  Component,
  EventEmitter,
  OnDestroy,
  OnInit,
  Output,
} from '@angular/core';
import {
  TaskManagementStateService,
} from '../store';
import {FormControl, FormGroup} from '@angular/forms';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {Subscription} from 'rxjs';
import {BackendConfigurationPnPropertiesService} from '../../../../services';
import {
  PropertyAreaModel,
  PropertyAssignWorkersModel,
} from '../../../../models';
import { CommonDictionaryModel } from 'src/app/common/models';
import {SitesService} from 'src/app/common/services';

@AutoUnsubscribe()
@Component({
  selector: 'app-task-management-filters',
  templateUrl: './task-management-filters.component.html',
  styleUrls: ['./task-management-filters.component.scss'],
})
export class TaskManagementFiltersComponent implements OnInit, OnDestroy {
  @Output() updateTable: EventEmitter<void> = new EventEmitter<void>();
  filtersForm: FormGroup;
  propertyAreas: PropertyAreaModel[] = [];
  properties: CommonDictionaryModel[];
  assignedSitesToProperty: CommonDictionaryModel[];

  selectFiltersSub$: Subscription;
  propertyIdValueChangesSub$: Subscription;
  areaIdValueChangesSub$: Subscription;

  constructor(
    public taskManagementStateService: TaskManagementStateService,
    private propertyService: BackendConfigurationPnPropertiesService,
    private sitesService: SitesService,
  ) {}

  ngOnInit(): void {
    this.getProperties();
    this.selectFiltersSub$ = this.taskManagementStateService.getFiltersAsync().subscribe(filters => {
      if(!this.filtersForm) {
        this.filtersForm = new FormGroup({
          propertyId: new FormControl(filters.propertyId),
          areaId: new FormControl({
            value: filters.areaId,
            disabled: !filters.propertyId,
          }),
          createdBy: new FormControl({
            value: filters.createdBy,
            disabled: !filters.propertyId && !filters.areaId,
          }),
          lastAssignedTo: new FormControl({
            value: filters.lastAssignedTo,
            disabled: !filters.propertyId && !filters.areaId,
          }),
          status: new FormControl({
            value: filters.status,
            disabled: !filters.propertyId && !filters.areaId,
          }),
          date: new FormControl({
            value: filters.date,
            disabled: !filters.propertyId && !filters.areaId,
          }),
        });
        if (filters.propertyId) {
          this.getPropertyAreas(filters.propertyId);
          this.getSites(filters.propertyId);
        }
      }
    })
    this.propertyIdValueChangesSub$ = this.filtersForm.get('propertyId')
      .valueChanges.subscribe((value: number) => {
        if(this.taskManagementStateService.store.getValue().filters.propertyId !== value) {
          this.getPropertyAreas(value);
          this.getSites(value);
          this.taskManagementStateService.store.update((state) => ({
            filters: {
              ...state.filters,
              propertyId: value,
              areaId: undefined,
              date: undefined,
              status: undefined,
              createdBy: undefined,
              lastAssignedTo: undefined,
            },
          }))
          this.filtersForm.get('areaId').enable();
          this.filtersForm.get('areaId').setValue(undefined);
          this.filtersForm.get('createdBy').setValue(undefined);
          this.filtersForm.get('lastAssignedTo').setValue(undefined);
          this.filtersForm.get('status').setValue(undefined);
          this.filtersForm.get('date').setValue(undefined);
          this.filtersForm.get('createdBy').disable();
          this.filtersForm.get('lastAssignedTo').disable();
          this.filtersForm.get('status').disable();
          this.filtersForm.get('date').disable();
        }
    });
    this.areaIdValueChangesSub$ = this.filtersForm.get('areaId')
      .valueChanges.subscribe((value: number) => {
        if(this.taskManagementStateService.store.getValue().filters.areaId !== value) {
          this.taskManagementStateService.store.update((state) => ({
            filters: {
              ...state.filters,
              areaId: value,
            },
          }));
        }
        if(value) {
          this.filtersForm.get('createdBy').enable();
          this.filtersForm.get('lastAssignedTo').enable();
          this.filtersForm.get('status').enable();
          this.filtersForm.get('date').enable();
        } else {
          this.filtersForm.get('createdBy').disable();
          this.filtersForm.get('lastAssignedTo').disable();
          this.filtersForm.get('status').disable();
          this.filtersForm.get('date').disable();
        }
    });

    this.filtersForm.get('createdBy').valueChanges.subscribe((value: number) => {
      if(this.taskManagementStateService.store.getValue().filters.createdBy !== value) {
        this.taskManagementStateService.store.update((state) => ({
          filters: {
            ...state.filters,
            createdBy: value,
          },
        }));
      }
    });
    this.filtersForm.get('lastAssignedTo').valueChanges.subscribe((value: number) => {
      if(this.taskManagementStateService.store.getValue().filters.lastAssignedTo !== value) {
        this.taskManagementStateService.store.update((state) => ({
          filters: {
            ...state.filters,
            lastAssignedTo: value,
          },
        }));
      }
    });
    this.filtersForm.get('status').valueChanges.subscribe((value: number) => {
      if(this.taskManagementStateService.store.getValue().filters.status !== value) {
        this.taskManagementStateService.store.update((state) => ({
          filters: {
            ...state.filters,
            status: value,
          },
        }));
      }
    });
    this.filtersForm.get('date').valueChanges.subscribe((value: string | Date) => {
      if(this.taskManagementStateService.store.getValue().filters.date !== value) {
        this.taskManagementStateService.store.update((state) => ({
          filters: {
            ...state.filters,
            date: value,
          },
        }));
      }
    });
  }

  getPropertyAreas(propertyId: number) {
    this.propertyService.getPropertyAreas(propertyId)
      .subscribe((data) => {
        if(data && data.success && data.model) {
          this.propertyAreas = data.model.filter(x => x.activated);
        }
      })
  }

  getProperties() {
    this.propertyService.getAllPropertiesDictionary()
      .subscribe(data => {
        if(data && data.success && data.model){
          this.properties = data.model;
        }
      })
  }

  getSites(propertyId: number) {
    this.sitesService.getAllSitesDictionary()
      .subscribe(result => {
        if(result && result.success && result.success){
          const sites = result.model;
          this.propertyService.getPropertiesAssignments()
            .subscribe(data => {
              if(data && data.success && data.model){
                data.model.forEach(x => x.assignments = x.assignments.filter(x => x.isChecked && x.propertyId === propertyId));
                data.model = data.model.filter(x => x.assignments.length > 0);
                this.assignedSitesToProperty = data.model.map((x) => {
                  return {
                    id: x.siteId,
                    name: sites.find(y => y.id === x.siteId).name,
                    description: '',
                  }});
              }
            })
        }
      })
  }

  ngOnDestroy(): void {
  }

}
