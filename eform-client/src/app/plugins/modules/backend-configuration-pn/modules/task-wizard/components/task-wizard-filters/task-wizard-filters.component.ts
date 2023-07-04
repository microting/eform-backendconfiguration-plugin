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
import {take} from 'rxjs';
import {tap} from 'rxjs/operators';
import {TaskWizardStateService} from '../store';

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
  @Input() sites: CommonDictionaryModel[] = [];
  filtersForm: FormGroup<{
    tagIds: FormControl<number[]>,
    assignToIds: FormControl<number[]>,
    propertyIds: FormControl<number[]>,
    statuses: FormControl<number[]>,
    folderIds: FormControl<number[]>
  }>;

  constructor(private taskWizardStateService: TaskWizardStateService) {
    this.filtersForm = new FormGroup({
      propertyIds: new FormControl([]),
      folderIds: new FormControl([]),
      tagIds: new FormControl([]),
      statuses: new FormControl([]),
      assignToIds: new FormControl([]),
    });
  }

  ngOnInit(): void {
    this.taskWizardStateService.getFiltersAsync()
      .pipe(
        take(1),
        tap(filters => {
          this.filtersForm.patchValue({
            propertyIds: filters.propertyIds,
            folderIds: filters.folderIds,
            tagIds: filters.tagIds,
            statuses: filters.statuses,
            assignToIds: filters.assignToIds,
          });
        }))
      .subscribe();
    this.filtersForm.valueChanges
      .pipe(
        tap(value => {
          this.taskWizardStateService.updateFilters({
            propertyIds: value.propertyIds,
            folderIds: value.folderIds,
            tagIds: value.tagIds,
            statuses: value.statuses,
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
