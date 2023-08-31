import {Injectable} from '@angular/core';
import {Query} from '@datorama/akita';
import {
  StatisticsState,
  StatisticsStore
} from './';

@Injectable({providedIn: 'root'})
export class StatisticsQuery extends Query<StatisticsState> {
  constructor(protected store: StatisticsStore) {
    super(store);
  }

  get pageSetting() {
    return this.getValue();
  }

  selectPropertyId$ = this.select((state) => state.filters.propertyId);
}
