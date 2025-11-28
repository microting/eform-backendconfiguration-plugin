import {Component, EventEmitter, Input, OnDestroy, OnInit, Output,
  inject
} from '@angular/core';
import {CommonDictionaryModel} from 'src/app/common/models';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {FormControl, FormGroup} from '@angular/forms';
import {Subscription, take, tap} from 'rxjs';
import {
  PropertyWorkersStateService
} from '../store';
import {Store} from '@ngrx/store';
import {
  selectPropertyWorkersFilters,
  propertyWorkersUpdateFilters
} from '../../../../state';

@AutoUnsubscribe()
@Component({
    selector: 'app-property-worker-filters',
    templateUrl: './property-worker-filters.component.html',
    styleUrls: ['./property-worker-filters.component.scss'],
    standalone: false
})
export class PropertyWorkerFiltersComponent implements OnInit, OnDestroy  {
  private store = inject(Store);
  public propertyWorkersStateService = inject(PropertyWorkersStateService);

  @Output() updateTable: EventEmitter<void> = new EventEmitter<void>();
  @Input() properties: CommonDictionaryModel[] = [];
  //@Input() availableSites: Array<DeviceUserModel>;


  filtersForm: FormGroup<{
    propertyIds: FormControl<number[]>,
    showResigned: FormControl<boolean>,
    //workerIds: FormControl<number[]>,
  }>;
  getFiltersAsyncSub$: Subscription;
  valueChangesPropertyIdsSub$: Subscription;
  valueChangesShowResignedSub$: Subscription;
  private selectPropertyWorkersFilters$ = this.store.select(selectPropertyWorkersFilters);

  
  constructor() {
    this.filtersForm = new FormGroup({
      propertyIds: new FormControl([]),
      showResigned: new FormControl(false),
      //workerIds: new FormControl([]),
    });
  }


  ngOnInit(): void {
    this.getFiltersAsyncSub$ = this.selectPropertyWorkersFilters$
      .pipe(
        take(1),
        tap(filters => {
          this.filtersForm.get('propertyIds').patchValue(filters.propertyIds);
          // this.propertyIdsChange(filters.propertyIds);
          // this.folderIdsChange(filters.folderIds);
          // this.tagIdsChange(filters.tagIds);
          // this.statusChange(filters.status);
          // this.assignToIdsChange(filters.assignToIds);
        })).subscribe();
    this.valueChangesPropertyIdsSub$ = this.filtersForm.get('propertyIds').valueChanges
      .subscribe((value: number[]) => {
        this.propertyWorkersStateService.updatePropertyIds(value);
        this.updateTable.emit();
      });
    this.valueChangesShowResignedSub$ = this.filtersForm.get('showResigned').valueChanges
      .subscribe((value: boolean) => {
        this.propertyWorkersStateService.updateShowResigned(value);
        this.updateTable.emit();
      });
    // .pipe(
    //   debounce(x => interval(200)),
    //   filter(value => !R.equals(value, this.propertyWorkersStateService.store.getValue().filters.propertyIds)),
    //   tap(value => {
    //     this.propertyIdsChange(value);
    //   }),
    //   tap(() => this.updateTable.emit())
    // ).subscribe();
  }

  // propertyIdsChange(value: number[]) {
  //   this.propertyWorkersStateService.updatePropertyIds(value);
  //   //this.filtersForm.get('propertyIds').patchValue(value);
  // }

  ngOnDestroy(): void {
  }

  onShowResignedSitesChanged($event: any) {

  }
}
