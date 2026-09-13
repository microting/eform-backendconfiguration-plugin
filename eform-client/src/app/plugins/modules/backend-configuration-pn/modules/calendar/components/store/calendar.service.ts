import {Injectable} from '@angular/core';
import {Store} from '@ngrx/store';
import {
  calendarUpdateFilters,
  CalendarFiltersModel,
  selectCalendarActiveBoardIds,
  selectCalendarActiveSiteIds,
  selectCalendarActiveTeamIds,
  selectCalendarActiveTagNames,
  selectCalendarCurrentDate,
  selectCalendarFilters,
  selectCalendarPropertyId,
  selectCalendarViewMode,
} from '../../../../state';

@Injectable({providedIn: 'root'})
export class CalendarStateService {
  readonly filters$ = this.store.select(selectCalendarFilters);
  readonly propertyId$ = this.store.select(selectCalendarPropertyId);
  readonly viewMode$ = this.store.select(selectCalendarViewMode);
  readonly currentDate$ = this.store.select(selectCalendarCurrentDate);
  readonly activeBoardIds$ = this.store.select(selectCalendarActiveBoardIds);
  readonly activeSiteIds$ = this.store.select(selectCalendarActiveSiteIds);
  readonly activeTeamIds$ = this.store.select(selectCalendarActiveTeamIds);
  readonly activeTagNames$ = this.store.select(selectCalendarActiveTagNames);

  private currentFilters: CalendarFiltersModel;

  constructor(private store: Store) {
    this.filters$.subscribe(f => this.currentFilters = f);
  }

  updatePropertyId(propertyId: number | null) {
    this.dispatch({propertyId, activeBoardIds: [], activeSiteIds: [], activeTeamIds: [], activeTagNames: []});
  }

  updateViewMode(viewMode: 'week' | 'day' | 'schedule' | 'month') {
    this.dispatch({viewMode});
  }

  updateCurrentDate(currentDate: string) {
    this.dispatch({currentDate});
  }

  toggleBoard(boardId: number) {
    const ids = this.currentFilters.activeBoardIds;
    const activeBoardIds = ids.includes(boardId)
      ? ids.filter(id => id !== boardId)
      : [...ids, boardId];
    this.dispatch({activeBoardIds});
  }

  setActiveBoardIds(ids: number[]) {
    this.dispatch({activeBoardIds: ids});
  }

  toggleTeam(teamId: number) {
    const ids = this.currentFilters.activeTeamIds;
    const activeTeamIds = ids.includes(teamId)
      ? ids.filter(id => id !== teamId)
      : [...ids, teamId];
    this.dispatch({activeTeamIds});
  }

  toggleSite(siteId: number) {
    const ids = this.currentFilters.activeSiteIds;
    const activeSiteIds = ids.includes(siteId)
      ? ids.filter(id => id !== siteId)
      : [...ids, siteId];
    this.dispatch({activeSiteIds});
  }

  /**
   * The toolbar's "All employees" reset (#1211). Clears BOTH assignee lists in
   * ONE dispatch — two calls would emit two filter states, and the caller
   * reloads the grid after each dispatch, so the first reload would fire for a
   * half-cleared filter and could still land last.
   *
   * Scoped to the assignee filter on purpose: the calendars (`activeBoardIds`)
   * and the planning tags (`activeTagNames`) are separate controls and must
   * survive it. `updatePropertyId` remains the only thing that clears everything.
   */
  clearAssignees() {
    this.dispatch({activeSiteIds: [], activeTeamIds: []});
  }

  private dispatch(partial: Partial<CalendarFiltersModel>) {
    this.store.dispatch(calendarUpdateFilters(partial));
  }
}
