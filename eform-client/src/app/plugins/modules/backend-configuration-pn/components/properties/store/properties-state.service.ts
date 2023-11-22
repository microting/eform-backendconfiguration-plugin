import { Injectable } from '@angular/core';
import {Observable, zip} from 'rxjs';
import {
  CommonPaginationState,
  OperationDataResult,
  Paged,
} from 'src/app/common/models';
import { updateTableSort } from 'src/app/common/helpers';
import { map } from 'rxjs/operators';
import {PropertiesRequestModel, PropertyModel} from '../../../models';
import { BackendConfigurationPnPropertiesService } from '../../../services';
import {Store} from '@ngrx/store';
import {
  selectPropertiesFilters, selectPropertiesPagination
} from '../../../state/properties/properties.selector';

@Injectable({ providedIn: 'root' })
export class PropertiesStateService {
  private selectPropertiesFilters$ = this.store.select(selectPropertiesFilters);
  private selectPropertiesPagination$ = this.store.select(selectPropertiesPagination);
  constructor(
    private store: Store,
    private service: BackendConfigurationPnPropertiesService,
  ) {}

  getAllProperties(): Observable<OperationDataResult<Paged<PropertyModel>>> {
    let propertiesRequestModel = new PropertiesRequestModel();
    zip(this.selectPropertiesFilters$, this.selectPropertiesPagination$).subscribe(
        ([filters, pagination]) => {
            propertiesRequestModel = {
            ...propertiesRequestModel,
            ...filters,
            ...pagination,
            };
        }
        ).unsubscribe();
    return this.service.getAllProperties(propertiesRequestModel).pipe(
        map((response) => {
            if (response && response.success && response.model) {
                this.store.dispatch({
                    type: '[Properties] Update Properties Total Properties', payload: {
                    pagination: {
                      total: response.model.total,
                    },
                    totalProperties: response.model.total,
                  }
                });
            }
            return response;
        })
    );
  }

  updateNameFilter(nameFilter: string) {
    let currentFilters: any;
    this.selectPropertiesFilters$.subscribe((filters) => {
      if (filters === undefined) {
        return;
      }
      currentFilters = filters;
    }).unsubscribe();
    this.store.dispatch({
      type: '[Properties] Update Filters', payload: {
        filters: {nameFilter: nameFilter}
      }
    });
  }

  onDelete() {
    // this.store.update((state) => ({
    //   totalProperties: state.totalProperties - 1,
    // }));
    // this.checkOffset();
  }

  onSortTable(sort: string) {
    let currentPagination: CommonPaginationState;
    this.selectPropertiesPagination$.subscribe((pagination) => {
      if (pagination === undefined) {
        return;
      }
      currentPagination = pagination;
    }).unsubscribe();
    const localPageSettings = updateTableSort(
      sort,
      currentPagination.sort,
      currentPagination.isSortDsc
    );
    this.store.dispatch({
      type: '[Properties] Update Pagination', payload: {
        pagination: {sort: localPageSettings.sort, isSortDsc: localPageSettings.isSortDsc}
      }
    });
  }
}
