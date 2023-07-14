import {Injectable} from '@angular/core';
import {
  TaskWizardStore,
  TaskWizardQuery,
  TaskWizardFiltrationModel,
} from './';
import {Observable, filter} from 'rxjs';
import {BackendConfigurationPnTaskWizardService} from '../../../../services';
import {CommonDictionaryModel} from 'src/app/common/models';
import {updateTableSort} from 'src/app/common/helpers';

@Injectable({providedIn: 'root'})
export class TaskWizardStateService {
  constructor(
    public store: TaskWizardStore,
    private service: BackendConfigurationPnTaskWizardService,
    private query: TaskWizardQuery
  ) {
  }

  getAllTasks() {
    return this.service
      .getTasks({
        filters: {...this.query.pageSetting.filters},
        pagination: {...this.query.pageSetting.pagination},
      }).pipe(
        filter(data => !!(data && data.success && data.model)),
      );
  }

  getFiltersAsync(): Observable<TaskWizardFiltrationModel> {
    return this.query.selectFilters$;
  }

  updateFilters(taskManagementFiltrationModel: TaskWizardFiltrationModel) {
    this.store.update((state) => ({
      filters: {
        ...state.filters,
        ...taskManagementFiltrationModel
      },
    }));
  }

  addTagToFilters(tag: CommonDictionaryModel) {
    this.store.update((state) => ({
      filters: {
        ...state.filters,
        tagIds: state.filters.tagIds.findIndex(tagId => tagId === tag.id) === -1 ? [...state.filters.tagIds, tag.id] : state.filters.tagIds,
      },
    }));
  }

  getActiveSort(): Observable<string> {
    return this.query.selectActiveSort$;
  }

  getActiveSortDirection(): Observable<'asc' | 'desc'> {
    return this.query.selectActiveSortDirection$;
  }

  onSortTable(sort: string) {
    const localPageSettings = updateTableSort(
      sort,
      this.query.pageSetting.pagination.sort,
      this.query.pageSetting.pagination.isSortDsc
    );
    this.store.update((state) => ({
      pagination: {
        ...state.pagination,
        isSortDsc: localPageSettings.isSortDsc,
        sort: localPageSettings.sort,
      },
    }));
  }
}
