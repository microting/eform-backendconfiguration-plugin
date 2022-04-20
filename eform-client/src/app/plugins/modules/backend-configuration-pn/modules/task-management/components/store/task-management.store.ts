import { Injectable } from '@angular/core';
import { persistState, Store, StoreConfig } from '@datorama/akita';
import {
  CommonPaginationState,
} from 'src/app/common/models';

export interface TaskManagementFiltrationModel {
  propertyId: number,
  areaName: string,
  createdBy?: string,
  lastAssignedTo?: string,
  status?: number,
  dateFrom?: Date | string,
  dateTo?: Date | string,
}

export interface TaskManagementState {
  pagination: CommonPaginationState;
  filters: TaskManagementFiltrationModel;
  // total: number;
}

function createInitialState(): TaskManagementState {
  return <TaskManagementState>{
    pagination: {
      // pageSize: 10,
      sort: 'CreatedAt',
      isSortDsc: false,
      // offset: 0,
    },
    filters: {
      propertyId: null,
      areaName: null,
    },
    // total: 0,
  };
}

const taskManagementPersistStorage = persistState({
  include: ['task-management'],
  key: 'backendConfigurationPn',
  preStorageUpdate(storeName, state: TaskManagementState) {
    return {
      pagination: state.pagination,
      filters: state.filters,
    };
  },
});

@Injectable({ providedIn: 'root' })
@StoreConfig({ name: 'task-management', resettable: true })
export class TaskManagementStore extends Store<TaskManagementState> {
  constructor() {
    super(createInitialState());
  }
}

export const taskManagementPersistProvider = {
  provide: 'persistStorage',
  useValue: taskManagementPersistStorage,
  multi: true,
};
