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
import {TaskWizardStateService} from '../store';
import {TaskWizardStatusesEnum} from '../../../../enums';
import {TranslateService} from '@ngx-translate/core';
import {interval, Subscription} from 'rxjs';
import {Store} from '@ngrx/store';
import * as R from 'ramda';
import {
  selectTaskWizardFilters, selectTaskWizardPropertyIds
} from '../../../../state/task-wizard/task-wizard.selector';
import {debounce, filter, tap} from 'rxjs/operators';

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

  valueChangesStatusSub$: Subscription;
  valueChangesAssignToIdsSub$: Subscription;
  valueChangesTagIdsSub$: Subscription;
  valueChangesFolderIdsSub$: Subscription;
  valueChangesPropertyIdsSub$: Subscription;
  getFiltersAsyncSub$: Subscription;
  private selectTaskWizardFilters$ = this.store.select(selectTaskWizardFilters);
  private selectTaskWizardPropertyIds$ = this.store.select(selectTaskWizardPropertyIds);

  constructor(
    private store: Store,
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

    this.getFiltersAsyncSub$ = this.selectTaskWizardFilters$
      .pipe(
        debounce(x => interval(200)),
        filter(value => !R.equals(value, this.filtersForm.getRawValue())),
        tap(filters => {
          // this.propertyIdsChange(filters.propertyIds);
          // this.folderIdsChange(filters.folderIds);
          this.tagIdsChange(filters.tagIds);
          // this.statusChange(filters.status);
          // this.assignToIdsChange(filters.assignToIds);
        })).subscribe();

    this.valueChangesPropertyIdsSub$ = this.filtersForm.get('propertyIds').valueChanges
      .subscribe((value: number[]) => {
        let currentFilters: any;
        this.selectTaskWizardFilters$.subscribe((filters) => {
          currentFilters = filters;
        }).unsubscribe();
          this.store.dispatch({
            type: '[TaskWizard] Update filters',
            payload: {
              filters: {
                propertyIds: value,
                folderIds: [],
                assignToIds: [],
                tagIds: [],
                status: null,
              }
            }
          });
          this.propertyIdsChange(value);
          this.updateTable.emit();
    });

    this.valueChangesFolderIdsSub$ = this.filtersForm.get('folderIds').valueChanges
      .subscribe((value: number[]) => {
        let currentFilters: any;
        this.selectTaskWizardFilters$.subscribe((filters) => {
          currentFilters = filters;
        }).unsubscribe();
        if (currentFilters.folderIds.length !== value.length) {
          this.store.dispatch({
            type: '[TaskWizard] Update filters',
            payload: {
              filters: {
                ...currentFilters,
                folderIds: value,
              }
            }
          });
          this.updateTable.emit();
        }
      });
    this.valueChangesTagIdsSub$ = this.filtersForm.get('tagIds').valueChanges
      .subscribe((value: number[]) => {
        let currentFilters: any;
        this.selectTaskWizardFilters$.subscribe((filters) => {
          currentFilters = filters;
        }).unsubscribe();
        if (currentFilters.tagIds.length !== value.length) {
          this.store.dispatch({
            type: '[TaskWizard] Update filters',
            payload: {
              filters: {
                ...currentFilters,
                tagIds: value,
              }
            }
          });
          this.updateTable.emit();
        }
      });
    this.valueChangesAssignToIdsSub$ = this.filtersForm.get('assignToIds').valueChanges
      .subscribe((value: number[]) => {
        let currentFilters: any;
        this.selectTaskWizardFilters$.subscribe((filters) => {
          currentFilters = filters;
        }).unsubscribe();
        if (currentFilters.assignToIds.length !== value.length) {
          this.store.dispatch({
            type: '[TaskWizard] Update filters',
            payload: {
              filters: {
                ...currentFilters,
                assignToIds: value,
              }
            }
          });
          this.updateTable.emit();
        }
      });
    this.valueChangesStatusSub$ = this.filtersForm.get('status').valueChanges
      .subscribe((value: TaskWizardStatusesEnum | null) => {
        let currentFilters: any;
        this.selectTaskWizardFilters$.subscribe((filters) => {
          currentFilters = filters;
        }).unsubscribe();
        if (currentFilters.status !== value) {
          this.store.dispatch({
            type: '[TaskWizard] Update filters',
            payload: {
              filters: {
                ...currentFilters,
                status: value,
              }
            }
          });
          this.updateTable.emit();
        }
      });
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
  }

  tagIdsChange(value: number[]) {
    // this.taskWizardStateService.updateTagIds(value);
    this.filtersForm.get('tagIds').patchValue(value);
    this.updateTable.emit();
  }

  ngOnDestroy(): void {
  }
}
