import {Subject, of} from 'rxjs';
import {ComplianceReportStateService} from '../../store';
import {ComplianceOverviewViewComponent} from './compliance-overview-view.component';

/**
 * The single most load-bearing guarantee in #1164, and the one nothing else on
 * the client asserted: `buildRequest()` sends #1162's aggregation body and
 * NOTHING else.
 *
 * `POST compliance-report/overview` binds `ComplianceReportOverviewRequestModel`,
 * which has no `Status`, no `PageIndex`/`PageSize` and no `Sort`/`IsSortDsc`. The
 * shell's `requestModel` carries all five — it is shaped for the PAGED endpoint
 * that Detaljer and Rapport use — so this view has to reduce it, and it does so
 * field by field rather than by spreading and deleting. A future field added to
 * the paged model must not leak through, and `status` must stay off the wire even
 * though the (disabled) status control still holds a value. Nothing on screen
 * would show it if either broke: the server would silently ignore the extra keys
 * and the numbers would look right.
 *
 * Constructed directly rather than through a TestBed — the class takes only two
 * injectables and touches no DOM in these paths, matching the pattern in
 * `compliance-report-state.service.spec.ts` and the kanban component specs. The
 * state service is the REAL one (it has no dependencies), so what is asserted is
 * the actual reduction of the actual `requestModel`, not of a hand-written stub
 * that could drift from it.
 */
describe('ComplianceOverviewViewComponent — buildRequest', () => {
  let state: ComplianceReportStateService;
  let component: ComplianceOverviewViewComponent;

  /** `buildRequest` is private on purpose; the guarantee is still public. */
  const buildRequest = (): Record<string, unknown> =>
    (component as any).buildRequest() as Record<string, unknown>;

  const FORBIDDEN = ['status', 'pageIndex', 'pageSize', 'sort', 'isSortDsc'];

  beforeEach(() => {
    state = new ComplianceReportStateService();
    // `overview()` is never reached: no test here subscribes to
    // `fetchRequested$`, so `ngOnInit` is deliberately not called.
    const service = {overview: jest.fn()};
    component = new ComplianceOverviewViewComponent(state, service as any);
  });

  it('sends exactly the aggregation body and nothing else', () => {
    // Default period is `ytd`, which always yields bounds, so both date keys
    // are present — six keys, no seventh.
    expect(Object.keys(buildRequest()).sort()).toEqual([
      'boardIds',
      'dateFrom',
      'dateTo',
      'propertyId',
      'siteIds',
      'tagIds',
    ]);
  });

  it('omits status, paging and sort — the four parameters #1162 does not have', () => {
    // Set every one of them on the shared state FIRST, so this cannot pass by
    // the paged model happening to be empty.
    state.setFilter({status: 'done'});
    state.requestFetch();
    state.setPage(3);
    state.setSort('taskDate', true);

    const paged = state.requestModel as unknown as Record<string, unknown>;
    // The premise: the shared model really does carry all five.
    for (const key of FORBIDDEN) {
      expect(Object.keys(paged)).toContain(key);
    }

    const request = buildRequest();
    for (const key of FORBIDDEN) {
      expect(Object.keys(request)).not.toContain(key);
    }
    // Belt and braces: absent, not present-and-undefined. `undefined` would be
    // dropped by JSON.stringify today but would still read as "we send it".
    expect('status' in request).toBe(false);
    expect('pageIndex' in request).toBe(false);
    expect('pageSize' in request).toBe(false);
    expect('sort' in request).toBe(false);
    expect('isSortDsc' in request).toBe(false);
  });

  it('copies the filter values through unchanged', () => {
    state.setFilter({propertyId: 7, boardIds: [2, 3], tagIds: [9], siteIds: [4, 5]});

    const request = buildRequest();

    expect(request.propertyId).toBe(7);
    expect(request.boardIds).toEqual([2, 3]);
    expect(request.tagIds).toEqual([9]);
    expect(request.siteIds).toEqual([4, 5]);
  });

  it('reads the state AT CALL TIME, never a cached copy', () => {
    const before = buildRequest();
    expect(before.propertyId).toBeNull();

    state.setFilter({propertyId: 11});

    expect(buildRequest().propertyId).toBe(11);
  });

  it('omits both date keys for an incomplete Sæt periode range', () => {
    // The one input for which `periodBounds` is null. "No period filter" is
    // expressed by ABSENT keys, never by today substituted for a missing bound.
    state.setFilter({periodPreset: 'custom', customFrom: new Date(2026, 0, 5), customTo: null});

    expect(Object.keys(buildRequest()).sort()).toEqual([
      'boardIds',
      'propertyId',
      'siteIds',
      'tagIds',
    ]);
  });

  it('sends both date keys for a complete range', () => {
    state.setFilter({
      periodPreset: 'custom',
      customFrom: new Date(2026, 0, 5),
      customTo: new Date(2026, 1, 9),
    });

    const request = buildRequest();

    expect(request.dateFrom).toBe('2026-01-05');
    expect(request.dateTo).toBe('2026-02-09');
  });
});

/**
 * The count Oversigt reports back, and it has a live reader: the filter bar's
 * `canDownload` is `!!exportFormat && state.reportVisible && state.total > 0`
 * (compliance-report-filters.component.ts:337) and gates `#complianceDownloadBtn`.
 * A wrong count here — off by the totals row, say — silently disables Download
 * on an empty-but-not-really result, which is why the exact number is pinned.
 * (The pagination <nav> is NOT a reader: the shell hides it outside Detaljer.)
 */
