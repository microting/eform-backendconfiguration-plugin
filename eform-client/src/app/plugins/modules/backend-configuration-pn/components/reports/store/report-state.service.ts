import {Injectable} from '@angular/core';
import {Store} from '@ngrx/store';
import {
  selectReportsV1DateRange, selectReportsV1FiltersTagIds, selectReportsV1ScrollPosition
} from '../../../state/reports-v1/reports-v1.selector';

@Injectable({providedIn: 'root'})
export class ReportStateService {
  private selectReportsV1DateRange$ = this.store.select(selectReportsV1DateRange);
  private selectReportsV1FiltersTagIds$ = this.store.select(selectReportsV1FiltersTagIds);
  private selectReportsV1ScrollPosition$ = this.store.select(selectReportsV1ScrollPosition);
  constructor(
    private store: Store,
  ) {
  }

  addOrRemoveTagIds(id: number) {
    let currentTagIds: number[];
    this.selectReportsV1FiltersTagIds$.subscribe((filters) => {
      if (filters === undefined) {
        return;
      }
      currentTagIds = filters;
    }).unsubscribe();
    this.store.dispatch({
      type: '[ReportsV1] Update filters',
      payload: {
        filters: {tagIds: this.arrayToggle(currentTagIds, id)}
      }
    });
  }


  updateDateRange(dateRange: { startDate?: string, endDate?: string, }) {
    let currentRange: { startDate: string, endDate: string };
    this.selectReportsV1DateRange$.subscribe((range) => {
      currentRange = range;
    }).unsubscribe();
    this.store.dispatch({
      type: '[ReportsV1] Update date range',
      payload: {
        dateRange: {
          startDate: dateRange.startDate ? dateRange.startDate : currentRange.startDate,
          endDate: dateRange.endDate ? dateRange.endDate : currentRange.endDate,
        }
      }
    });
  }

  updateScrollPosition(scrollPosition: [number, number]) {
    let currentScrollPosition: [number, number];
    this.selectReportsV1ScrollPosition$.subscribe((position) => {
      currentScrollPosition = position;
    }).unsubscribe();
    this.store.dispatch({
      type: '[ReportsV1] Update scroll position',
      payload: {
        scrollPosition: scrollPosition ? scrollPosition : currentScrollPosition,
      }
    });
  }

  arrayToggle<T>(arr: T[], val: T, forced?: boolean): T[] {
    if (forced && arr.includes(val)) {
      return [...arr];
    } else if (forced === false || arr.includes(val)) {
      return arr.filter((v: typeof val) => v !== val);
    }
    return [...arr, val];
  }
}
