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
import {debounce, filter, tap} from 'rxjs/operators';
import {TaskWizardStateService} from '../store';
import {TaskWizardStatusesEnum} from '../../../../enums';
import {TranslateService} from '@ngx-translate/core';
import * as R from 'ramda';
import {interval, Subscription} from 'rxjs';

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
  valueChangesStatusSub$: Subscription;
  valueChangesAssignToIdsSub$: Subscription;
  valueChangesTagIdsSub$: Subscription;
  valueChangesFolderIdsSub$: Subscription;
  valueChangesPropertyIdsSub$: Subscription;

  constructor(
    private taskWizardStateService: TaskWizardStateService,
    private translateService: TranslateService,
  ) {
    this.filtersForm = new FormGroup({
      propertyIds: new FormControl([]),
      folderIds: new FormControl({value: [], disabled: true}),
      tagIds: new FormControl([]),
      status: new FormControl(null),
      assignToIds: new FormControl({value: [], disabled: true}),
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
        debounce(x => interval(200)),
        filter(value => !R.equals(value, this.filtersForm.getRawValue())),
        tap(filters => {
          this.propertyIdsChange(filters.propertyIds);
          this.folderIdsChange(filters.folderIds);
          this.tagIdsChange(filters.tagIds);
          this.statusChange(filters.status);
          this.assignToIdsChange(filters.assignToIds);
        })).subscribe();

    this.valueChangesPropertyIdsSub$ = this.filtersForm.get('propertyIds').valueChanges.pipe(
      debounce(x => interval(200)),
      filter(value => !R.equals(value, this.taskWizardStateService.store.getValue().filters.propertyIds)),
      tap(value => {
        this.propertyIdsChange(value);
      }),
      tap(() => this.updateTable.emit())
    ).subscribe();

    this.valueChangesFolderIdsSub$ = this.filtersForm.get('folderIds').valueChanges.pipe(
      debounce(x => interval(200)),
      filter(value => !R.equals(value, this.taskWizardStateService.store.getValue().filters.folderIds)),
      tap(value => {
        this.folderIdsChange(value);
      }),
      tap(() => this.updateTable.emit())
    ).subscribe();

    this.valueChangesTagIdsSub$ = this.filtersForm.get('tagIds').valueChanges.pipe(
      debounce(x => interval(200)),
      filter(value => !R.equals(value, this.taskWizardStateService.store.getValue().filters.tagIds)),
      tap(value => {
        this.tagIdsChange(value);
      }),
      tap(() => this.updateTable.emit())
    ).subscribe();

    this.valueChangesAssignToIdsSub$ = this.filtersForm.get('assignToIds').valueChanges.pipe(
      debounce(x => interval(200)),
      filter(value => !R.equals(value, this.taskWizardStateService.store.getValue().filters.assignToIds)),
      tap(value => {
        this.assignToIdsChange(value);
      }),
      tap(() => this.updateTable.emit())
    ).subscribe();

    this.valueChangesStatusSub$ = this.filtersForm.get('status').valueChanges.pipe(
      debounce(x => interval(200)),
      filter(value => !R.equals(value, this.taskWizardStateService.store.getValue().filters.status)),
      tap(value => {
        this.statusChange(value);
      }),
      tap(() => this.updateTable.emit())
    ).subscribe();
  }

  propertyIdsChange(value: number[]) {
    if (value.length !== 0) {
      this.filtersForm.get('folderIds').enable();
      this.filtersForm.get('assignToIds').enable();
    } else {
      this.filtersForm.get('folderIds').disable();
      this.filtersForm.get('assignToIds').disable();
    }
    this.filtersForm.patchValue({folderIds: [], assignToIds: []});
    this.taskWizardStateService.updatePropertyIds(value);
    this.filtersForm.get('propertyIds').patchValue(value);
    this.folderIdsChange([]);
    this.assignToIdsChange([]);
  }

  folderIdsChange(value: number[]) {
    this.taskWizardStateService.updateFolderIds(value);
    this.filtersForm.get('folderIds').patchValue(value);
  }

  assignToIdsChange(value: number[]) {
    this.taskWizardStateService.updateAssignToIds(value);
    this.filtersForm.get('assignToIds').patchValue(value);
  }

  tagIdsChange(value: number[]) {
    this.taskWizardStateService.updateTagIds(value);
    this.filtersForm.get('tagIds').patchValue(value);
  }

  statusChange(value: TaskWizardStatusesEnum) {
    this.taskWizardStateService.updateStatus(value);
    this.filtersForm.get('status').patchValue(value);
  }

  ngOnDestroy(): void {
  }
}
