import {
  Component,
  EventEmitter,
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
import {ItemsPlanningPnTagsService} from '../../../../../items-planning-pn/services';

@AutoUnsubscribe()
@Component({
  selector: 'app-task-tracker-filters',
  templateUrl: './task-tracker-filters.component.html',
  styleUrls: ['./task-tracker-filters.component.scss'],
})
export class TaskTrackerFiltersComponent implements OnInit, OnDestroy {
  @Output() updateTable: EventEmitter<void> = new EventEmitter<void>();
  filtersForm: FormGroup = new FormGroup({
      propertyIds: new FormControl([]),
      tags: new FormControl([]),
      workers: new FormControl([]),
    }
  );
  properties: CommonDictionaryModel[] = [];
  sites: CommonDictionaryModel[] = [];
  tags: CommonDictionaryModel[] = [];

  getAllPropertiesDictionarySub$: Subscription;
  getAllSitesDictionarySub$: Subscription;
  getPlanningsTagsSub$: Subscription;
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
    this.getProperties();
    this.getSites();
    this.getTags();
    this.subToFormChanges();
  }

  getProperties() {
    this.getAllPropertiesDictionarySub$ = this.propertyService.getAllPropertiesDictionary(true).subscribe((data) => {
      if (data && data.success && data.model) {
        this.properties = [...data.model];
      }
    });
  }

  getSites() {
    this.getAllSitesDictionarySub$ = this.sitesService.getAllSitesDictionary().subscribe((result) => {
      if (result && result.success && result.success) {
        this.sites = [...result.model];
      }
    });
  }

  getTags() {
    this.getPlanningsTagsSub$ = this.itemsPlanningPnTagsService.getPlanningsTags().subscribe((result) => {
      if (result && result.success && result.success) {
        this.tags = [...result.model];
      }
    });
  }

  subToFormChanges() {
    this.getFiltersAsyncSub$ = this.taskTrackerStateService.getFiltersAsync().pipe(take(1)) // get values FIRST time
      .subscribe(filters => {
        this.filtersForm.patchValue({
          propertyIds: filters.propertyIds ?? [],
          tags: filters.tagIds ?? [],
          workers: filters.workerIds ?? [],
        });
      });

    this.filtersFormChangesSub$ = this.filtersForm.valueChanges
      .subscribe((filters) => {
        this.taskTrackerStateService.updateFilters({
          propertyIds: filters.propertyIds,
          workerIds: filters.workers,
          tagIds: filters.tags,
        });
        this.updateTable.emit();
      });
  }

  ngOnDestroy(): void {
  }
}
