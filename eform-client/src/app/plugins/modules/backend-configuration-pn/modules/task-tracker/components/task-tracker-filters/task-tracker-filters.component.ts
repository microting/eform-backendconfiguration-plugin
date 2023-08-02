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
import {TranslateService} from '@ngx-translate/core';
import {tap} from 'rxjs/operators';

@AutoUnsubscribe()
@Component({
  selector: 'app-task-tracker-filters',
  templateUrl: './task-tracker-filters.component.html',
  styleUrls: ['./task-tracker-filters.component.scss'],
})
export class TaskTrackerFiltersComponent implements OnInit, OnDestroy {
  @Output() updateTable: EventEmitter<void> = new EventEmitter<void>();
  @Input() properties: CommonDictionaryModel[] = [];
  @Input() tags: CommonDictionaryModel[] = [];
  filtersForm: FormGroup = new FormGroup({
      propertyIds: new FormControl([]),
      tags: new FormControl([]),
      workers: new FormControl([]),
    }
  );
  sites: CommonDictionaryModel[] = [];

  propertyIdValueChangesSub$: Subscription;
  getFiltersAsyncSub$: Subscription;
  filtersFormChangesSub$: Subscription;
  getSitesSub$: Subscription;

  constructor(
    private translate: TranslateService,
    private taskTrackerStateService: TaskTrackerStateService,
    private propertyService: BackendConfigurationPnPropertiesService,
  ) {
  }

  ngOnInit(): void {
    this.propertyIdValueChangesSub$ = this.filtersForm
      .get('propertyIds')
      .valueChanges.subscribe((value: any) => {
          if(value.length !== 0) {
            this.getSites(value);
          } else {
            this.sites = [];
          }
        }
      );
    this.subToFormChanges();
  }

  getSites(propertyIds: number[]) {
    this.getSitesSub$ = this.propertyService
      .getLinkedSitesByMultipleProperties(propertyIds)
      .pipe(tap(result => {
        if (result && result.success && result.success) {
          this.sites = result.model;
        }
      })).subscribe();
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
