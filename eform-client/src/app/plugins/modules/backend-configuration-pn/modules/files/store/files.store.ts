import { Injectable } from '@angular/core';
import { persistState, Store, StoreConfig } from '@datorama/akita';
import {
  FiltrationStateModel,
  CommonPaginationState,
} from 'src/app/common/models';


export interface FilesFiltrationModel extends FiltrationStateModel{
  propertyIds: number[],
  dateRange: string[],
}

export interface FilesState {
  pagination: CommonPaginationState;
  filters: FilesFiltrationModel;
  total: number;
}

function createInitialState(): FilesState {
  return <FilesState>{
    pagination: {
      //pageSize: 10,
      sort: 'Id',
      isSortDsc: false,
      //offset: 0,
    },
    filters: {
      propertyIds: [],
      dateRange: [],
      nameFilter: '',
      tagIds: [],
    }
  };
}

const filesPersistStorage = persistState({
  include: ['files'],
  key: 'backendConfigurationPn',
  preStorageUpdate(storeName, state: FilesState) {
    return {
      pagination: state.pagination,
      filters: state.filters,
    };
  },
});

@Injectable({ providedIn: 'root' })
@StoreConfig({ name: 'files', resettable: true })
export class FilesStore extends Store<FilesState> {
  constructor() {
    super(createInitialState());
  }
}

export const filesPersistProvider = {
  provide: 'persistStorage',
  useValue: filesPersistStorage,
  multi: true,
};
