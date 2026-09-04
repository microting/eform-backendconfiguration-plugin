import {Injectable} from '@angular/core';
import {BehaviorSubject, Observable, ReplaySubject} from 'rxjs';
import {filter as rxFilter} from 'rxjs/operators';
import {
  ComplianceReportRequestModel,
  ComplianceReportSortKey,
  ComplianceReportStatus,
} from '../../../models';

export type ComplianceMode = 'overview' | 'details' | 'report';

export type CompliancePeriodPreset = '1' | '3' | '6' | '12' | 'ytd' | 'custom';

export const COMPLIANCE_MODES: ComplianceMode[] = ['overview', 'details', 'report'];

/** Rows per page. Matches the prototype's PAGE_SIZE (compliance.js:3). */
export const COMPLIANCE_PAGE_SIZE = 10;

export interface ComplianceFilterState {
  /** null = Alle ejendomme. */
  propertyId: number | null;
  /** [] = Alle kalendere. */
  boardIds: number[];
  /** [] = Alle tags. Multi-select, OR semantics. */
  tagIds: number[];
  /** [] = Alle medarbejdere. */
  siteIds: number[];
  status: ComplianceReportStatus;
  periodPreset: CompliancePeriodPreset;
  customFrom: Date | null;
  customTo: Date | null;
}

/**
 * Prototype defaults (Compliance.html:13-54): everything "all", status
 * `Ikke udførte opgaver`, period `År til dato`. Note the calendar view mode
 * being replaced defaults its period to '1' — the prototype wins (#1163 §6).
 */
export function complianceInitialFilters(): ComplianceFilterState {
  return {
    propertyId: null,
    boardIds: [],
    tagIds: [],
    siteIds: [],
    status: 'open',
    periodPreset: 'ytd',
    customFrom: null,
    customTo: null,
  };
}

export interface CompliancePeriodBounds {
  from: Date;
  to: Date;
}

/**
 * `setMonth` overflows at month ends (31 May − 3 months → 3 March); clamp back
 * to the last day of the intended month. Carried forward verbatim from
 * `calendar-compliance-view.component.ts:131-141` — the prototype's bare
 * `setMonth` (compliance.js:477) has the bug. `months` may be negative.
 */
export function addClampedMonths(date: Date, months: number): Date {
  const d = new Date(date);
  const targetMonthIndex = d.getMonth() + months;
  d.setMonth(targetMonthIndex);
  if (d.getMonth() !== ((targetMonthIndex % 12) + 12) % 12) {
    d.setDate(0);
  }
  return d;
}

function startOfDay(date: Date): Date {
  const d = new Date(date);
  d.setHours(0, 0, 0, 0);
  return d;
}

function toIsoDate(d: Date): string {
  return `${d.getFullYear()}-${(d.getMonth() + 1).toString().padStart(2, '0')}-${d
    .getDate()
    .toString()
    .padStart(2, '0')}`;
}

/**
 * The whole state surface of the standalone Compliance page (#1163 §11).
 *
 * It exists so #1164 (Oversigt), #1165 (Detaljer) and #1167 (Rapport) can be
 * dumb about filters: they read `requestModel`, they never build it, and they
 * never recompute the period bounds — `periodBounds` is the SINGLE derivation
 * that both the displayed range and the query use (the prototype's own comment
 * at compliance.js:438-442 records what happens when there are two).
 *
 * The central contract is the blank-on-change state machine:
 *   - `setFilter()`   invalidates: page 1, showAll off, reportVisible false.
 *   - `setFilterSilently()` does NOT invalidate — it is the Angular stand-in
 *     for the prototype's "assign `.value` without dispatching `change`"
 *     bypass, used by the mode toggle and by #1164's drill-down. Getting this
 *     wrong makes the drill-down blank the page it just navigated to.
 *
 * Provided by `ComplianceReportModule`, not in root: the page's state is per
 * lazy-module instance, and nothing outside the module has any business
 * reading it.
 */
