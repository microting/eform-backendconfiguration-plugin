import {Injectable} from '@angular/core';
import {filter} from 'rxjs';
import {BackendConfigurationPnTaskWizardService} from '../../../../services';
import {CommonDictionaryModel} from 'src/app/common/models';
import {updateTableSort} from 'src/app/common/helpers';
import {TaskWizardStatusesEnum} from '../../../../enums';
import {Store} from '@ngrx/store';
import * as R from 'ramda';
import {
  selectTaskWizardFilters,
  selectTaskWizardPagination,
  taskWizardUpdateFilters,
  taskWizardUpdatePagination,
  TaskWizardFiltrationModel,
  TaskWizardPaginationModel,
} from '../../../../state';

@Injectable({providedIn: 'root'})
export class TaskWizardStateService {
  private selectTaskWizardFilters$ = this.store.select(selectTaskWizardFilters);
  private selectTaskWizardPagination$ = this.store.select(selectTaskWizardPagination);
  currentPagination: TaskWizardPaginationModel;
  currentFilters: TaskWizardFiltrationModel;

  constructor(
    private store: Store,
    private service: BackendConfigurationPnTaskWizardService,
  ) {
    this.selectTaskWizardFilters$.subscribe((x) => this.currentFilters = x);
    this.selectTaskWizardPagination$.subscribe((x) => this.currentPagination = x);
  }

  getAllTasks() {
    return this.service
      .getTasks({
        filters: {...this.currentFilters},
        pagination: {...this.currentPagination},
      }).pipe(
        filter(data => !!(data && data.success && data.model)),
      );
  }

  updatePropertyIds(propertyIds: number[]) {
    if (!R.equals(this.currentFilters.propertyIds, propertyIds)) {
      this.store.dispatch(taskWizardUpdateFilters({
        ...this.currentFilters,
        propertyIds: propertyIds
      }));
      return true;
    }
    return false;
  }

  updateFolderIds(folderIds: number[]) {
    if (!R.equals(this.currentFilters.folderIds, folderIds)) {
      this.store.dispatch(taskWizardUpdateFilters({
        ...this.currentFilters,
        folderIds: folderIds
      }));
      return true;
    }
    return false;
  }

  updateTagIds(tagIds: number[]) {
    if (!R.equals(this.currentFilters.tagIds, tagIds)) {
      this.store.dispatch(taskWizardUpdateFilters({
        ...this.currentFilters,
        tagIds: tagIds
      }));
      return true;
    }
    return false;
  }

  updateStatus(status: TaskWizardStatusesEnum) {
    if (!R.equals(this.currentFilters.status, status)) {
      this.store.dispatch(taskWizardUpdateFilters({
        ...this.currentFilters,
        status: status
      }));
      return true;
    }
    return false;
  }

  updateAssignToIds(assignToIds: number[]) {
    if (!R.equals(this.currentFilters.assignToIds, assignToIds)) {
      this.store.dispatch(taskWizardUpdateFilters({
        ...this.currentFilters,
        assignToIds: assignToIds
      }));
      return true;
    }
    return false;
  }

  addTagToFilters(tag: CommonDictionaryModel) {
    const newTagIds = this.currentFilters.tagIds
      .findIndex(tagId => tagId === tag.id) === -1 ? [...this.currentFilters.tagIds, tag.id] : this.currentFilters.tagIds;
    this.store.dispatch(taskWizardUpdateFilters({
      ...this.currentFilters,
      tagIds: newTagIds
    }));
  }

  onSortTable(sort: string) {
    const localPageSettings = updateTableSort(
      sort,
      this.currentPagination.sort,
      this.currentPagination.isSortDsc
    );
    this.store.dispatch(taskWizardUpdatePagination({
      ...this.currentPagination,
      ...localPageSettings,
    }));
  }
}
