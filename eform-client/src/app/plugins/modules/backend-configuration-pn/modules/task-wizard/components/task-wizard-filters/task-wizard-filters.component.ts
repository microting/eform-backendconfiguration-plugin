import {
  Component,
  EventEmitter, Input,
  OnDestroy,
  OnInit,
  Output,
} from '@angular/core';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {CommonDictionaryModel, FolderDto} from 'src/app/common/models';
import {FormControl, FormGroup} from '@angular/forms';
import {filter, tap} from 'rxjs/operators';
import {TaskWizardStateService} from '../store';
import {TaskWizardStatusesEnum} from '../../../../enums';
import {TranslateService} from '@ngx-translate/core';
import * as R from 'ramda'
import {Subscription} from 'rxjs';

@AutoUnsubscribe()
@Component({
  selector: 'app-task-wizard-filters',
  templateUrl: './task-wizard-filters.component.html',
  styleUrls: ['./task-wizard-filters.component.scss'],
})
export class TaskWizardFiltersComponent implements OnInit, OnDestroy {
  @Output() updateTable: EventEmitter<void> = new EventEmitter<void>();
  @Input() properties: CommonDictionaryModel[] = [];
  @Input() folders: FolderDto[] = [];
  @Input() tags: CommonDictionaryModel[] = [];
  @Input() sites: CommonDictionaryModel[] = [];
  statuses: { label: string, value: number }[] = [];

  filtersForm: FormGroup<{
    tagIds: FormControl<number[]>,
    assignToIds: FormControl<number[]>,
    propertyIds: FormControl<number[]>,
    status: FormControl<TaskWizardStatusesEnum | null>,
    folderIds: FormControl<number[]>
  }>;
  getFiltersAsyncSub$: Subscription;
  valueChangesSub$: Subscription;

  constructor(
    private taskWizardStateService: TaskWizardStateService,
    private translateService: TranslateService,
  ) {
    this.filtersForm = new FormGroup({
      propertyIds: new FormControl([]),
      folderIds: new FormControl([]),
      tagIds: new FormControl([]),
      status: new FormControl(null),
      assignToIds: new FormControl([]),
    });
  }

  ngOnInit(): void {
    this.statuses = Object.keys(TaskWizardStatusesEnum)
      .filter(key => isNaN(Number(key))) // Filter out numeric keys that TypeScript adds to enumerations
      .map(key => {
        return {
          label: this.translateService.instant(key),
          value: TaskWizardStatusesEnum[key],
        };
      });
    this.getFiltersAsyncSub$ = this.taskWizardStateService.getFiltersAsync()
      .pipe(
        filter(value => !R.equals(value, this.filtersForm.getRawValue())),
        tap(filters => {
          this.filtersForm.patchValue({
            propertyIds: filters.propertyIds,
            folderIds: filters.folderIds,
            tagIds: filters.tagIds,
            status: filters.status,
            assignToIds: filters.assignToIds,
          }, {emitEvent: false});
        }))
      .subscribe();
    this.valueChangesSub$ = this.filtersForm.valueChanges
      .pipe(
        filter(value => !R.equals(this.taskWizardStateService.store.getValue().filters, value)),
        tap(value => {
          this.taskWizardStateService.updateFilters({
            propertyIds: value.propertyIds,
            folderIds: value.folderIds,
            tagIds: value.tagIds,
            status: value.status,
            assignToIds: value.assignToIds,
          });
        }),
        tap(() => this.updateTable.emit())
      )
      .subscribe();
  }

  ngOnDestroy(): void {
  }
}