@Injectable()
export class ComplianceReportStateService {
  private filtersSubject = new BehaviorSubject<ComplianceFilterState>(complianceInitialFilters());
  private modeSubject = new BehaviorSubject<ComplianceMode>('overview');
  private reportVisibleSubject = new BehaviorSubject<boolean>(false);
  private pageSubject = new BehaviorSubject<number>(0);
  private showAllSubject = new BehaviorSubject<boolean>(false);
  private totalSubject = new BehaviorSubject<number>(0);
  private loadingSubject = new BehaviorSubject<boolean>(false);
  /**
   * REPLAYABLE, deliberately (bufferSize 1).
   *
   * The page template switches view modes with `ngSwitch`, so a mode switch
   * DESTROYS the current child and CREATES the next one. Creation — and
   * therefore the child's `fetchRequested$` subscription — happens in the
   * change-detection pass that runs AFTER the click handler that called
   * `setMode()`. With a plain `Subject` the new child subscribes after every
   * emission that could concern it and receives nothing: fetch in Oversigt,
   * switch to Detaljer, and the container renders empty while `reportVisible`
   * is true. Replaying the last trigger to a late subscriber is what makes the
   * shared trigger usable by #1164/#1165/#1167 without each of them
   * re-implementing it.
   *
   * It cannot double-fetch on the ordinary `Opdater tabel` path: a child that
   * is already subscribed gets the live emission only (a ReplaySubject replays
   * to NEW subscribers at subscribe time, not to existing ones), and
   * `requestFetch()` never re-creates the children because `reportVisible`
   * stays true.
   */
  private fetchRequestedSubject = new ReplaySubject<void>(1);

  private sortKey: ComplianceReportSortKey | null = null;
  private sortDsc = true;

  /**
   * The property the Oversigt drill-down forced, and the status value that was
   * in place before it did. Both are restored on the way back to Oversigt, and
   * both only when they still hold the value the drill-down wrote (#1163 §10.1)
   * — a user who deliberately changed either while drilled in keeps their
   * choice.
   */
  private drilledPropertyId: number | null = null;
  private preDrillStatus: ComplianceReportStatus | null = null;

  readonly filters$: Observable<ComplianceFilterState> = this.filtersSubject.asObservable();
  readonly mode$: Observable<ComplianceMode> = this.modeSubject.asObservable();
  readonly reportVisible$: Observable<boolean> = this.reportVisibleSubject.asObservable();
  readonly page$: Observable<number> = this.pageSubject.asObservable();
  readonly showAll$: Observable<boolean> = this.showAllSubject.asObservable();
  readonly total$: Observable<number> = this.totalSubject.asObservable();
  readonly loading$: Observable<boolean> = this.loadingSubject.asObservable();
  /**
   * Fires when `Opdater tabel` is pressed (or a page/sort change re-queries),
   * and replays the last such trigger to a subscriber that arrives late — see
   * `fetchRequestedSubject`.
   *
   * The `reportVisible` gate is what keeps the replay honest: `setFilter()`
   * invalidates by setting `reportVisible` false but cannot erase the buffered
   * value, so without this filter a child created while the report is hidden
   * would replay a stale trigger and fetch for a result the user just blanked.
   */
  readonly fetchRequested$: Observable<void> = this.fetchRequestedSubject
    .asObservable()
    .pipe(rxFilter(() => this.reportVisible));

  get filters(): ComplianceFilterState {
    return this.filtersSubject.value;
  }
  get mode(): ComplianceMode {
    return this.modeSubject.value;
  }
  get reportVisible(): boolean {
    return this.reportVisibleSubject.value;
  }
  get page(): number {
    return this.pageSubject.value;
  }
  get showAll(): boolean {
    return this.showAllSubject.value;
  }
  get total(): number {
    return this.totalSubject.value;
  }
  get loading(): boolean {
    return this.loadingSubject.value;
  }

  // -------------------------------------------------------------------
  // Period — one derivation, used by both the display and the request
  // -------------------------------------------------------------------

  /**
   * `null` only for an incomplete custom range, which means "no period filter"
   * and renders the period label empty (compliance.js:464-479, :481-490).
   *
   * Fixed presets and YTD are bounded ABOVE by today. This is a deliberate
   * change from the calendar view mode, which extends `dateTo` into the future
   * for open/all — a compliance report is retrospective, and a percentage that
   * counts not-yet-due tasks is what #1160's `dueTotal` rule already rejects.
   */
  get periodBounds(): CompliancePeriodBounds | null {
    const {periodPreset, customFrom, customTo} = this.filters;
    const today = startOfDay(new Date());

    if (periodPreset === 'custom') {
      if (!customFrom || !customTo) {
        return null;
      }
      return {from: startOfDay(customFrom), to: startOfDay(customTo)};
    }
    if (periodPreset === 'ytd') {
      return {from: new Date(today.getFullYear(), 0, 1), to: today};
    }
    const months = parseInt(periodPreset, 10);
    return {from: startOfDay(addClampedMonths(today, -months)), to: today};
  }

