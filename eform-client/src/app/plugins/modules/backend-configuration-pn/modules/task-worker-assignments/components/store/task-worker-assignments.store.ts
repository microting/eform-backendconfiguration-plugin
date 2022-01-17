import { Injectable } from '@angular/core';
import { persistState, Store, StoreConfig } from '@datorama/akita';
import {
  FiltrationStateModel,
  CommonPaginationState,
} from 'src/app/common/models';

export interface TaskWorkerAssignmentsState {
  pagination: CommonPaginationState;
  // filters: FiltrationStateModel;
  totalProperties: number;
}

function createInitialState(): TaskWorkerAssignmentsState {
  return <TaskWorkerAssignmentsState>{
    pagination: {
      sort: 'Id',
      isSortDsc: false,
    },
    // filters: {
    //   nameFilter: '',
    //   // tagIds: [],
    // },
    totalProperties: 0,
  };
}

const taskWorkerAssignmentsPersistStorage = persistState({
  include: ['taskWorkerAssignments'],
  key: 'backendConfigurationPn',
  preStorageUpdate(storeName, state: TaskWorkerAssignmentsState) {
    return {
      pagination: state.pagination,
      // filters: state.filters,
    };
  },
});

@Injectable({ providedIn: 'root' })
@StoreConfig({ name: 'taskWorkerAssignments', resettable: true })
export class TaskWorkerAssignmentsStore extends Store<TaskWorkerAssignmentsState> {
  constructor() {
    super(createInitialState());
  }
}

export const taskWorkerAssignmentsPersistProvider = {
  provide: 'persistStorage',
  useValue: taskWorkerAssignmentsPersistStorage,
  multi: true,
};
