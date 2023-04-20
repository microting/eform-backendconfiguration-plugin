import { Injectable } from '@angular/core';
import { persistState, Store, StoreConfig } from '@datorama/akita';

export interface TaskTrackerFiltrationModel {
  propertyIds: number[];
  tagIds: number[];
  workerIds: number[];
}

export interface TaskTrackerState {
  filters: TaskTrackerFiltrationModel;
}

function createInitialState(): TaskTrackerState {
  return <TaskTrackerState>{
    filters: {
      propertyIds: [-1], // -1 - it's All
      tagIds: [-1],
      workerIds: [-1]
    },
  };
}

const taskTrackerPersistStorage = persistState({
  include: ['task-tracker'],
  key: 'backendConfigurationPn',
  preStorageUpdate(storeName, state: TaskTrackerState) {
    return {
      filters: state.filters,
    };
  },
});

@Injectable({ providedIn: 'root' })
@StoreConfig({ name: 'task-tracker', resettable: true })
export class TaskTrackerStore extends Store<TaskTrackerState> {
  constructor() {
    super(createInitialState());
  }
}

export const taskTrackerPersistProvider = {
  provide: 'persistStorage',
  useValue: taskTrackerPersistStorage,
  multi: true,
};
