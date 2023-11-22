import {Injectable} from '@angular/core';
import {Observable, filter} from 'rxjs';
import {BackendConfigurationPnTaskWizardService} from '../../../../services';
import {CommonDictionaryModel, CommonPaginationState} from 'src/app/common/models';
import {updateTableSort} from 'src/app/common/helpers';
import * as R from 'ramda';
import {TaskWizardStatusesEnum} from 'src/app/plugins/modules/backend-configuration-pn/enums';
import {Store} from '@ngrx/store';
import {
  TaskWizardFiltrationModel, TaskWizardPaginationModel
} from '../../../../state/task-wizard/task-wizard.reducer';
import {
  selectTaskWizardFilters, selectTaskWizardPagination
} from '../../../../state/task-wizard/task-wizard.selector';

@Injectable({providedIn: 'root'})
export class TaskWizardStateService {
  private selectTaskWizardFilters$ = this.store.select(selectTaskWizardFilters);
  private selectTaskWizardPagination$  = this.store.select(selectTaskWizardPagination);
  constructor(
      private store: Store,
    private service: BackendConfigurationPnTaskWizardService,
  ) {
  }

  getAllTasks() {
    let filters: TaskWizardFiltrationModel;
    let pagination: any;
    this.selectTaskWizardFilters$.subscribe((x) => filters = x);
    this.selectTaskWizardPagination$.subscribe((x) => pagination = x);
    return this.service
      .getTasks({
        filters: {...filters},
        pagination: {...pagination},
      }).pipe(
        filter(data => !!(data && data.success && data.model)),
      );
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
    let currentFilters: TaskWizardFiltrationModel;
    this.selectTaskWizardFilters$.subscribe((x) => currentFilters = x);
    const newTagIds = currentFilters.tagIds
      .findIndex(tagId => tagId === tag.id) === -1 ? [...currentFilters.tagIds, tag.id] : currentFilters.tagIds;
    this.store.dispatch({
      type: '[TaskWizard] Update filters',
      payload: {
        filters: {
          tagIds: newTagIds,
          folderIds: currentFilters.folderIds,
          propertyIds: currentFilters.propertyIds,
          assignToIds: currentFilters.assignToIds,
          status: currentFilters.status,
        }
      },
    });
    // this.store.update((state) => ({
    //   filters: {
    //     ...state.filters,
    //     tagIds: state.filters.tagIds
    //     .findIndex(tagId => tagId === tag.id) === -1 ? [...state.filters.tagIds, tag.id] : state.filters.tagIds,
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
    let currentPagination: TaskWizardPaginationModel;
    this.selectTaskWizardPagination$.subscribe((x) => currentPagination = x);
    const localPageSettings = updateTableSort(
      sort,
      currentPagination.sort,
      currentPagination.isSortDsc
    );
    this.store.dispatch({
      type: '[TaskWizard] Update pagination',
      payload: {
        ...currentPagination,
        isSortDsc: localPageSettings.isSortDsc,
        sort: localPageSettings.sort,
      },
    })
  }
}
