import {Injectable} from '@angular/core';
import {Observable, tap} from 'rxjs';
import {
  CommonPaginationState,
  OperationDataResult,
  Paged,
} from 'src/app/common/models';
import {updateTableSort} from 'src/app/common/helpers';
import {PropertyModel} from '../../../models';
import {BackendConfigurationPnPropertiesService} from '../../../services';
import {Store} from '@ngrx/store';
import {
  selectPropertiesFilters,
  selectPropertiesPagination,
  propertiesUpdateTotalProperties,
  propertiesUpdateFilters,
  propertiesUpdatePagination,
  PropertiesFiltrationModel,
} from '../../../state';

@Injectable({providedIn: 'root'})
export class PropertiesStateService {
  private selectPropertiesFilters$ = this.store.select(selectPropertiesFilters);
  private selectPropertiesPagination$ = this.store.select(selectPropertiesPagination);
  currentPagination: CommonPaginationState;
  currentFilters: PropertiesFiltrationModel;

  constructor(
    private store: Store,
    private service: BackendConfigurationPnPropertiesService,
  ) {
    this.selectPropertiesPagination$.subscribe(x => this.currentPagination = x);
    this.selectPropertiesFilters$.subscribe(x => this.currentFilters = x);
  }

  getAllProperties(): Observable<OperationDataResult<Paged<PropertyModel>>> {
    return this.service.getAllProperties({...this.currentFilters, ...this.currentPagination}).pipe(
      tap((response) => {
        if (response && response.success && response.model) {
          this.store.dispatch(propertiesUpdateTotalProperties(response.model.total));
        }
      })
    );
  }

  updateNameFilter(nameFilter: string) {
    this.store.dispatch(propertiesUpdateFilters({nameFilter: nameFilter}));
  }

  onSortTable(sort: string) {
    const localPageSettings = updateTableSort(
      sort,
      this.currentPagination.sort,
      this.currentPagination.isSortDsc
    );
    this.store.dispatch(propertiesUpdatePagination({...this.currentPagination, ...localPageSettings}));
  }
}
