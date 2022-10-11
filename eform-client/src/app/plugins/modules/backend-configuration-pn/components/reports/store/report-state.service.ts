import { Injectable } from '@angular/core';
import { ReportStore, ReportQuery } from './';
import { Observable } from 'rxjs';
// import { ItemsPlanningPnPlanningsService } from '../../../services';
import { arrayToggle } from '@datorama/akita';

@Injectable({ providedIn: 'root' })
export class ReportStateService {
  constructor(
    private store: ReportStore,
    // private service: ItemsPlanningPnPlanningsService,
    private query: ReportQuery
  ) {}

  getTagIds(): Observable<number[]> {
    return this.query.selectTagIds$;
  }

  getScrollPosition(): Observable<[number, number]> {
    return this.query.selectScrollPosition$;
  }

  addOrRemoveTagIds(id: number) {
    this.store.update((state) => ({
      filters: {
        ...state.filters,
        tagIds: arrayToggle(state.filters.tagIds, id),
      },
    }));
  }

  updateDateRange(dateRange: string[]) {
    this.store.update((_) => ({
      dateRange: dateRange,
    }));
  }

  updateScrollPosition(scrollPosition: [number, number]) {
    this.store.update((_) => ({
      scrollPosition: scrollPosition,
    }));
  }
}
