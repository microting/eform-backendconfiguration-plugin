import {Injectable} from '@angular/core';
import {Store} from '@ngrx/store';
import {
  calendarUpdateFilters,
  CalendarFiltersModel,
  selectCalendarActiveBoardIds,
  selectCalendarActiveSiteIds,
  selectCalendarActiveTagNames,
  selectCalendarCurrentDate,
  selectCalendarFilters,
  selectCalendarPropertyId,
  selectCalendarSidebarOpen,
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
  readonly activeTagNames$ = this.store.select(selectCalendarActiveTagNames);
  readonly sidebarOpen$ = this.store.select(selectCalendarSidebarOpen);

  private currentFilters: CalendarFiltersModel;

  constructor(private store: Store) {
    this.filters$.subscribe(f => this.currentFilters = f);
  }

  updatePropertyId(propertyId: number | null) {
    this.dispatch({propertyId, activeBoardIds: [], activeSiteIds: [], activeTagNames: []});
  }

  updateViewMode(viewMode: 'week' | 'day' | 'schedule') {
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

  toggleTag(tagName: string) {
    const names = this.currentFilters.activeTagNames;
    const activeTagNames = names.includes(tagName)
      ? names.filter(n => n !== tagName)
      : [...names, tagName];
    this.dispatch({activeTagNames});
  }

  toggleSite(siteId: number) {
    const ids = this.currentFilters.activeSiteIds;
    const activeSiteIds = ids.includes(siteId)
      ? ids.filter(id => id !== siteId)
      : [...ids, siteId];
    this.dispatch({activeSiteIds});
  }

  toggleSidebar() {
    this.dispatch({sidebarOpen: !this.currentFilters.sidebarOpen});
  }

  toggleSidebarSection(section: keyof CalendarFiltersModel['sidebarSections']) {
    const sidebarSections = {
      ...this.currentFilters.sidebarSections,
      [section]: !this.currentFilters.sidebarSections[section],
    };
    this.dispatch({sidebarSections});
  }

  private dispatch(partial: Partial<CalendarFiltersModel>) {
    this.store.dispatch(calendarUpdateFilters(partial));
  }
}
