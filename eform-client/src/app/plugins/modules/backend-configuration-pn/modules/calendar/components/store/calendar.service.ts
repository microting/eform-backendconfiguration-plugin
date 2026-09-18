import {Injectable} from '@angular/core';
import {Store} from '@ngrx/store';
import {withLatestFrom} from 'rxjs/operators';
import {selectAuthUser} from 'src/app/state/auth/auth.selector';
import {
  calendarUpdateFilters,
  CalendarFiltersModel,
  createCalendarInitialState,
  selectCalendarActiveBoardIds,
  selectCalendarActiveSiteIds,
  selectCalendarActiveTeamIds,
  selectCalendarActiveTagNames,
  selectCalendarCurrentDate,
  selectCalendarFilters,
  selectCalendarPropertyId,
  selectCalendarViewMode,
} from '../../../../state';
import {readSavedCalendarFilters, writeSavedCalendarFilters} from './calendar-filters.storage';

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

  /**
   * The user the in-memory filters belong to (#1303), set by
   * restoreSavedFilters(). Null until the calendar has been opened once in this
   * app instance. Nothing is saved until it is known, and nothing is saved
   * while it differs from the signed-in user, so one user's selections are
   * never written under another user's key.
   */
  private filtersOwnerUserId: number | null = null;

  constructor(private store: Store) {
    this.filters$.subscribe(f => this.currentFilters = f);
    // #1303: remember the last property/calendars/workers/week per user. Only
    // filter changes trigger a save (never an auth change on its own), and the
    // untouched initial state (no property) is never written, so the app start
    // and a logout reset cannot overwrite what the user saved.
    this.filters$.pipe(withLatestFrom(this.store.select(selectAuthUser)))
      .subscribe(([filters, user]) => {
        const userId = (user as {id?: number} | null | undefined)?.id;
        if (!filters || filters.propertyId == null || !userId) return;
        if (this.filtersOwnerUserId !== userId) return;
        writeSavedCalendarFilters(userId, filters);
      });
  }

  /**
   * #1303: called once per calendar visit, with the signed-in user's id, BEFORE
   * the property list loads. When the in-memory filters are empty (first visit
   * since the page loaded, or since a logout) the user's saved settings are
   * written through the restore path; the calendar then validates them against
   * the fresh property/calendar/worker lists. When the in-memory filters already
   * belong to this user (re-entry inside the SPA, #1292) they are kept as they
   * are. If they belong to someone else (the signed-in user changed without a
   * logout in this tab) they are reset first, so nothing leaks across users.
   */
  restoreSavedFilters(userId: number) {
    if (!userId) return;
    const hasInMemory = this.currentFilters?.propertyId != null;
    const ownedByOther = this.filtersOwnerUserId !== null && this.filtersOwnerUserId !== userId;
    if (hasInMemory && !ownedByOther) {
      this.filtersOwnerUserId = userId;
      return;
    }
    if (hasInMemory && ownedByOther) {
      this.dispatch({...createCalendarInitialState().filters});
    }
    // Set BEFORE the dispatch below: the save subscription runs synchronously
    // on it and must already see the new owner.
    this.filtersOwnerUserId = userId;
    const saved = readSavedCalendarFilters(userId);
    if (saved) {
      this.restoreFilters(saved);
    }
  }

  updatePropertyId(propertyId: number | null) {
    this.dispatch({propertyId, activeBoardIds: [], activeSiteIds: [], activeTeamIds: [], activeTagNames: []});
  }

  /**
   * The restore path (#1292): writes a property together with the selections
   * that belong to it in ONE dispatch, WITHOUT the clearing `updatePropertyId`
   * does. For re-applying a known-good (or about-to-be-validated) filter set —
   * re-entering the calendar within the SPA, restoring saved settings, or
   * narrowing stored selections to what still exists — never for a user
   * picking a different property, which must go through `updatePropertyId`.
   *
   * Only the fields passed are written; anything omitted keeps its value.
   * `viewMode` is deliberately not part of the restore surface.
   */
  restoreFilters(filters: Partial<Omit<CalendarFiltersModel, 'viewMode'>>) {
    this.dispatch({...filters});
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
   * survive it. `updatePropertyId` remains the only thing that clears everything
   * (apart from logout, which resets the whole calendar state in the reducer).
   */
  clearAssignees() {
    this.dispatch({activeSiteIds: [], activeTeamIds: []});
  }

  private dispatch(partial: Partial<CalendarFiltersModel>) {
    this.store.dispatch(calendarUpdateFilters(partial));
  }
}
