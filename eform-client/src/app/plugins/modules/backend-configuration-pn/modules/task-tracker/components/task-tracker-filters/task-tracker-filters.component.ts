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
import {ItemsPlanningPnTagsService} from 'src/app/plugins/modules/items-planning-pn/services';
import {skip, tap} from 'rxjs/operators';

@AutoUnsubscribe()
@Component({
  selector: 'app-task-tracker-filters',
  templateUrl: './task-tracker-filters.component.html',
  styleUrls: ['./task-tracker-filters.component.scss'],
})
export class TaskTrackerFiltersComponent implements OnInit, OnDestroy {
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

  getAllPropertiesDictionarySub$: Subscription;
  getAllSitesDictionarySub$: Subscription;
  getPlanningsTagsSub$: Subscription;
  propertyIdsChangesSub$: Subscription;
  tagsChangesSub$: Subscription;
  workersChangesSub$: Subscription;
  getFiltersAsyncSub$: Subscription;
  filtersFormChangesSub$: Subscription;

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
    this.getAllPropertiesDictionarySub$ = this.propertyService.getAllPropertiesDictionary(true).subscribe((data) => {
      if (data && data.success && data.model) {
        this.properties = [{id: -1, name: this.translate.instant('All'), description: ''}, ...data.model];
      }
    });
  }

  getSites() {
    this.getAllSitesDictionarySub$ = this.sitesService.getAllSitesDictionary().subscribe((result) => {
      if (result && result.success && result.success) {
        this.sites = [{id: -1, name: this.translate.instant('All'), description: ''}, ...result.model];
      }
    });
  }

  getTags() {
    this.getPlanningsTagsSub$ = this.itemsPlanningPnTagsService.getPlanningsTags().subscribe((result) => {
      if (result && result.success && result.success) {
        this.tags = [{id: -1, name: this.translate.instant('All'), description: ''}, ...result.model];
      }
    });
  }

  subToFormChanges() {
    this.propertyIdsChangesSub$ = this.filtersForm.get('propertyIds').valueChanges
      .subscribe((propertyIds: number[]) => {
        if (propertyIds.length >= 2 && propertyIds.some(x => x === -1)) {
          this.filtersForm.get('propertyIds').patchValue(propertyIds.filter(x => x !== -1), {emitEvent: false});
        }
        if (propertyIds.length < 1 && !propertyIds.some(x => x === -1)) {
          this.filtersForm.get('propertyIds').patchValue([-1], {emitEvent: false});
        }
        if (propertyIds.length > 2 && propertyIds.some(x => x === -1)) {
          this.filtersForm.get('propertyIds').patchValue([-1], {emitEvent: false});
        }
      });

    this.tagsChangesSub$ = this.filtersForm.get('tags').valueChanges
      .subscribe((tagIds: number[]) => {
        if (tagIds.length >= 2 && tagIds.some(x => x === -1)) {
          this.filtersForm.get('tags').patchValue(tagIds.filter(x => x !== -1), {emitEvent: false});
        }
        if (tagIds.length < 1 && !tagIds.some(x => x === -1)) {
          this.filtersForm.get('tags').patchValue([-1], {emitEvent: false});
        }
        if (tagIds.length > 2 && tagIds.some(x => x === -1)) {
          this.filtersForm.get('tags').patchValue([-1], {emitEvent: false});
        }
      });

    this.workersChangesSub$ = this.filtersForm.get('workers').valueChanges
      .subscribe((workerIds: number[]) => {
        if (workerIds.length >= 2 && workerIds.some(x => x === -1)) {
          this.filtersForm.get('workers').patchValue(workerIds.filter(x => x !== -1), {emitEvent: false});
        }
        if (workerIds.length < 1 && !workerIds.some(x => x === -1)) {
          this.filtersForm.get('workers').patchValue([-1], {emitEvent: false});
        }
        if (workerIds.length > 2 && workerIds.some(x => x === -1)) {
          this.filtersForm.get('workers').patchValue([-1], {emitEvent: false});
        }
      });

    this.getFiltersAsyncSub$ = this.taskTrackerStateService.getFiltersAsync().pipe(take(1)) // get values FIRST time
      .subscribe(filters => {
        this.filtersForm.patchValue({
          propertyIds: filters.propertyIds ?? [-1],
          tags: filters.tagIds ?? [-1],
          workers: filters.workerIds ?? [-1],
        });
      });

    this.filtersFormChangesSub$ = this.filtersForm.valueChanges.pipe(skip(1)) // skip initial values
      .subscribe((filters) => {
        this.taskTrackerStateService.updateFilters({
          propertyIds: filters.propertyIds,
          workerIds: filters.workers,
          tagIds: filters.tags,
        })
        this.updateTable.emit();
      })
  }

  ngOnDestroy(): void {
  }
}
