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
