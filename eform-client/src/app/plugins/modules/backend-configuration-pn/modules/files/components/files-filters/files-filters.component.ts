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
} from '../../../../services';
import {CommonDictionaryModel, SharedTagModel} from 'src/app/common/models';
import {TranslateService} from '@ngx-translate/core';
import {FilesFiltrationModel, FilesStateService} from '../../store';
import {Moment} from 'moment';
import {format, parse} from 'date-fns';
import {debounceTime, skip} from 'rxjs/operators';
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
    if (this.filtersForm && this.filtersForm.controls) {
      // delete from filter deleted tags
      const newTagIdsWithoutDeletedTags = this.filtersForm.value.tagIds.filter((x: number) => this._availableTags.some(y => y.id === x));
      if (newTagIdsWithoutDeletedTags.length !== this.filesStateService.store.getValue().filters.tagIds.length) {
        this.filtersForm.patchValue({
          tagIds: newTagIdsWithoutDeletedTags,
        });
      }
    }
  }

  private _availableTags: SharedTagModel[] = [];

  filtersForm: FormGroup;
  properties: CommonDictionaryModel[] = [];
  dateFormat = 'yyyy-MM-dd';

  selectFiltersSub$: Subscription;
  filterChangesSub$: Subscription;
  getAllPropertiesSub$: Subscription;

  get dateRangeFilterControl() {
    if (this.filtersForm && this.filtersForm.controls) {
      return this.filtersForm.controls.dateRange;
    }
  }

  get displayClearButtonOnDateRangeFilter(): boolean {
    if (this.filtersForm && this.filtersForm.controls) {
      return this.filtersForm.get('dateRange.dateFrom').value !== null || this.filtersForm.get('dateRange.dateTo').value !== null;
    }
  }

  get nameFilterControl() {
    if (this.filtersForm && this.filtersForm.controls) {
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
      .pipe(take(1))
      .subscribe((filters) => {
        this.filtersForm = new FormGroup({
          propertyIds: new FormControl(filters.propertyIds),
          dateRange: new FormGroup({
            dateFrom: new FormControl(filters.dateRange.dateFrom ? parse(filters.dateRange.dateFrom, this.dateFormat, new Date) : null),
            dateTo: new FormControl(filters.dateRange.dateTo ? parse(filters.dateRange.dateTo, this.dateFormat, new Date) : null),
          }),
          nameFilter: new FormControl(filters.nameFilter),
          tagIds: new FormControl(filters.tagIds),
        });
      });

    this.filterChangesSub$ = this.filtersForm.valueChanges
      .pipe(debounceTime(500), skip(1))
      .subscribe((value: { propertyIds: number[], dateRange: { dateFrom: Date, dateTo: Date }, nameFilter: string, tagIds: number[] }) => {
        const filters: FilesFiltrationModel = {
          ...this.filesStateService.store.getValue().filters,
          ...value,
          dateRange: {
            dateFrom: value.dateRange.dateFrom && format(value.dateRange.dateFrom, this.dateFormat),
            dateTo: value.dateRange.dateTo && format(value.dateRange.dateTo, this.dateFormat)
          },
        };

        if (filters.dateRange.dateFrom && !filters.dateRange.dateTo) {
          return; // no update store and table if date range not fulfilled
        }
        this.filesStateService.updateFilters(filters);
        this.updateTable.emit();
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

  clearDateRangeFilter() {
    this.filtersForm.get('dateRange').patchValue({dateFrom: null, dateTo: null});
  }

  ngOnDestroy(): void {
  }
}
