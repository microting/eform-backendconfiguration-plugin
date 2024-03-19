import {Injectable} from '@angular/core';
import {Store} from '@ngrx/store';
import {
  reportsV2UpdateDateRange,
  reportsV2UpdateFilters, reportsV2UpdateScrollPosition,
  selectReportsV2DateRange,
  selectReportsV2Filters,
  selectReportsV2ScrollPosition
} from '../../../../state';
import {arrayToggle} from 'src/app/common/helpers';
import {ReportPnGenerateModel} from '../../../../models';
import {FiltrationStateModel} from 'src/app/common/models';

@Injectable({providedIn: 'root'})
export class ReportStateService {
  private selectReportsV2DateRange$ = this.store.select(selectReportsV2DateRange);
  private selectReportsV2Filters$ = this.store.select(selectReportsV2Filters);
  private selectReportsV2ScrollPosition$ = this.store.select(selectReportsV2ScrollPosition);
  currentDateRange: { startDate: string, endDate: string };
  currentFilters: FiltrationStateModel;
  currentScrollPosition: [number, number];

  constructor(
    private store: Store,
  ) {
    this.selectReportsV2DateRange$.subscribe(x => this.currentDateRange = x);
    this.selectReportsV2Filters$.subscribe(x => this.currentFilters = x);
    this.selectReportsV2ScrollPosition$.subscribe(x => this.currentScrollPosition = x);
  }

  addOrRemoveTagIds(id: number) {
    this.store.dispatch(reportsV2UpdateFilters({
      ...this.currentFilters,
      tagIds: arrayToggle(this.currentFilters.tagIds, id)
    }));
  }

  updateDateRange(dateRange: { startDate?: string, endDate?: string, }) {
    if (dateRange.startDate !== this.currentDateRange.startDate || dateRange.endDate !== this.currentDateRange.endDate) {
      this.store.dispatch(reportsV2UpdateDateRange({
        startDate: dateRange.startDate || this.currentDateRange.startDate,
        endDate: dateRange.endDate || this.currentDateRange.endDate,
      }));
    }
  }

  updateScrollPosition(scrollPosition: [number, number]) {
    this.store.dispatch(reportsV2UpdateScrollPosition(scrollPosition || this.currentScrollPosition));
  }

  extractData(): ReportPnGenerateModel {
    return new ReportPnGenerateModel({
      dateFrom: this.currentDateRange.startDate,
      dateTo: this.currentDateRange.endDate,
      tagIds: [...this.currentFilters.tagIds],
    });
  }
}
