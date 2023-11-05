import {Injectable} from '@angular/core';
import {Observable} from 'rxjs';
import {Store} from '@ngrx/store';

@Injectable({providedIn: 'root'})
export class ReportStateService {
  constructor(
      private store: Store,
  ) {
  }

  addOrRemoveTagIds(id: number) {
    // this.store.update((state) => ({
    //   filters: {
    //     ...state.filters,
    //     tagIds: arrayToggle(state.filters.tagIds, id),
    //   },
    // }));
  }


  updateDateRange(dateRange: { startDate?: string, endDate?: string, }) {
    // this.store.update((state) => ({
    //   dateRange: {
    //     ...{
    //       startDate: dateRange.startDate ? dateRange.startDate : state.dateRange.startDate,
    //       endDate: dateRange.endDate ? dateRange.endDate : state.dateRange.endDate,
    //     }
    //   },
    // }));
  }

  updateScrollPosition(scrollPosition: [number, number]) {
    // this.store.update((_) => ({
    //   scrollPosition: scrollPosition,
    // }));
  }
}
