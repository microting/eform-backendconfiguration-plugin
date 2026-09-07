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

/**
 * How long a filter change waits before it re-queries (#1185 decision D).
 * The tag control is a `closeOnSelect=false` multi-select where every click is
 * one `ngModelChange`; without this, ticking three tags is three requests.
 * Only the FILTER path waits — `Opdater periode`, the mode toggle, paging and
 * sorting fetch immediately.
 */
export const COMPLIANCE_FILTER_DEBOUNCE_MS = 300;

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
  /**
   * The COMMITTED custom range — what `periodBounds` and the request use.
   * The date pickers edit a separate draft (`customDraftFrom`/`customDraftTo`)
   * that only becomes this on `commitCustomPeriod()` (#1185 decision C).
   * Both null until a range has been committed.
   */
  customFrom: Date | null;
  customTo: Date | null;
}

/**
 * Prototype defaults (Compliance.html:13-54): everything "all", status
 * `Ikke udførte opgaver`, period `År til dato`. Note the calendar view mode
 * being replaced defaults its period to '1' — the prototype wins (#1163 §6).
 * These are also what `resetToOverview()` restores (#1185).
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

function isOrderedRange(from: Date | null, to: Date | null): boolean {
  return !!from && !!to && startOfDay(from) <= startOfDay(to);
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
 * The central contract is the auto-fetch state machine (#1185, which reversed
 * #1163 §5's blank-on-change rule at the customer's request):
 *   - `setFilter()`   re-queries the ACTIVE mode after a short debounce,
 *     WITHOUT blanking first — the result on screen stays until the new one
 *     lands. The one exception is `Sæt periode`: choosing it, and editing its
 *     dates, only STAGES; the container shows the placeholder until
 *     `commitCustomPeriod()` (the `Opdater periode` button) fetches.
 *   - `setFilterSilently()` neither blanks nor fetches — it is the Angular
 *     stand-in for the prototype's "assign `.value` without dispatching
 *     `change`" bypass, used by #1164's drill-down. Getting this wrong makes
 *     the drill-down re-query the page it just navigated to.
 *   - `resetToOverview()` is what pressing `Oversigt` does, from anywhere:
 *     every filter back to its default, then one Oversigt fetch.
 *
 * Known limitation (#1185 decision E): clicking the sidebar entry while
 * already on the page is a router no-op (`onSameUrlNavigation: 'ignore'` in
 * the core `app.routing.ts`), so it neither resets nor re-fetches; only the
 * `Oversigt` mode button does.
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
   * It cannot double-fetch on the ordinary filter-change path: a child that
   * is already subscribed gets the live emission only (a ReplaySubject replays
   * to NEW subscribers at subscribe time, not to existing ones), and
   * `requestFetch()` never re-creates the children because `reportVisible`
   * stays true.
   */
  private fetchRequestedSubject = new ReplaySubject<void>(1);

  private sortKey: ComplianceReportSortKey | null = null;
  private sortDsc = true;

  /**
   * The STAGED custom range (#1185 decision C). The date pickers write here;
   * nothing reads it for a query. `commitCustomPeriod()` copies it into
   * `filters.customFrom/customTo`, which is what `periodBounds` derives from.
   */
  private draftCustomFrom: Date | null = null;
  private draftCustomTo: Date | null = null;

  /** The pending debounced filter fetch, if any. See `scheduleFetch()`. */
  private pendingFilterFetch: ReturnType<typeof setTimeout> | null = null;

  readonly filters$: Observable<ComplianceFilterState> = this.filtersSubject.asObservable();
  readonly mode$: Observable<ComplianceMode> = this.modeSubject.asObservable();
  readonly reportVisible$: Observable<boolean> = this.reportVisibleSubject.asObservable();
  readonly page$: Observable<number> = this.pageSubject.asObservable();
  readonly showAll$: Observable<boolean> = this.showAllSubject.asObservable();
  readonly total$: Observable<number> = this.totalSubject.asObservable();
  readonly loading$: Observable<boolean> = this.loadingSubject.asObservable();
  /**
   * Fires whenever the active view must (re-)query: a debounced filter change,
   * `Opdater periode`, the `Oversigt` reset, page entry, or a page/sort change.
   * It replays the last such trigger to a subscriber that arrives late — see
   * `fetchRequestedSubject`.
   *
   * The `reportVisible` gate is what keeps the replay honest: staging a
   * `Sæt periode` range (and `enterPage()` in Detaljer/Rapport) hides the
   * report but cannot erase the buffered value, so without this filter a child
   * created while the report is hidden would replay a stale trigger and fetch
   * for a result the user cannot see yet.
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
   * `null` only for an incomplete COMMITTED custom range — i.e. `Sæt periode`
   * chosen but `Opdater periode` not yet pressed — which means "no period
   * filter" and renders the period label empty (compliance.js:464-479,
   * :481-490). The draft the pickers are editing never shows here.
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

  /** What the `Sæt periode` date pickers show and edit. Staged, not queried. */
  get customDraftFrom(): Date | null {
    return this.draftCustomFrom;
  }

  get customDraftTo(): Date | null {
    return this.draftCustomTo;
  }

  /**
   * Validity of the range the user is EDITING: false while a `Sæt periode`
   * draft is missing a bound or runs backwards. Gates `Opdater periode` and
   * drives `#compliancePeriodError` (the prototype's modal silently `return`s
   * instead — compliance.js:2012-2027, defect 1 in #1163 §9). Always true for
   * a fixed preset.
   */
  get isPeriodValid(): boolean {
    if (this.filters.periodPreset !== 'custom') {
      return true;
    }
    return isOrderedRange(this.draftCustomFrom, this.draftCustomTo);
  }

  /**
   * Validity of the range a query would USE: the committed one. False only in
   * custom mode before a range has been committed (or with a committed range
   * that is incomplete/backwards, reachable through `setFilter` only). This is
   * what gates every fetch; `isPeriodValid` gates the commit.
   */
  get isCommittedPeriodValid(): boolean {
    const {periodPreset, customFrom, customTo} = this.filters;
    if (periodPreset !== 'custom') {
      return true;
    }
    return isOrderedRange(customFrom, customTo);
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
    // `periodBounds` is null for exactly one input: an INCOMPLETE committed
    // `Sæt periode` range, which #1163 defines as "no period filter". The keys
    // are OMITTED rather than filled with today — substituting today
    // fabricated a one-day window that looks like a legitimate result.
    // `requestFetch()` still refuses to fire in this state, but `requestModel`
    // is a public getter the children read directly and #1169's export path
    // reads it outside that gate, so the shape has to be honest on its own.
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
  // The auto-fetch state machine (#1185)
  // -------------------------------------------------------------------

  /**
   * The user-driven path. Every one of the filter controls goes through here:
   * apply the patch, reset to page 1, drop "show all" — and then either
   *
   *  - re-query the active mode after `COMPLIANCE_FILTER_DEBOUNCE_MS`, leaving
   *    `reportVisible` TRUE so the mounted child (whose `switchMap` cancels
   *    any in-flight request) keeps the old rows on screen until the new ones
   *    land. `total` and `loading` are left alone for the same reason: the
   *    pagination chrome and the spinner belong to the child that is still
   *    mounted, and it re-reports both; or
   *  - when the committed period cannot be queried — i.e. `Sæt periode` is
   *    selected and no range has been committed yet — blank to the placeholder
   *    and fetch NOTHING until `commitCustomPeriod()`. That is the one gesture
   *    that still waits for a button (#1185 decision C).
   *
   * Switching the preset TO `custom` deliberately clears the committed range:
   * the previously committed dates would otherwise be queried by the next
   * filter change while the user is still typing new ones. The draft keeps
   * whatever it held, so re-picking `Sæt periode` offers the last range again.
   * A patch that carries `customFrom`/`customTo` writes them as COMMITTED (and
   * mirrors them into the draft) — that is the programmatic shape the specs
   * use; the filter bar itself goes through `stageCustomPeriod()`.
   */
  setFilter(patch: Partial<ComplianceFilterState>): void {
    const prev = this.filters;
    const next: ComplianceFilterState = {...prev, ...patch};
    const patchHasDates = 'customFrom' in patch || 'customTo' in patch;
    if (next.periodPreset === 'custom' && prev.periodPreset !== 'custom' && !patchHasDates) {
      next.customFrom = null;
      next.customTo = null;
    }
    if (patchHasDates) {
      this.draftCustomFrom = next.customFrom;
      this.draftCustomTo = next.customTo;
    }
    this.filtersSubject.next(next);
    this.pageSubject.next(0);
    this.showAllSubject.next(false);

    if (!this.isCommittedPeriodValid) {
      this.blankUntilCommit();
      return;
    }
    this.scheduleFetch();
  }

  /**
   * The bypass path. Updates filter values WITHOUT fetching or blanking, so an
   * already fetched result survives. Used by #1164's `drillIntoProperty`,
   * whose mode switch then re-queries through the replay. Never call it from
   * a template's `(ngModelChange)` — a user-driven change must re-query.
   */
  setFilterSilently(patch: Partial<ComplianceFilterState>): void {
    this.filtersSubject.next({...this.filters, ...patch});
  }

  /**
   * `Sæt periode` date-picker writes. Staged only: no query, no blank, no
   * change to what `periodBounds` reports. `undefined` leaves a bound as it
   * was; `null` clears it.
   */
  stageCustomPeriod(draft: {from?: Date | null; to?: Date | null}): void {
    if (draft.from !== undefined) {
      this.draftCustomFrom = draft.from;
    }
    if (draft.to !== undefined) {
      this.draftCustomTo = draft.to;
    }
  }

  /**
   * `Opdater periode`. Copies the staged range into the committed one and
   * fetches — immediately, no debounce; a button click is one gesture. A no-op
   * outside custom mode or while the draft is invalid (the button is disabled
   * in both states, this is the belt to that brace).
   */
  commitCustomPeriod(): void {
    if (this.filters.periodPreset !== 'custom' || !this.isPeriodValid) {
      return;
    }
    this.filtersSubject.next({
      ...this.filters,
      customFrom: this.draftCustomFrom,
      customTo: this.draftCustomTo,
    });
    this.requestFetch();
  }

  /**
   * A plain view-mode switch: Oversigt ↔ Detaljer ↔ Rapport with the filters
   * and `reportVisible` untouched (compliance.js:1516-1545 never calls
   * onFilterChange), so the child the `ngSwitch` creates re-queries the SAME
   * filters through the replay. Pressing the `Oversigt` button is NOT this —
   * it is `resetToOverview()`.
   */
  setMode(mode: ComplianceMode): void {
    // A filter change followed within the debounce by a mode switch is ONE
    // gesture's worth of requests, not two — but only while the report is
    // VISIBLE: then the switch recreates the child, whose late subscription
    // replays the last trigger against `requestModel` as it stands at fetch
    // time, the new filters included, so the pending filter fetch would only
    // issue the same query twice. While the report is HIDDEN (the B1 re-entry
    // state: `enterPage()` in Detaljer/Rapport, no child mounted, the gate on
    // `fetchRequested$` closed) nothing replays, and the pending timer's
    // `requestFetch()` is the one thing that will un-hide the report. It must
    // survive the mode switch, or a filter change made from the placeholder
    // and followed by a mode click is swallowed outright.
    if (this.reportVisible) {
      this.cancelScheduledFetch();
    }
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
    // The ngSwitch destroys the outgoing child, whose in-flight request may
    // never reach a `setLoading(false)`. `reportVisible` deliberately stays
    // true here, so the incoming child mounts, receives the replayed trigger
    // and sets `loading` itself.
    this.loadingSubject.next(false);
  }

  /**
   * Oversigt → Detaljer for one property (#1164). The property is written
   * silently, so the result already on screen survives until the Detaljer
   * child re-queries through the replay; the status is NOT touched (#1185):
   * Oversigt's percentage is built on `Ikke udførte opgaver`, and the customer
   * wants the drill-down to list exactly those, not `Alle opgaver`. The rows
   * will therefore not add up to the row's `dueTotal` — that is intended, do
   * not re-add `status: 'all'` to make the numbers match.
   *
   * There is no bookkeeping to unwind on the way back: pressing `Oversigt` is
   * `resetToOverview()`, which restores every filter regardless of who wrote
   * it.
   */
  drillIntoProperty(propertyId: number): void {
    this.setFilterSilently({propertyId});
    this.setMode('details');
  }

  /**
   * What pressing `Oversigt` does, from Detaljer, from Rapport, or while
   * already in Oversigt (#1185): every filter back to `complianceInitialFilters()`
   * — Alle ejendomme / Alle kalendere / Alle tags / Ikke udførte opgaver /
   * Alle medarbejdere / År til dato — any staged or committed custom range and
   * the sort dropped, mode `overview`, then ONE Oversigt fetch.
   *
   * The fetch fires while the outgoing Detaljer/Rapport child is still
   * subscribed (the `ngSwitch` swap happens on the next change-detection
   * pass); each child guards its pipeline on `state.mode === <own>`, so that
   * child drops the trigger and only the incoming Oversigt child queries.
   */
  resetToOverview(): void {
    this.cancelScheduledFetch();
    this.filtersSubject.next(complianceInitialFilters());
    this.draftCustomFrom = null;
    this.draftCustomTo = null;
    this.sortKey = null;
    this.sortDsc = true;
    this.setMode('overview');
    this.requestFetch();
  }

  // -------------------------------------------------------------------
  // Fetching, paging, sorting
  // -------------------------------------------------------------------

  /**
   * Called once per VISIT, from the page component's `ngOnInit` (#1163 §6,
   * kept by #1185 decision B1: entering the page is not "pressing Oversigt").
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
   *  - `details` / `report`: force the page back to its un-fetched state with
   *    the previous visit's filters and mode preserved. `reportVisible` false
   *    both shows the placeholder AND closes `fetchRequested$`'s gate, which
   *    is what actually neutralises the buffered trigger — the buffer itself
   *    cannot be erased. `total`/`page` are cleared so the pagination chrome
   *    does not draw the previous visit's `Viser 1-10 af N` before any new
   *    response lands. From there, any filter change re-queries (it goes
   *    through `setFilter`, which fetches), and `Oversigt` resets.
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

  /**
   * Fetch NOW for the current filters: `Opdater periode`, the `Oversigt`
   * reset, page entry, and the tail of a debounced filter change all end here.
   * Supersedes any filter fetch still waiting on its debounce — one gesture,
   * one request. Refuses while the committed period cannot be queried.
   */
  requestFetch(): void {
    this.cancelScheduledFetch();
    if (!this.isCommittedPeriodValid) {
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
    // The immediate fetch below already reads the new filters; a filter fetch
    // still waiting on its debounce would only issue the same query again.
    // Cancelled AFTER the gate: while hidden nothing fetches here, so the
    // pending filter fetch stays the one thing that un-hides the report.
    this.cancelScheduledFetch();
    this.showAllSubject.next(false);
    this.pageSubject.next(Math.max(0, pageIndex));
    this.fetchRequestedSubject.next();
  }

  setShowAll(): void {
    if (!this.reportVisible) {
      return;
    }
    // Same as setPage: the immediate fetch supersedes a pending filter fetch.
    this.cancelScheduledFetch();
    this.showAllSubject.next(true);
    this.pageSubject.next(0);
    this.fetchRequestedSubject.next();
  }

  /** Sorting re-queries the same filtered set, immediately. */
  setSort(sort: ComplianceReportSortKey | null, isSortDsc: boolean): void {
    this.sortKey = sort;
    this.sortDsc = isSortDsc;
    if (!this.reportVisible) {
      return;
    }
    // Same as setPage: the immediate fetch supersedes a pending filter fetch.
    this.cancelScheduledFetch();
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

  /**
   * Children report in-flight state so the shell can show the spinner and
   * disable `Opdater periode`.
   */
  setLoading(loading: boolean): void {
    this.loadingSubject.next(loading);
  }

  // -------------------------------------------------------------------
  // Debounced filter fetch
  // -------------------------------------------------------------------

  /**
   * A `setTimeout` rather than `debounceTime` so that it can be CANCELLED by
   * whatever fetches or blanks in the meantime: a tag click followed within
   * 300 ms by `Oversigt` must produce the reset's one request, not two, and
   * a tag click followed by `Sæt periode` must not un-blank the placeholder
   * 300 ms later.
   */
  private scheduleFetch(): void {
    this.cancelScheduledFetch();
    this.pendingFilterFetch = setTimeout(() => {
      this.pendingFilterFetch = null;
      this.requestFetch();
    }, COMPLIANCE_FILTER_DEBOUNCE_MS);
  }

  private cancelScheduledFetch(): void {
    if (this.pendingFilterFetch !== null) {
      clearTimeout(this.pendingFilterFetch);
      this.pendingFilterFetch = null;
    }
  }

  /**
   * The placeholder state: `Sæt periode` is selected and nothing is committed.
   * `reportVisible` false unmounts the child, which owns `loading` and may be
   * torn down mid-flight without ever reaching `setLoading(false)` — and
   * `canFetch` is `isPeriodValid && !loading`, so a stuck `true` would leave
   * `Opdater periode` dead. The shell owns both the unmount and this reset.
   */
  private blankUntilCommit(): void {
    this.cancelScheduledFetch();
    this.reportVisibleSubject.next(false);
    this.totalSubject.next(0);
    this.loadingSubject.next(false);
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