describe('ComplianceOverviewViewComponent — the count it reports back', () => {
  it('reports the ROW count, which excludes the totals row', () => {
    const state = new ComplianceReportStateService();
    const component = new ComplianceOverviewViewComponent(state, {overview: jest.fn()} as any);
    const row = (propertyId: number) => ({
      propertyId,
      propertyName: `Ejendom ${propertyId}`,
      total: 1,
      done: 0,
      overdue: 0,
      dueTotal: 1,
      dueDone: 0,
      compliancePct: 50,
    });

    (component as any).applyResponse({
      rows: [row(1), row(2), row(3)],
      totals: {...row(0), propertyName: null},
    });

    expect(state.total).toBe(3);
  });
});

/**
 * The view-mode guard at the head of the fetch pipeline (#1185, PR #1202):
 * `rxFilter(() => this.state.mode === 'overview')`.
 *
 * `fetchRequested$` is ONE stream shared by all three children, and the shell
 * swaps them with an `ngSwitch` — i.e. on the change-detection pass AFTER the
 * click handler that called `setMode()`. So a trigger emitted inside that
 * handler reaches the child that is on its way OUT as well as the one on its
 * way in, and the guard is what makes the outgoing child ignore it. That is the
 * Detaljer and Rapport story; it is NOT this one.
 *
 * The Oversigt child is never the outgoing child under any emitted trigger:
 *   - `resetToOverview()` calls `setMode('overview')` (state service :517)
 *     BEFORE `requestFetch()` (:518), so by the time the emission lands the
 *     mode is already `'overview'` and THIS guard passes. The child it is
 *     dropped by is whichever of Detaljer/Rapport is on its way out.
 *   - `drillIntoProperty()` (Oversigt → Detaljer, :493-496) uses
 *     `setFilterSilently` + `setMode` and emits nothing at all.
 *   - The only other `fetchRequestedSubject.next()` sites — `setPage`,
 *     `setShowAll`, `setSort` (:596, :607, :620) — are not mode switches.
 * So no reachable gesture makes the Oversigt guard drop a trigger. These are
 * CONTRACT tests, not a reproduction: they pin that all three children guard
 * the shared stream the same way, and this one's guard is defensive symmetry
 * with the two that do fire in anger. (The report-side spec's
 * `drops the reset trigger Oversigt fires while it is still mounted` is the
 * genuine end-to-end case.)
 *
 * The guard is uncovered by everything above, and its failure mode is
 * ASYMMETRIC: deleting it is SILENT — one extra request that `takeUntil` cancels on
 * destroy, plus a `loading` flag set for a moment — while mis-writing it (a
 * wrong mode string, an inverted comparison) is loud, because the view then
 * never fetches at all. The silent direction is the one that needs pinning, so
 * the drop tests below are the load-bearing ones and `is not stuck closed` only
 * proves the guard is not inverted.
 *
 * Same construction as the `buildRequest` suite: `new` rather than a TestBed,
 * with the REAL state service, so the guard is exercised against the actual
 * mode machine. Unlike that suite these tests DO call `ngOnInit`, which is the
 * whole point — `fetchRequested$` is only subscribed there.
 */
describe('ComplianceOverviewViewComponent — the view-mode guard', () => {
  let state: ComplianceReportStateService;
  let service: {overview: jest.Mock};
  let component: ComplianceOverviewViewComponent;

  beforeEach(() => {
    state = new ComplianceReportStateService();
    service = {
      overview: jest.fn().mockReturnValue(of({success: true, model: {rows: [], totals: null}})),
    };
    component = new ComplianceOverviewViewComponent(state, service as any);
  });

  afterEach(() => {
    component.ngOnDestroy();
  });

  it('drops a trigger emitted while another view owns the mode', () => {
    state.setMode('details');
    component.ngOnInit();

    // `requestFetch()` un-hides the report and emits, so the trigger really
    // does reach the subscription — it is the guard, not the
    // `reportVisible` gate on `fetchRequested$`, that stops it here.
    state.requestFetch();

    expect(service.overview).not.toHaveBeenCalled();
  });

  it('drops a trigger emitted while Rapport owns the mode, not just Detaljer', () => {
    // Exercises a SECOND foreign mode, so a guard written as
    // `mode !== 'details'` cannot pass this suite.
    state.setMode('report');
    component.ngOnInit();

    state.requestFetch();

    expect(service.overview).not.toHaveBeenCalled();
  });

  it('is not stuck closed: it queries as soon as Oversigt owns the mode', () => {
    state.setMode('details');
    component.ngOnInit();
    state.requestFetch();

    state.setMode('overview');
    state.requestFetch();

    // Once, not twice: the trigger dropped above must not be replayed.
    expect(service.overview).toHaveBeenCalledTimes(1);
  });

  it('never touches the shell loading flag for a trigger it drops', () => {
    // A request that never settles, so the `tap` that sets `loading` true is
    // observable. With a synchronously completing stub the subscribe callback
    // would clear it again in the same tick and this could not fail.
    service.overview.mockReturnValue(new Subject<any>());
    state.setMode('details');
    component.ngOnInit();

    state.requestFetch();

    // `loading` is the SHELL's flag and it gates `Opdater periode`. Without the
    // guard the `tap` above the switchMap sets it true for a view that is not
    // even on screen.
    expect(state.loading).toBe(false);
  });
});
