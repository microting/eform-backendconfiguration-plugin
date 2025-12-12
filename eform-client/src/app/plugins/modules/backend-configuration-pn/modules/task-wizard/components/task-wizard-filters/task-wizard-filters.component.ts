import {
  Component,
  EventEmitter, Input,
  OnDestroy,
  OnInit,
  Output,
  inject
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
  selectTaskWizardFilters, TaskWizardFiltrationModel,
} from '../../../../state';
import {debounce, filter, tap} from 'rxjs/operators';
import {ActivatedRoute} from '@angular/router';

@AutoUnsubscribe()
@Component({
    selector: 'app-task-wizard-filters',
    templateUrl: './task-wizard-filters.component.html',
    styleUrls: ['./task-wizard-filters.component.scss'],
    standalone: false
})
export class TaskWizardFiltersComponent implements OnInit, OnDestroy {
  private store = inject(Store);
  private translateService = inject(TranslateService);
  private route = inject(ActivatedRoute);
  private taskWizardStateService = inject(TaskWizardStateService);

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
  showDiagram: boolean = false;
  firstLoad: boolean = true;
  private selectTaskWizardFilters$ = this.store.select(selectTaskWizardFilters);
  currentFilters: TaskWizardFiltrationModel;


  constructor() {
    this.selectTaskWizardFilters$.subscribe(x => this.currentFilters = x);
    this.route.queryParams.subscribe(x => {
      if (x && x.showDiagram) {
        this.showDiagram = x.showDiagram;
      }
    });
  }


  ngOnInit(): void {
    this.filtersForm = new FormGroup({
      propertyIds: new FormControl(this.currentFilters.propertyIds),
      folderIds: new FormControl(this.currentFilters.folderIds),
      tagIds: new FormControl(this.currentFilters.tagIds),
      status: new FormControl(this.currentFilters.status),
      assignToIds: new FormControl(this.currentFilters.assignToIds),
    });
    this.firstLoad = false;
    this.statuses = Object.keys(TaskWizardStatusesEnum)
      .filter(key => isNaN(Number(key))) // Filter out numeric keys that TypeScript adds to enumerations
      .map(key => {
        return {
          label: this.translateService.instant(key),
          value: TaskWizardStatusesEnum[key],
        };
      });

    // TODO: Implement this logic correctly
    // this.getFiltersAsyncSub$ = this.selectTaskWizardFilters$
    //   .pipe(
    //     debounce(() => interval(200)),
    //     filter(value => !R.equals(value, this.filtersForm.getRawValue())),
    //     tap(filters => {
    //       debugger;
    //       if (this.firstLoad) {
    //         this.firstLoad = false;
    //         this.filtersForm.get('propertyIds').patchValue(filters.propertyIds);
    //         this.filtersForm.get('assignToIds').patchValue(filters.assignToIds);
    //       }
    //     })).subscribe();

    this.valueChangesPropertyIdsSub$ = this.filtersForm.get('propertyIds').valueChanges
      .subscribe((value: number[]) => {
        this.taskWizardStateService.updatePropertyIds(value);
        this.propertyIdsChange(value);
        if (!this.firstLoad) {
          this.updateTable.emit();
        }
      });

    this.valueChangesFolderIdsSub$ = this.filtersForm.get('folderIds').valueChanges
      .subscribe((value: number[]) => {
        if (this.taskWizardStateService.updateFolderIds(value)) {
          if (!this.firstLoad) {
            this.updateTable.emit();
          }
        }
      });
    this.valueChangesTagIdsSub$ = this.filtersForm.get('tagIds').valueChanges
      .subscribe((value: number[]) => {
        if (this.taskWizardStateService.updateTagIds(value)) {
          if (!this.firstLoad) {
            this.updateTable.emit();
          }
        }
      });
    this.valueChangesAssignToIdsSub$ = this.filtersForm.get('assignToIds').valueChanges
      .subscribe((value: number[]) => {
        if (this.taskWizardStateService.updateAssignToIds(value)) {
          if (!this.firstLoad) {
            this.updateTable.emit();
          }
        }
      });
    this.valueChangesStatusSub$ = this.filtersForm.get('status').valueChanges
      .subscribe((value: TaskWizardStatusesEnum | null) => {
        if (this.taskWizardStateService.updateStatus(value)) {
          if (!this.firstLoad) {
            this.updateTable.emit();
          }
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

  ngOnDestroy(): void {
  }
}
