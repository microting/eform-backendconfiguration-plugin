import {Injectable} from '@angular/core';
import {Observable} from 'rxjs';
import {CommonPaginationState, OperationDataResult} from 'src/app/common/models';
import {updateTableSort} from 'src/app/common/helpers';
import {BackendConfigurationPnPropertiesService} from '../../../../services';
import {DeviceUserModel} from '../../../../models';
import {Store} from '@ngrx/store';
import {
  PropertyWorkersFiltrationModel,
  propertyWorkersUpdateFilters,
  propertyWorkersUpdatePagination,
  selectPropertyWorkersFilters,
  selectPropertyWorkersPagination
} from '../../../../state';

@Injectable({providedIn: 'root'})
export class PropertyWorkersStateService {
  private selectPropertyWorkersFilters$ = this.store.select(selectPropertyWorkersFilters);
  private selectPropertyWorkersPagination$ = this.store.select(selectPropertyWorkersPagination);
  currentPagination: CommonPaginationState;
  currentFilters: PropertyWorkersFiltrationModel;

  constructor(
    private store: Store,
    private service: BackendConfigurationPnPropertiesService,
  ) {
    this.selectPropertyWorkersPagination$.subscribe(x => this.currentPagination = x);
    this.selectPropertyWorkersFilters$.subscribe(x => this.currentFilters = x);
  }

  getDeviceUsersFiltered(): Observable<OperationDataResult<DeviceUserModel[]>> {
    return this.service.getDeviceUsersFiltered({
      propertyIds: this.currentFilters.propertyIds,
      nameFilter: this.currentFilters.nameFilter,
      sort: this.currentPagination.sort,
      isSortDsc: this.currentPagination.isSortDsc,
      showResigned: this.currentFilters.showResigned,
      tagIds: this.currentFilters.tagIds,
    });
  }

  updateNameFilter(nameFilter: string) {
    if (this.currentFilters.nameFilter !== nameFilter) {
      this.store.dispatch(propertyWorkersUpdateFilters({
        ...this.currentFilters,
        nameFilter: nameFilter,
      }));
    }
  }

  updateTagIds(tagIds: number[]) {
    this.store.dispatch(propertyWorkersUpdateFilters({
      ...this.currentFilters,
      tagIds: tagIds,
    }));
  }

  updatePropertyIds(propertyIds: number[]) {
    this.store.dispatch(propertyWorkersUpdateFilters({
      ...this.currentFilters,
      propertyIds: propertyIds,
    }));
  }

  updateShowResigned(showResigned: boolean) {
    this.store.dispatch(propertyWorkersUpdateFilters({
      ...this.currentFilters,
      showResigned: showResigned,
    }));
  }

  onSortTable(sort: string) {
    const localPageSettings = updateTableSort(
      sort,
      this.currentPagination.sort,
      this.currentPagination.isSortDsc
    );
    this.store.dispatch(propertyWorkersUpdatePagination({
      ...this.currentPagination,
      ...localPageSettings,
    }));
  }
}