  get dateFrom(): Date | null {
    return this.periodBounds?.from ?? null;
  }

  get dateTo(): Date | null {
    return this.periodBounds?.to ?? null;
  }

  /**
   * False while a `Sæt periode` range is missing a bound or runs backwards.
   * Gates `Opdater tabel` (the prototype's modal silently `return`s instead —
   * compliance.js:2012-2027, defect 1 in #1163 §9).
   */
  get isPeriodValid(): boolean {
    const {periodPreset, customFrom, customTo} = this.filters;
    if (periodPreset !== 'custom') {
      return true;
    }
    return !!customFrom && !!customTo && startOfDay(customFrom) <= startOfDay(customTo);
  }

  // -------------------------------------------------------------------
  // The request the children issue
  // -------------------------------------------------------------------

  /**
   * `showAll` asks for the unpaged shape (`pageSize: 0`), which #1161 caps at
   * 5000 rows server-side — that cap, not the client, is what keeps "Vis alle"
   * from pulling an unbounded result set.
   */
  get requestModel(): ComplianceReportRequestModel {
    const bounds = this.periodBounds;
    const f = this.filters;
    const model: ComplianceReportRequestModel = {
      propertyId: f.propertyId,
      boardIds: [...f.boardIds],
      tagIds: [...f.tagIds],
      siteIds: [...f.siteIds],
      status: f.status,
      pageIndex: this.showAll ? 0 : this.page,
      pageSize: this.showAll ? 0 : COMPLIANCE_PAGE_SIZE,
      sort: this.sortKey,
      isSortDsc: this.sortDsc,
    };
    // `periodBounds` is null for exactly one input: an INCOMPLETE `Sæt periode`
    // range, which #1163 defines as "no period filter". The keys are OMITTED
    // rather than filled with today — substituting today fabricated a one-day
    // window that looks like a legitimate result. `requestFetch()` still
    // refuses to fire in this state, but `requestModel` is a public getter the
    // children read directly and #1169's export path reads it outside that
    // gate, so the shape has to be honest on its own.
    //
    // Omitting is what the server's `DateTime` (non-nullable) can represent:
    // an absent key deserialises to `default(DateTime)`, so the query bounds
    // collapse and the result is EMPTY — visibly nothing, rather than a
    // plausible-looking day of rows. Emitting `null` instead would be a 400.
    if (bounds) {
      model.dateFrom = toIsoDate(bounds.from);
      model.dateTo = toIsoDate(bounds.to);
    }
    return model;
  }

  // -------------------------------------------------------------------
  // The blank-on-change state machine (#1163 §5)
  // -------------------------------------------------------------------

  /**
   * The invalidating path. Every one of the seven filter controls goes through
   * here: reset to page 1, drop "show all", hide the report (which blanks the
   * container back to `Vælg filtre og klik Opdater tabel.` and clears the
   * pagination) — and issue NO request. Only `requestFetch()` fetches.
   */
  setFilter(patch: Partial<ComplianceFilterState>): void {
    this.filtersSubject.next({...this.filters, ...patch});
    this.pageSubject.next(0);
    this.showAllSubject.next(false);
    this.totalSubject.next(0);
    this.reportVisibleSubject.next(false);
    // `loading` belongs to the child that is about to be UNMOUNTED by
    // `reportVisible` going false. A child torn down mid-flight cannot be
    // relied on to run a `finalize`/complete path, so nothing would ever call
    // `setLoading(false)` again — and `canFetch` is
    // `isPeriodValid && !loading`, so `Opdater tabel` would be dead until a
    // reload. The shell owns both this call and the unmounting, so the reset
    // belongs here, next to the `total` reset it was already inconsistent
    // with.
    this.loadingSubject.next(false);
  }

