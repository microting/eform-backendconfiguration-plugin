import { Injectable } from '@angular/core';
import { persistState, Store, StoreConfig } from '@datorama/akita';
import {
  FiltrationStateModel,
  CommonPaginationState,
} from 'src/app/common/models';


export interface DocumentsFiltrationModel {
  propertyId: number,
  folderId?: string,
  documentId?: string,
  expiration?: string,
}

export interface DocumentsState {
  pagination: CommonPaginationState;
  filters: DocumentsFiltrationModel;
  //totalProperties: number;
}

function createInitialState(): DocumentsState {
  return <DocumentsState>{
    pagination: {
      //pageSize: 10,
      sort: 'Id',
      isSortDsc: false,
      //offset: 0,
    },
    filters: {
      propertyId: -1,
      folderId: null,
      documentId: null,
      expiration: null,
    }
  };
}

const propertiesPersistStorage = persistState({
  include: ['documents'],
  key: 'backendConfigurationPn',
  preStorageUpdate(storeName, state: DocumentsState) {
    return {
      pagination: state.pagination,
      filters: state.filters,
    };
  },
});

@Injectable({ providedIn: 'root' })
@StoreConfig({ name: 'documents', resettable: true })
export class DocumentsStore extends Store<DocumentsState> {
  constructor() {
    super(createInitialState());
  }
}

export const propertiesPersistProvider = {
  provide: 'persistStorage',
  useValue: propertiesPersistStorage,
  multi: true,
};
