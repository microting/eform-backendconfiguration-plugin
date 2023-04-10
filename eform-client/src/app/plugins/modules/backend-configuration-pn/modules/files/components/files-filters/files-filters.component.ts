import {
  Component,
  EventEmitter, Input,
  OnDestroy,
  OnInit,
  Output,
} from '@angular/core';
import {FormControl, FormGroup} from '@angular/forms';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {Subscription} from 'rxjs';
import {
  BackendConfigurationPnPropertiesService,
} from '../../../../services';
import {CommonDictionaryModel, SharedTagModel} from 'src/app/common/models';
import {TranslateService} from '@ngx-translate/core';
import {FilesFiltrationModel, FilesStateService} from '../../store';
import {Moment} from 'moment';
import {format} from 'date-fns';
import {debounceTime} from 'rxjs/operators';
import moment from 'moment';
import * as R from 'ramda';

@AutoUnsubscribe()
@Component({
  selector: 'app-files-filters',
  templateUrl: './files-filters.component.html',
  styleUrls: ['./files-filters.component.scss'],
})
export class FilesFiltersComponent implements OnInit, OnDestroy {
  @Output() updateTable: EventEmitter<void> = new EventEmitter<void>();
  @Input()
  get availableTags(): SharedTagModel[] {
    return this._availableTags;
  }
  set availableTags(val: SharedTagModel[]) {
    this._availableTags = val ?? [];
    if(this.filtersForm && this.filtersForm.controls) {
      // delete from filter deleted tags
      const newTagIdsWithoutDeletedTags = this.filtersForm.value.tagIds.filter((x: number) => this._availableTags.some(y => y.id === x));
      if (newTagIdsWithoutDeletedTags.length !== this.filesStateService.store.getValue().filters.tagIds.length) {
        this.filtersForm.patchValue({
          tagIds: newTagIdsWithoutDeletedTags,
        });
      }
    }
  }
  private _availableTags: SharedTagModel[] = []

  filtersForm: FormGroup;
  properties: CommonDictionaryModel[] = [];

  selectFiltersSub$: Subscription;
  filterChangesSub$: Subscription;
  getAllPropertiesSub$: Subscription;

  get dateRangeFilterControl() {
    if(this.filtersForm && this.filtersForm.controls) {
      return this.filtersForm.controls.dateRange;
    }
  }

  get nameFilterControl() {
    if(this.filtersForm && this.filtersForm.controls) {
      return this.filtersForm.controls.nameFilter;
    }
  }

  constructor(
    private translate: TranslateService,
    public filesStateService: FilesStateService,
    private propertyService: BackendConfigurationPnPropertiesService,
  ) {
  }

  ngOnInit(): void {
    this.getProperties();
    this.selectFiltersSub$ = this.filesStateService
      .getFiltersAsync()
      .subscribe((filters) => {
        if (!this.filtersForm) {
          this.filtersForm = new FormGroup({
            propertyIds: new FormControl(filters.propertyIds),
            dateRange: new FormControl(filters.dateRange.map(x => moment(x))),
            nameFilter: new FormControl(filters.nameFilter),
            tagIds: new FormControl(filters.tagIds),
          });
        } else {
          this.filtersForm.patchValue({
            propertyIds: filters.propertyIds,
            dateRange: filters.dateRange.map(x => moment(x)),
            nameFilter: filters.nameFilter,
            tagIds: filters.tagIds,
          }, {emitEvent: false});
        }
      });

    this.filterChangesSub$ = this.filtersForm.valueChanges
      .pipe(debounceTime(500))
      .subscribe((value: { propertyIds: number[], dateRange: string[], nameFilter: string, tagIds: number[] }) => {
        const filters: FilesFiltrationModel = {...this.filesStateService.store.getValue().filters};
        // @ts-ignore
        value.dateRange = value.dateRange.filter(x => !R.isNil(x)).map((x: Moment) => format(x.toDate(), 'yyyy-MM-dd'));
        if (value && !R.equals(filters, value)) {
          if (!R.equals(filters.propertyIds, value.propertyIds)) {
            filters.propertyIds = value.propertyIds;
          }
          if (!R.isNil(value.dateRange)) {
            // delete null from range
            if (!R.equals(filters.dateRange, value.dateRange)) {
              filters.dateRange = value.dateRange;
            }
          }
          if (!R.equals(filters.nameFilter, value.nameFilter)) {
            filters.nameFilter = value.nameFilter;
          }
          if (!R.equals(filters.tagIds, value.tagIds)) {
            filters.tagIds = value.tagIds;
          }
          this.filesStateService.updateFilters(filters);
          this.updateTable.emit();
        }
      });
  }

  getProperties() {
    this.getAllPropertiesSub$ = this.propertyService.getAllPropertiesDictionary()
      .subscribe((data) => {
      if (data && data.success && data.model) {
        this.properties = data.model;
        // delete from filter deleted properties
        this.filtersForm.patchValue({
          propertyIds: this.filtersForm.value.propertyIds.filter((x: number) => this.properties.some(y => y.id === x)),
        });
      }
    });
  }

  ngOnDestroy(): void {
  }
}
