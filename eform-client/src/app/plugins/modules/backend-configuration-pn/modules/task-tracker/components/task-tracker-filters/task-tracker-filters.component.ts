import {
  Component,
  EventEmitter, Input,
  OnDestroy,
  OnInit,
  Output,
} from '@angular/core';
import {TaskTrackerStateService} from '../store';
import {FormControl, FormGroup} from '@angular/forms';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {Subscription, take} from 'rxjs';
import {
  BackendConfigurationPnPropertiesService,
} from '../../../../services';
import {CommonDictionaryModel} from 'src/app/common/models';
import {SitesService} from 'src/app/common/services';
import {TranslateService} from '@ngx-translate/core';
import {DateTimeAdapter} from '@danielmoncada/angular-datetime-picker';
import {AuthStateService} from 'src/app/common/store';
import {ItemsPlanningPnTagsService} from 'src/app/plugins/modules/items-planning-pn/services';
import {map, skip, tap} from 'rxjs/operators';
import {MatDialog} from '@angular/material/dialog';
import {
  TaskTrackerShownColumnsComponent
} from 'src/app/plugins/modules/backend-configuration-pn/modules/task-tracker/components';
import {IColumns} from 'src/app/plugins/modules/backend-configuration-pn/models/task-tracker/columns.model';

@AutoUnsubscribe()
@Component({
  selector: 'app-task-tracker-filters',
  templateUrl: './task-tracker-filters.component.html',
  styleUrls: ['./task-tracker-filters.component.scss'],
})
export class TaskTrackerFiltersComponent implements OnInit, OnDestroy {
  @Input() columnsPostRequestSuccess: boolean;
  @Input() columns: IColumns;
  @Output() updateTable: EventEmitter<void> = new EventEmitter<void>();
  filtersForm: FormGroup = new FormGroup({
      propertyIds: new FormControl([-1]), // -1 - it's All
      tags: new FormControl([-1]),
      workers: new FormControl([-1]),
    }
  );
  properties: CommonDictionaryModel[] = [];
  sites: CommonDictionaryModel[] = [];
  tags: CommonDictionaryModel[] = [];

  selectFiltersSub$: Subscription;
  propertyIdValueChangesSub$: Subscription;
  areaNameValueChangesSub$: Subscription;

  constructor(
    private translate: TranslateService,
    private taskTrackerStateService: TaskTrackerStateService,
    private propertyService: BackendConfigurationPnPropertiesService,
    private sitesService: SitesService,
    private itemsPlanningPnTagsService: ItemsPlanningPnTagsService,

  ) {
  }


  ngOnInit(): void {
    this.properties = [{id: -1, name: this.translate.instant('All'), description: ''}];
    this.sites = [{id: -1, name: this.translate.instant('All'), description: ''}];
    this.tags = [{id: -1, name: this.translate.instant('All'), description: ''}];
    this.getProperties();
    this.getSites();
    this.getTags();
    this.subToFormChanges();
  }

  getProperties() {
    this.propertyService.getAllPropertiesDictionary(true).subscribe((data) => {
      if (data && data.success && data.model) {
        this.properties = [{id: -1, name: this.translate.instant('All'), description: ''}, ...data.model];
      }
    });
  }

  getSites() {
    this.sitesService.getAllSitesDictionary().subscribe((result) => {
      if (result && result.success && result.success) {
        this.sites = [{id: -1, name: this.translate.instant('All'), description: ''}, ...result.model];
      }
    });
  }

  getTags() {
    this.itemsPlanningPnTagsService.getPlanningsTags().subscribe((result) => {
      if (result && result.success && result.success) {
        this.tags = [{id: -1, name: this.translate.instant('All'), description: ''}, ...result.model];
      }
    });
  }

  subToFormChanges() {
    this.filtersForm.get('propertyIds').valueChanges
      .pipe(
        tap((propertyIds: number[]) => {
          if (propertyIds.length >= 2 && propertyIds.some(x => x === -1)) {
            this.filtersForm.get('propertyIds').patchValue(propertyIds.filter(x => x !== -1), {emitEvent: false});
          }
          if (propertyIds.length < 1 && !propertyIds.some(x => x === -1)) {
            this.filtersForm.get('propertyIds').patchValue([-1], {emitEvent: false});
          }
          if (propertyIds.length > 2 && propertyIds.some(x => x === -1)) {
            this.filtersForm.get('propertyIds').patchValue([-1], {emitEvent: false});
          }
        }),
      ).subscribe();

    this.filtersForm.get('tags').valueChanges
      .pipe(
        tap((propertyIds: number[]) => {
          if (propertyIds.length >= 2 && propertyIds.some(x => x === -1)) {
            this.filtersForm.get('tags').patchValue(propertyIds.filter(x => x !== -1), {emitEvent: false});
          }
          if (propertyIds.length < 1 && !propertyIds.some(x => x === -1)) {
            this.filtersForm.get('tags').patchValue([-1], {emitEvent: false});
          }
          if (propertyIds.length > 2 && propertyIds.some(x => x === -1)) {
            this.filtersForm.get('tags').patchValue([-1], {emitEvent: false});
          }
        }),
      ).subscribe();

    this.filtersForm.get('workers').valueChanges
      .pipe(
        tap((propertyIds: number[]) => {
          if (propertyIds.length >= 2 && propertyIds.some(x => x === -1)) {
            this.filtersForm.get('workers').patchValue(propertyIds.filter(x => x !== -1), {emitEvent: false});
          }
          if (propertyIds.length < 1 && !propertyIds.some(x => x === -1)) {
            this.filtersForm.get('workers').patchValue([-1], {emitEvent: false});
          }
          if (propertyIds.length > 2 && propertyIds.some(x => x === -1)) {
            this.filtersForm.get('workers').patchValue([-1], {emitEvent: false});
          }
        }),
      ).subscribe();

    this.taskTrackerStateService.getFiltersAsync().pipe(take(1)) // get values FIRST time
      .subscribe(filters => {
        this.filtersForm.patchValue({
          propertyIds: filters.propertyIds,
          tags: filters.tags,
          workers: filters.workers,
        });
      });

    this.filtersForm.valueChanges.pipe(skip(1)) // skip initial values
      .subscribe((filters) => {
        this.taskTrackerStateService.updateFilters({
          propertyIds: filters.propertyIds,
          workers: filters.workers,
          tags: filters.tags,
        })
        this.updateTable.emit();
      })
  }

  ngOnDestroy(): void {
  }
}
