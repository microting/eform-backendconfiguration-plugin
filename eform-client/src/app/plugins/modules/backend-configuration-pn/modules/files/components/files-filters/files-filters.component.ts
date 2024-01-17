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
import {BackendConfigurationPnPropertiesService,} from '../../../../services';
import {CommonDictionaryModel, SharedTagModel} from 'src/app/common/models';
import {TranslateService} from '@ngx-translate/core';
import {FilesStateService} from '../../store';
import {format, parse} from 'date-fns';
import {debounceTime, skip} from 'rxjs/operators';
import {Store} from '@ngrx/store';
import {
  selectFilesFilters,
  FilesFiltrationModel, filesUpdateFilters
} from '../../../../state';

@AutoUnsubscribe()
@Component({
  selector: 'app-files-filters',
  templateUrl: './files-filters.component.html',
  styleUrls: ['./files-filters.component.scss'],
})
export class FilesFiltersComponent implements OnInit, OnDestroy {
  @Output() updateTable: EventEmitter<void> = new EventEmitter<void>();
  private _availableTags: SharedTagModel[] = [];
  filtersForm: FormGroup;
  properties: CommonDictionaryModel[] = [];
  dateFormat = 'yyyy-MM-dd';
  currentFilters: FilesFiltrationModel;
  private selectFilesFilters$ = this.store.select(selectFilesFilters);

  selectFiltersSub$: Subscription;
  filterChangesSub$: Subscription;
  getAllPropertiesSub$: Subscription;
  @Input()
  get availableTags(): SharedTagModel[] {
    return this._availableTags;
  }

  set availableTags(val: SharedTagModel[]) {
    this._availableTags = val ?? [];
    if (this.filtersForm && this.filtersForm.controls) {
      // delete from filter deleted tags
      const newTagIdsWithoutDeletedTags = this.filtersForm.value.tagIds.filter((x: number) => this._availableTags.some(y => y.id === x));
      if (newTagIdsWithoutDeletedTags.length !== this.currentFilters.tagIds.length) {
        this.filtersForm.patchValue({
          tagIds: newTagIdsWithoutDeletedTags,
        });
      }
    }
  }

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
    private store: Store,
    private translate: TranslateService,
    public filesStateService: FilesStateService,
    private propertyService: BackendConfigurationPnPropertiesService,
  ) {
    this.selectFiltersSub$ = this.selectFilesFilters$.subscribe((filters) => this.currentFilters = filters)
  }

  ngOnInit(): void {
    this.getProperties();
    this.selectFilesFilters$
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
          ...this.currentFilters,
          ...value,
          dateRange: {
            dateFrom: value.dateRange.dateFrom && format(value.dateRange.dateFrom, this.dateFormat),
            dateTo: value.dateRange.dateTo && format(value.dateRange.dateTo, this.dateFormat)
          },
        };

        if (filters.dateRange.dateFrom && !filters.dateRange.dateTo) {
          return; // no update store and table if date range not fulfilled
        }
        this.store.dispatch(filesUpdateFilters(filters))
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
