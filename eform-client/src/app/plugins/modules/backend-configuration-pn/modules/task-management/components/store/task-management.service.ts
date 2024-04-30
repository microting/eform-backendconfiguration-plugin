import {Injectable} from '@angular/core';
import {Observable, zip} from 'rxjs';
import {
  CommonPaginationState,
  OperationDataResult,
} from 'src/app/common/models';
import {updateTableSort} from 'src/app/common/helpers';
import {BackendConfigurationPnTaskManagementService} from '../../../../services';
import {WorkOrderCaseModel} from '../../../../models';
import {Store} from '@ngrx/store';
import {
  selectTaskManagementFilters,
  selectTaskManagementPagination,
  TaskManagementFiltrationModel,
  taskManagementUpdateFilters,
  taskManagementUpdatePagination
} from '../../../../state';
import * as R from 'ramda';

@Injectable({providedIn: 'root'})
export class TaskManagementStateService {
  private selectTaskManagementPagination$ = this.store.select(selectTaskManagementPagination);
  private selectTaskManagementFilters$ = this.store.select(selectTaskManagementFilters);
  currentPagination: CommonPaginationState;
  currentFilters: TaskManagementFiltrationModel;

  constructor(
    private store: Store,
    private service: BackendConfigurationPnTaskManagementService,
  ) {
    this.selectTaskManagementPagination$.subscribe(x => this.currentPagination = x);
    this.selectTaskManagementFilters$.subscribe(x => this.currentFilters = x);
  }

  getAllWorkOrderCases(delayed: boolean): Observable<OperationDataResult<WorkOrderCaseModel[]>> {
    return this.service.getWorkOrderCases({
      filters: {...this.currentFilters},
      pagination: {...this.currentPagination},
    });
  }

  downloadWordReport() {
    return this.service.downloadWordReport({
      ...this.currentFilters,
      ...this.currentPagination,
    });
  }

  downloadExcelReport() {
    return this.service
      .downloadExcelReport({
        ...this.currentFilters,
      });
  }

  onSortTable(sort: string) {
    const localPageSettings = updateTableSort(
      sort,
      this.currentPagination.sort,
      this.currentPagination.isSortDsc
    );
    this.store.dispatch(taskManagementUpdatePagination({
      ...this.currentPagination,
      ...localPageSettings,
    }));
  }

  getCurrentPropertyId() {
    return this.currentFilters.propertyId;
  }

  updatePropertyId(propertyId: number) {
    if(this.currentFilters.propertyId !== propertyId) {
      this.store.dispatch(taskManagementUpdateFilters({
        ...this.currentFilters,
        propertyId: propertyId,
      }));
    }
  }

  updateAreaName(areaName?: string) {
    if(this.currentFilters.areaName !== areaName) {
      this.store.dispatch(taskManagementUpdateFilters({
        ...this.currentFilters,
        areaName: areaName,
      }));
    }
  }

  updateCreatedBy(createdBy?: string) {
    if(this.currentFilters.createdBy !== createdBy) {
      this.store.dispatch(taskManagementUpdateFilters({
        ...this.currentFilters,
        createdBy: createdBy,
      }));
    }
  }

  updateLastAssignedTo(lastAssignedTo?: number) {
    if(this.currentFilters.lastAssignedTo !== lastAssignedTo) {
      this.store.dispatch(taskManagementUpdateFilters({
        ...this.currentFilters,
        lastAssignedTo: lastAssignedTo,
      }));
    }
  }
  updateStatuses(statusIds: number[]) {
    if (!R.equals(this.currentFilters.statuses, statusIds)) {
      this.store.dispatch(taskManagementUpdateFilters({
        ...this.currentFilters,
        statuses: statusIds
      }));
      return true;
    }
    return false;
  }

  updateDateFrom(dateFrom?: string | Date) {
    if(this.currentFilters.dateFrom !== dateFrom) {
      this.store.dispatch(taskManagementUpdateFilters({
        ...this.currentFilters,
        dateFrom: dateFrom,
      }));
    }
  }

  updateDateTo(dateTo?: string | Date) {
    if(this.currentFilters.dateTo !== dateTo) {
      this.store.dispatch(taskManagementUpdateFilters({
        ...this.currentFilters,
        dateTo: dateTo,
      }));
    }
  }

  updatePriority(priority?: number) {
    if(this.currentFilters.priority !== priority) {
      this.store.dispatch(taskManagementUpdateFilters({
        ...this.currentFilters,
        priority: priority,
      }));
    }
  }

  updateDelayed(delayed: boolean) {
    if(this.currentFilters.delayed !== delayed) {
      this.store.dispatch(taskManagementUpdateFilters({
        ...this.currentFilters,
        delayed: delayed,
      }));
    }
  }
}
