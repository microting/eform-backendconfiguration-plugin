import {Component, EventEmitter, Input, OnDestroy, OnInit, Output} from '@angular/core';
import {CommonDictionaryModel} from 'src/app/common/models';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {FormControl, FormGroup} from '@angular/forms';
import {DeviceUserModel} from 'src/app/plugins/modules/backend-configuration-pn/models';
import {interval, Subscription} from 'rxjs';
import {
  PropertyWorkersStateService
} from 'src/app/plugins/modules/backend-configuration-pn/modules/property-workers/components/store';
import {debounce, filter, tap} from 'rxjs/operators';
import * as R from 'ramda';
import {Store} from '@ngrx/store';
import {
  selectPropertyWorkersFilters
} from "src/app/plugins/modules/backend-configuration-pn/state/property-workers/property-workers.selector";

@AutoUnsubscribe()
@Component({
  selector: 'app-property-worker-filters',
  templateUrl: './property-worker-filters.component.html',
  styleUrls: ['./property-worker-filters.component.scss'],
})
export class PropertyWorkerFiltersComponent implements OnInit, OnDestroy  {
  @Output() updateTable: EventEmitter<void> = new EventEmitter<void>();
  @Input() properties: CommonDictionaryModel[] = [];
  //@Input() availableSites: Array<DeviceUserModel>;


  filtersForm: FormGroup<{
    propertyIds: FormControl<number[]>,
    //workerIds: FormControl<number[]>,
  }>;
  getFiltersAsyncSub$: Subscription;
  valueChangesPropertyIdsSub$: Subscription;
  private selectPropertyWorkersFilters$ = this.store.select(selectPropertyWorkersFilters);

  constructor(
    private store: Store,
    public propertyWorkersStateService: PropertyWorkersStateService,
  ) {
    this.filtersForm = new FormGroup({
      propertyIds: new FormControl([]),
      //workerIds: new FormControl([]),
    });
  }

  ngOnInit(): void {
    this.getFiltersAsyncSub$ = this.selectPropertyWorkersFilters$
      .pipe(
        debounce(x => interval(200)),
        filter(value => !R.equals(value, this.filtersForm.getRawValue())),
        tap(filters => {
          this.propertyIdsChange(filters.propertyIds);
          // this.propertyIdsChange(filters.propertyIds);
          // this.folderIdsChange(filters.folderIds);
          // this.tagIdsChange(filters.tagIds);
          // this.statusChange(filters.status);
          // this.assignToIdsChange(filters.assignToIds);
        })).subscribe();
    // this.valueChangesPropertyIdsSub$ = this.filtersForm.get('propertyIds').valueChanges.pipe(
    //   debounce(x => interval(200)),
    //   filter(value => !R.equals(value, this.propertyWorkersStateService.store.getValue().filters.propertyIds)),
    //   tap(value => {
    //     this.propertyIdsChange(value);
    //   }),
    //   tap(() => this.updateTable.emit())
    // ).subscribe();
  }

  propertyIdsChange(value: number[]) {
    this.propertyWorkersStateService.updatePropertyIds(value);
    this.filtersForm.get('propertyIds').patchValue(value);
  }

  ngOnDestroy(): void {
  }
}