  /**
   * The bypass path. Updates filter values WITHOUT invalidating, so an already
   * fetched result survives. Used by the mode toggle's drill-down unwind and by
   * #1164's `drillIntoProperty`. Never call it from a template's
   * `(ngModelChange)` — a user-driven change must invalidate.
   */
  setFilterSilently(patch: Partial<ComplianceFilterState>): void {
    this.filtersSubject.next({...this.filters, ...patch});
  }

  /**
   * Mode switches deliberately preserve `reportVisible` (compliance.js:1516-1545
   * never calls onFilterChange), so a user fetches once and then flips between
   * Oversigt / Detaljer / Rapport freely.
   */
  setMode(mode: ComplianceMode): void {
    const next = COMPLIANCE_MODES.indexOf(mode) !== -1 ? mode : 'overview';
    this.modeSubject.next(next);
    this.pageSubject.next(0);
    this.showAllSubject.next(false);
    // `total` is per-VIEW, not per-filter: Oversigt counts one row per
    // property, Detaljer one per task. Carrying it across a mode switch does
    // not merely show a stale number, it shows one that is wrong by an order
    // of magnitude — `Viser 1-10 af <previous mode's total>` plus the previous
    // mode's page-button window, drawn the instant the toggle is clicked and
    // left standing until the new child calls `setTotalCount`. Reset it so the
    // chrome shows `Ingen resultater` until the new child reports in.
    this.totalSubject.next(0);
    // Same reasoning as `setFilter`: the ngSwitch destroys the outgoing child,
    // whose in-flight request may never reach a `setLoading(false)`.
    // `reportVisible` deliberately stays true here, so the incoming child
    // mounts, receives the replayed trigger and sets `loading` itself.
    this.loadingSubject.next(false);

    if (next === 'overview' && this.drilledPropertyId !== null) {
      const patch: Partial<ComplianceFilterState> = {};
      if (this.filters.propertyId === this.drilledPropertyId) {
        patch.propertyId = null;
      }
      // The drill-down forces 'all'; restore only if it is still 'all'.
      if (this.filters.status === 'all' && this.preDrillStatus !== null) {
        patch.status = this.preDrillStatus;
      }
      if (Object.keys(patch).length > 0) {
        this.setFilterSilently(patch);
      }
      this.drilledPropertyId = null;
      this.preDrillStatus = null;
    }
  }

  /**
   * Oversigt → Detaljer for one property (#1164). Forces status to `all`
   * because Oversigt counts done and not-done together, and a drill-down that
   * showed only the open subset would not add up to the number just clicked.
   * Both writes are silent, so the result already on screen survives.
   */
  drillIntoProperty(propertyId: number): void {
    // Capture the pre-drill status ONLY when no drill is already in effect.
    // A second drill without an intervening Oversigt visit would otherwise
    // record the 'all' that the FIRST drill forced, and the unwind would
    // restore 'all' instead of the user's own choice. Not reachable through
    // today's UI (drilling requires being in Oversigt, which unwinds on the
    // way in), but #1164 builds directly on this method.
    if (this.drilledPropertyId === null) {
      this.preDrillStatus = this.filters.status;
    }
    this.drilledPropertyId = propertyId;
    this.setFilterSilently({propertyId, status: 'all'});
    this.setMode('details');
  }

  /** Test/diagnostic accessor — the drill-down is otherwise opaque. */
  get drilledProperty(): number | null {
    return this.drilledPropertyId;
  }

  // -------------------------------------------------------------------
  // Fetching, paging, sorting
  // -------------------------------------------------------------------

