import { Injectable } from '@angular/core';
import { persistState, Store, StoreConfig } from '@datorama/akita';
import { FiltrationStateModel } from 'src/app/common/models';

export interface ReportState {
  filters: FiltrationStateModel;
  dateRange: string[];
  scrollPosition: [number, number];
}

function createInitialState(): ReportState {
  return <ReportState>{
    filters: {
      tagIds: [],
    },
    dateRange: [],
    scrollPosition: [0, 0],
  };
}

const persistState1 = persistState({
  include: ['backendConfigurationReport'],
  key: 'backendConfigurationPn',
  preStorageUpdate(
    storeName,
    state: ReportState
  ): ReportState {
    return {
      filters: state.filters,
      dateRange: state.dateRange,
      scrollPosition: [0, 0],
    };
  },
});

@Injectable({ providedIn: 'root' })
@StoreConfig({ name: 'backendConfigurationReport', resettable: true })
export class ReportStore extends Store<ReportState> {
  constructor() {
    super(createInitialState());
  }
}

export const planningsReportPersistProvider = {
  provide: 'persistStorage',
  useValue: persistState1,
  multi: true,
};
