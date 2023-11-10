import {Injectable} from '@angular/core';
import {Store} from '@ngrx/store';
import {
  selectReportsV2DateRange,
  selectReportsV2FiltersTagIds, selectReportsV2ScrollPosition
} from '../../../../state/reports-v2/reports-v2.selector';

@Injectable({providedIn: 'root'})
export class ReportStateService {
  private selectReportsV2DateRange$ = this.store.select(selectReportsV2DateRange);
  private selectReportsV2FiltersTagIds$ = this.store.select(selectReportsV2FiltersTagIds);
  private selectReportsV2ScrollPosition$ = this.store.select(selectReportsV2ScrollPosition);
  constructor(
      private store: Store,
  ) {
  }

  addOrRemoveTagIds(id: number) {
    let currentTagIds: number[];
    this.selectReportsV2FiltersTagIds$.subscribe((filters) => {
      if (filters === undefined) {
        return;
      }
      currentTagIds = filters;
    }).unsubscribe();
    this.store.dispatch({
      type: '[ReportsV2] Update filters',
      payload: {
        filters: {tagIds: this.arrayToggle(currentTagIds, id)}
      }
    });
  }


  updateDateRange(dateRange: { startDate?: string, endDate?: string, }) {
    let currentRange: { startDate: string, endDate: string };
    this.selectReportsV2DateRange$.subscribe((range) => {
      currentRange = range;
    }).unsubscribe();
    this.store.dispatch({
      type: '[ReportsV2] Update date range',
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
    this.selectReportsV2ScrollPosition$.subscribe((position) => {
      currentScrollPosition = position;
    }).unsubscribe();
    this.store.dispatch({
      type: '[ReportsV2] Update scroll position',
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
