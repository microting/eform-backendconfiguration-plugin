import {Injectable} from '@angular/core';
import {Observable, filter} from 'rxjs';
import {BackendConfigurationPnTaskWizardService} from '../../../../services';
import {CommonDictionaryModel} from 'src/app/common/models';
import {updateTableSort} from 'src/app/common/helpers';
import * as R from 'ramda';
import {TaskWizardStatusesEnum} from 'src/app/plugins/modules/backend-configuration-pn/enums';
import {Store} from '@ngrx/store';
import {
  TaskWizardFiltrationModel
} from '../../../../state/task-wizard/task-wizard.reducer';

@Injectable({providedIn: 'root'})
export class TaskWizardStateService {
  constructor(
      private store: Store,
    private service: BackendConfigurationPnTaskWizardService,
  ) {
  }

  getAllTasks() {
    // return this.service
    //   .getTasks({
    //     filters: {...this.query.pageSetting.filters},
    //     pagination: {...this.query.pageSetting.pagination},
    //   }).pipe(
    //     filter(data => !!(data && data.success && data.model)),
    //   );
  }

  // getFiltersAsync(): Observable<TaskWizardFiltrationModel> {
  //   return this.query.selectFilters$;
  // }

  updatePropertyIds(propertyIds: number[]) {
    // if(!R.equals(this.store.getValue().filters.propertyIds, propertyIds)) {
    //   this.store.update((state) => ({
    //     filters: {
    //       ...state.filters,
    //       propertyIds: propertyIds,
    //     },
    //   }));
    // }
  }

  updateFolderIds(folderIds: number[]) {
    // if(!R.equals(this.store.getValue().filters.folderIds, folderIds)) {
    //   this.store.update((state) => ({
    //     filters: {
    //       ...state.filters,
    //       folderIds: folderIds,
    //     },
    //   }));
    // }
  }

  updateTagIds(tagIds: number[]) {
    // if(!R.equals(this.store.getValue().filters.tagIds, tagIds)) {
    //   this.store.update((state) => ({
    //     filters: {
    //       ...state.filters,
    //       tagIds: tagIds,
    //     },
    //   }));
    // }
  }

  updateStatus(status: TaskWizardStatusesEnum) {
    // if(!R.equals(this.store.getValue().filters.status, status)) {
    //   this.store.update((state) => ({
    //     filters: {
    //       ...state.filters,
    //       status: status,
    //     },
    //   }));
    // }
  }

  updateAssignToIds(assignToIds: number[]) {
    // if(!R.equals(this.store.getValue().filters.assignToIds, assignToIds)) {
    //   this.store.update((state) => ({
    //     filters: {
    //       ...state.filters,
    //       assignToIds: assignToIds,
    //     },
    //   }));
    // }
  }

  addTagToFilters(tag: CommonDictionaryModel) {
    // this.store.update((state) => ({
    //   filters: {
    //     ...state.filters,
    //     tagIds: state.filters.tagIds.findIndex(tagId => tagId === tag.id) === -1 ? [...state.filters.tagIds, tag.id] : state.filters.tagIds,
    //   },
    // }));
  }

  // getActiveSort(): Observable<string> {
  //   return this.query.selectActiveSort$;
  // }
  //
  // getActiveSortDirection(): Observable<'asc' | 'desc'> {
  //   return this.query.selectActiveSortDirection$;
  // }

  onSortTable(sort: string) {
    // const localPageSettings = updateTableSort(
    //   sort,
    //   this.query.pageSetting.pagination.sort,
    //   this.query.pageSetting.pagination.isSortDsc
    // );
    // this.store.update((state) => ({
    //   pagination: {
    //     ...state.pagination,
    //     isSortDsc: localPageSettings.isSortDsc,
    //     sort: localPageSettings.sort,
    //   },
    // }));
  }
}
