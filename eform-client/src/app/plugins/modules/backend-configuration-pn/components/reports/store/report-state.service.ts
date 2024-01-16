import {Injectable} from '@angular/core';
import {Store} from '@ngrx/store';
import {
  reportsV1UpdateDateRange,
  reportsV1UpdateFilters,
  reportsV1UpdateScrollPosition,
  selectReportsV1DateRange,
  selectReportsV1Filters,
  selectReportsV1ScrollPosition
} from '../../../state';
import {FiltrationStateModel} from 'src/app/common/models';
import {arrayToggle} from 'src/app/common/helpers';
import {ReportPnGenerateModel} from '../../../models';

@Injectable({providedIn: 'root'})
export class ReportStateService {
  private selectReportsV1DateRange$ = this.store.select(selectReportsV1DateRange);
  private selectReportsV1Filters$ = this.store.select(selectReportsV1Filters);
  private selectReportsV1ScrollPosition$ = this.store.select(selectReportsV1ScrollPosition);
  currentDateRange: { startDate: string, endDate: string };
  currentFilters: FiltrationStateModel;
  currentScrollPosition: [number, number];

  constructor(
    private store: Store,
  ) {
    this.selectReportsV1DateRange$.subscribe(x => this.currentDateRange = x);
    this.selectReportsV1Filters$.subscribe(x => this.currentFilters = x);
    this.selectReportsV1ScrollPosition$.subscribe(x => this.currentScrollPosition = x);
  }

  addOrRemoveTagIds(id: number) {
    this.store.dispatch(reportsV1UpdateFilters({
      ...this.currentFilters,
      tagIds: arrayToggle(this.currentFilters.tagIds, id)
    }));
  }

  updateDateRange(dateRange: { startDate?: string, endDate?: string, }) {
    if (dateRange.startDate != this.currentDateRange.startDate || dateRange.endDate != this.currentDateRange.endDate) {
      this.store.dispatch(reportsV1UpdateDateRange({
        startDate: dateRange.startDate || this.currentDateRange.startDate,
        endDate: dateRange.endDate || this.currentDateRange.endDate,
      }));
    }
  }

  updateScrollPosition(scrollPosition: [number, number]) {
    this.store.dispatch(reportsV1UpdateScrollPosition(scrollPosition || this.currentScrollPosition));
  }

  extractData(): ReportPnGenerateModel {
    return new ReportPnGenerateModel({
      dateFrom: this.currentDateRange.startDate,
      dateTo: this.currentDateRange.endDate,
      tagIds: [...this.currentFilters.tagIds],
    });
  }
}
