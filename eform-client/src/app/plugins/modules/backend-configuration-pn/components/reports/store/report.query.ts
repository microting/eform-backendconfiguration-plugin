import { Injectable } from '@angular/core';
import { Query } from '@datorama/akita';
import {
  ReportState,
  ReportStore,
} from './report.store';

@Injectable({ providedIn: 'root' })
export class ReportQuery extends Query<ReportState> {
  constructor(protected store: ReportStore) {
    super(store);
  }

  get pageSetting() {
    return this.getValue();
  }

  selectScrollPosition$ = this.select((state) => state.scrollPosition);
  selectTagIds$ = this.select((state) => state.filters.tagIds);
}