  /**
   * Called once per VISIT, from the page component's `ngOnInit` (#1163 §6).
   *
   * The service is provided by the lazy `ComplianceReportModule`, and Angular
   * caches a lazy `NgModuleRef` for the lifetime of the app — so navigating
   * away from the page and back re-creates the components but reuses THIS
   * instance, `ReplaySubject` buffer and all. Without this method, re-entering
   * while the preserved mode is `details` or `report` would find
   * `reportVisible` still true: the container renders, the child mounts, the
   * buffered trigger replays past the `reportVisible` gate, and an unbounded
   * row query fires with no user gesture at all.
   *
   * So entry has exactly two shapes:
   *  - `overview`: auto-fetch once. One cheap server-side aggregation per
   *    property (#1162), and the prototype records the auto-fetch as a design
   *    choice (compliance.js:2371-2372).
   *  - `details` / `report`: force the page back to its un-fetched state.
   *    `reportVisible` false both shows the placeholder AND closes
   *    `fetchRequested$`'s gate, which is what actually neutralises the
   *    buffered trigger — the buffer itself cannot be erased. `total`/`page`
   *    are cleared so the pagination chrome does not draw the previous visit's
   *    `Viser 1-10 af N` before any new response lands.
   *
   * This is deliberately NOT wired into `setMode`: a mode switch WITHIN a
   * visit must keep replaying, or the recreated child renders nothing.
   */
  enterPage(): void {
    if (this.mode === 'overview') {
      this.requestFetch();
      return;
    }
    this.reportVisibleSubject.next(false);
    this.pageSubject.next(0);
    this.showAllSubject.next(false);
    this.totalSubject.next(0);
    this.loadingSubject.next(false);
  }

  /** `Opdater tabel`. The only user gesture that fetches. */
  requestFetch(): void {
    if (!this.isPeriodValid) {
      return;
    }
    this.pageSubject.next(0);
    this.showAllSubject.next(false);
    this.reportVisibleSubject.next(true);
    this.fetchRequestedSubject.next();
  }

  setPage(pageIndex: number): void {
    // Defence in depth: the pagination is empty while the report is hidden, and
    // the prototype guards the same way (compliance.js:1945).
    if (!this.reportVisible) {
      return;
    }
    this.showAllSubject.next(false);
    this.pageSubject.next(Math.max(0, pageIndex));
    this.fetchRequestedSubject.next();
  }

  setShowAll(): void {
    if (!this.reportVisible) {
      return;
    }
    this.showAllSubject.next(true);
    this.pageSubject.next(0);
    this.fetchRequestedSubject.next();
  }

  /** Sorting does not invalidate — it re-queries the same filtered set. */
  setSort(sort: ComplianceReportSortKey | null, isSortDsc: boolean): void {
    this.sortKey = sort;
    this.sortDsc = isSortDsc;
    if (!this.reportVisible) {
      return;
    }
    this.pageSubject.next(0);
    this.fetchRequestedSubject.next();
  }

  get sort(): ComplianceReportSortKey | null {
    return this.sortKey;
  }

  get isSortDsc(): boolean {
    return this.sortDsc;
  }

  /** Children report their total back so the shell can draw the pagination. */
  setTotalCount(total: number): void {
    this.totalSubject.next(Math.max(0, total ?? 0));
  }

  /** Children report in-flight state so the shell can disable `Opdater tabel`. */
  setLoading(loading: boolean): void {
    this.loadingSubject.next(loading);
  }

  // -------------------------------------------------------------------
  // Pagination chrome maths (the shell draws it, the child feeds it)
  // -------------------------------------------------------------------

  get totalPages(): number {
    return Math.max(1, Math.ceil(this.total / COMPLIANCE_PAGE_SIZE));
  }

  /**
   * Windowed page buttons: first / … / five around current / … / last, once
   * there are more than nine pages. Carried forward from
   * `calendar-compliance-view.component.ts:161-172` — the prototype renders one
   * button per page unbounded (compliance.js:1912-1917), which is 300 buttons
   * at 3000 rows.
   */
  get pageNumbers(): (number | 'gap')[] {
    const total = this.totalPages;
    if (total <= 9) {
      return Array.from({length: total}, (_, i) => i);
    }
    const current = this.page;
    const around = [current - 2, current - 1, current, current + 1, current + 2].filter(
      (i) => i > 0 && i < total - 1
    );
    const result: (number | 'gap')[] = [0];
    if (around.length === 0 || around[0] > 1) {
      result.push('gap');
    }
    result.push(...around);
    if (around.length === 0 || around[around.length - 1] < total - 2) {
      result.push('gap');
    }
    result.push(total - 1);
    return result;
  }

  get showingFrom(): number {
    if (this.total === 0) {
      return 0;
    }
    return this.showAll ? 1 : this.page * COMPLIANCE_PAGE_SIZE + 1;
  }

  get showingTo(): number {
    return this.showAll
      ? this.total
      : Math.min((this.page + 1) * COMPLIANCE_PAGE_SIZE, this.total);
  }
}
