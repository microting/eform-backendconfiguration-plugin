import {
  addClampedMonths,
  COMPLIANCE_FILTER_DEBOUNCE_MS,
  COMPLIANCE_PAGE_SIZE,
  CompliancePeriodPreset,
  ComplianceReportStateService,
} from './compliance-report-state.service';

/**
 * Unit spec for the page shell's state machine (#1163 §13, rewritten for
 * #1185's auto-fetch contract). This is where the fetch-on-change contract is
 * genuinely testable without a browser, and where the single most likely way
 * to break #1164 — a drill-down that re-queries or blanks the page it just
 * navigated to — is pinned.
 *
 * Constructed directly rather than through a TestBed: the service has no
 * dependencies, matching adhoc-state.service.spec.ts's own pattern of avoiding
 * a module bootstrap for a plain class.
 *
 * The filter path is debounced with a real `setTimeout`, so every
 * "setFilter → fetch" assertion runs under fake timers and advances them past
 * `COMPLIANCE_FILTER_DEBOUNCE_MS`; `settle()` below is that one line.
 */
describe('ComplianceReportStateService', () => {
  let service: ComplianceReportStateService;

  function settle(): void {
    jest.advanceTimersByTime(COMPLIANCE_FILTER_DEBOUNCE_MS);
  }

  beforeEach(() => {
    jest.useFakeTimers();
    service = new ComplianceReportStateService();
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  describe('defaults', () => {
    it('opens on "all everything", not completed tasks, year to date, Oversigt', () => {
      expect(service.filters.propertyId).toBeNull();
      expect(service.filters.boardIds).toEqual([]);
      expect(service.filters.tagIds).toEqual([]);
      expect(service.filters.siteIds).toEqual([]);
      expect(service.filters.status).toBe('open');
      // The prototype's default, not the calendar view mode's '1'.
      expect(service.filters.periodPreset).toBe('ytd');
      expect(service.mode).toBe('overview');
      expect(service.reportVisible).toBe(false);
    });
  });

  describe('the auto-fetch state machine', () => {
    it('setFilter resets paging but keeps the report visible', () => {
      service.requestFetch();
      service.setTotalCount(42);
      service.setShowAll();
      expect(service.reportVisible).toBe(true);

      service.setFilter({status: 'done'});

      expect(service.filters.status).toBe('done');
      // NOT blanked: the mounted child keeps its rows until the new ones land.
      expect(service.reportVisible).toBe(true);
      expect(service.page).toBe(0);
      expect(service.showAll).toBe(false);
    });

    it('setFilter leaves loading and total to the child that is still mounted', () => {
      // Under blank-on-change the shell reset both because it was about to
      // UNMOUNT the child. Under auto-fetch the child stays, its switchMap
      // cancels the in-flight request, and it re-reports both itself.
      service.requestFetch();
      service.setLoading(true);
      service.setTotalCount(42);

      service.setFilter({status: 'done'});

      expect(service.loading).toBe(true);
      expect(service.total).toBe(42);
    });

    it('setFilter emits exactly one fetch after the debounce', () => {
      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));

      service.setFilter({propertyId: 7});
      expect(fetches.length).toBe(0);

      settle();

      expect(fetches.length).toBe(1);
      expect(service.reportVisible).toBe(true);
    });

    it('coalesces rapid filter changes into one fetch', () => {
      // Every tag click in the closeOnSelect=false multi-select is one
      // ngModelChange; three ticks must be one request.
      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));

      service.setFilter({tagIds: [1]});
      jest.advanceTimersByTime(COMPLIANCE_FILTER_DEBOUNCE_MS - 50);
      service.setFilter({tagIds: [1, 2]});
      jest.advanceTimersByTime(COMPLIANCE_FILTER_DEBOUNCE_MS - 50);
      service.setFilter({tagIds: [1, 2, 3]});
      expect(fetches.length).toBe(0);

      settle();

      expect(fetches.length).toBe(1);
      expect(service.filters.tagIds).toEqual([1, 2, 3]);
    });

    it('fetches from the hidden state too, so a suppressed entry recovers on the first change', () => {
      // enterPage() in Detaljer leaves the placeholder up; the customer's
      // rule is that the table always reflects the filters, so a change from
      // there must populate it.
      service.requestFetch();
      service.setMode('details');
      service.enterPage();
      expect(service.reportVisible).toBe(false);

      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));
      service.setFilter({tagIds: [4]});
      settle();

      expect(service.reportVisible).toBe(true);
      expect(fetches.length).toBe(1);
    });

    it('setFilterSilently neither fetches nor blanks', () => {
      service.requestFetch();
      service.setTotalCount(42);
      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));
      expect(fetches.length).toBe(1); // the replay

      service.setFilterSilently({propertyId: 9, status: 'all'});
      settle();

      expect(service.filters.propertyId).toBe(9);
      expect(service.filters.status).toBe('all');
      expect(service.reportVisible).toBe(true);
      expect(service.total).toBe(42);
      expect(fetches.length).toBe(1);
    });

    it('requestFetch fires fetchRequested$ immediately', () => {
      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));

      service.requestFetch();

      expect(fetches.length).toBe(1);
      expect(service.reportVisible).toBe(true);
    });

    it('a direct fetch supersedes a filter fetch still waiting on its debounce', () => {
      // One gesture, one request: a tag click followed within the debounce by
      // anything that fetches immediately must not fetch twice.
      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));

      service.setFilter({tagIds: [1]});
      service.requestFetch();
      expect(fetches.length).toBe(1);

      settle();

      expect(fetches.length).toBe(1);
    });

    it('replays the pending trigger to a subscriber that arrives late', () => {
      // The page switches view modes with ngSwitch, which DESTROYS and
      // RECREATES the child. The recreated child subscribes after the
      // emission, so a plain Subject would leave it with nothing to render
      // while reportVisible is true.
      service.requestFetch();

      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));

      expect(fetches.length).toBe(1);
    });

    it('replays to each late subscriber exactly once, not to existing ones', () => {
      const first: number[] = [];
      service.fetchRequested$.subscribe(() => first.push(1));

      service.requestFetch();
      expect(first.length).toBe(1);

      const second: number[] = [];
      service.fetchRequested$.subscribe(() => second.push(1));

      // The late subscriber gets the replay; the existing one is NOT re-served,
      // which is what keeps the ordinary filter-change path single-fetch.
      expect(second.length).toBe(1);
      expect(first.length).toBe(1);
    });
  });

  describe('custom period (Sæt periode) staging', () => {
    const jan2 = new Date(2026, 0, 2);
    const mar4 = new Date(2026, 2, 4);

    it('choosing Sæt periode blanks to the placeholder and does not fetch', () => {
      service.requestFetch();
      service.setTotalCount(42);
      service.setLoading(true);
      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));
      expect(fetches.length).toBe(1); // the replay

      service.setFilter({periodPreset: 'custom'});
      settle();

      expect(service.reportVisible).toBe(false);
      expect(service.total).toBe(0);
      // The child this unmounted may never reach setLoading(false); the shell
      // resets it so Opdater periode cannot wedge.
      expect(service.loading).toBe(false);
      expect(fetches.length).toBe(1);
      expect(service.periodBounds).toBeNull();
    });

    it('does not replay a stale trigger to a child mounted while staging', () => {
      service.requestFetch();
      service.setFilter({periodPreset: 'custom'});

      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));

      expect(service.reportVisible).toBe(false);
      expect(fetches.length).toBe(0);
    });

    it('staging dates does not fetch and does not change the committed bounds', () => {
      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));
      service.setFilter({periodPreset: 'custom'});

      service.stageCustomPeriod({from: jan2});
      service.stageCustomPeriod({to: mar4});
      settle();

      expect(service.customDraftFrom).toBe(jan2);
      expect(service.customDraftTo).toBe(mar4);
      expect(service.isPeriodValid).toBe(true);
      expect(service.filters.customFrom).toBeNull();
      expect(service.periodBounds).toBeNull();
      expect(service.reportVisible).toBe(false);
      expect(fetches.length).toBe(0);
    });

    it('requestFetch is a no-op while no custom range is committed', () => {
      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));
      service.setFilter({periodPreset: 'custom'});
      service.stageCustomPeriod({from: jan2, to: mar4});

      service.requestFetch();

      expect(fetches.length).toBe(0);
      expect(service.reportVisible).toBe(false);
    });

    it('commitCustomPeriod is a no-op while the draft is invalid', () => {
      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));
      service.setFilter({periodPreset: 'custom'});
      service.stageCustomPeriod({from: mar4, to: jan2});
      expect(service.isPeriodValid).toBe(false);

      service.commitCustomPeriod();

      expect(fetches.length).toBe(0);
      expect(service.filters.customFrom).toBeNull();
      expect(service.reportVisible).toBe(false);
    });

    it('commitCustomPeriod fetches once, immediately, when the draft is valid', () => {
      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));
      service.setFilter({periodPreset: 'custom'});
      service.stageCustomPeriod({from: jan2, to: mar4});

      service.commitCustomPeriod();

      expect(fetches.length).toBe(1);
      expect(service.reportVisible).toBe(true);
      expect(service.filters.customFrom).toBe(jan2);
      expect(service.filters.customTo).toBe(mar4);
      expect(service.requestModel.dateFrom).toBe('2026-01-02');
      expect(service.requestModel.dateTo).toBe('2026-03-04');
      settle();
      expect(fetches.length).toBe(1);
    });

    it('a non-period change in custom mode fetches with the committed range', () => {
      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));
      service.setFilter({periodPreset: 'custom'});
      service.stageCustomPeriod({from: jan2, to: mar4});
      service.commitCustomPeriod();
      expect(fetches.length).toBe(1);
      // The user has started editing the range again; the draft is not what
      // the query uses.
      service.stageCustomPeriod({to: null});

      service.setFilter({propertyId: 7});
      settle();

      expect(fetches.length).toBe(2);
      expect(service.reportVisible).toBe(true);
      expect(service.requestModel.dateFrom).toBe('2026-01-02');
      expect(service.requestModel.dateTo).toBe('2026-03-04');
    });

    it('re-choosing Sæt periode after a fixed preset stages again', () => {
      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));
      service.setFilter({periodPreset: 'custom'});
      service.stageCustomPeriod({from: jan2, to: mar4});
      service.commitCustomPeriod();
      service.setFilter({periodPreset: '3'});
      settle();
      expect(fetches.length).toBe(2);

      service.setFilter({periodPreset: 'custom'});
      settle();

      // The previously committed dates must not be queried by the next filter
      // change while the user is typing new ones — but the draft still offers
      // them.
      expect(fetches.length).toBe(2);
      expect(service.reportVisible).toBe(false);
      expect(service.filters.customFrom).toBeNull();
      expect(service.customDraftFrom).toBe(jan2);
    });

    it('a filter fetch still waiting on its debounce is dropped by staging', () => {
      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));

      service.setFilter({tagIds: [1]});
      service.setFilter({periodPreset: 'custom'});
      settle();

      expect(fetches.length).toBe(0);
      expect(service.reportVisible).toBe(false);
    });
  });

  describe('mode toggle', () => {
    it('preserves reportVisible so one fetch serves all three modes', () => {
      service.requestFetch();

      service.setMode('details');
      expect(service.reportVisible).toBe(true);
      service.setMode('report');
      expect(service.reportVisible).toBe(true);
      service.setMode('overview');
      expect(service.reportVisible).toBe(true);
    });

    it('falls back to overview for an unknown mode', () => {
      service.setMode('nonsense' as never);

      expect(service.mode).toBe('overview');
    });

    it('resets paging', () => {
      service.requestFetch();
      service.setTotalCount(100);
      service.setPage(3);

      service.setMode('report');

      expect(service.page).toBe(0);
      expect(service.showAll).toBe(false);
    });

    it('clears the previous mode\'s total so the pagination is not stale', () => {
      // Oversigt counts one row per property, Detaljer one per task. Carrying
      // 100 across the switch draws `Viser 1-10 af 100` and a ten-page button
      // window for a view that has not reported a single row yet.
      service.requestFetch();
      service.setTotalCount(100);

      service.setMode('details');

      expect(service.total).toBe(0);
      // ...and the chrome derived from it reads "no rows" rather than a page
      // window belonging to the mode we just left.
      expect(service.totalPages).toBe(1);
      expect(service.showingFrom).toBe(0);
    });

    it('clears loading, since the switch unmounts the in-flight child too', () => {
      service.requestFetch();
      service.setLoading(true);

      service.setMode('report');

      expect(service.loading).toBe(false);
    });

    it('still replays the trigger to the child the switch creates', () => {
      // The counterpart to enterPage(): killing the CROSS-NAVIGATION replay
      // must not kill the WITHIN-VISIT one. setMode keeps reportVisible true,
      // so the recreated child's late subscription is still served.
      service.requestFetch();

      service.setMode('details');

      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));

      expect(service.reportVisible).toBe(true);
      expect(fetches.length).toBe(1);
    });

    it('drops a filter fetch still waiting on its debounce: the replay already carries the new filters', () => {
      // Preset change, then a mode switch inside the 300 ms. The switch
      // recreates the child, whose late subscription replays the pending
      // trigger and reads `requestModel` at fetch time — so the debounced
      // filter fetch would only issue the same query a second time.
      service.requestFetch();
      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));
      expect(fetches.length).toBe(1);

      service.setFilter({periodPreset: '3'});
      service.setMode('details');
      settle();

      // The existing subscriber saw nothing new...
      expect(fetches.length).toBe(1);
      // ...and the child the switch creates gets exactly one replay, with
      // the changed preset already in place.
      const late: number[] = [];
      service.fetchRequested$.subscribe(() => late.push(1));
      expect(late.length).toBe(1);
      expect(service.filters.periodPreset).toBe('3');
    });

    it('a plain setMode keeps the filters — the reset is resetToOverview', () => {
      service.setFilter({propertyId: 7, tagIds: [1]});
      settle();

      service.setMode('overview');

      expect(service.filters.propertyId).toBe(7);
      expect(service.filters.tagIds).toEqual([1]);
    });

    it('keeps a pending filter fetch when the switch happens from the hidden re-entry state', () => {
      // B1 re-entry: back in Detaljer, so enterPage() hides the report and no
      // child is mounted. A filter change is then the ONLY thing that will
      // un-hide it — and a mode click inside the 300 ms must not swallow it:
      // there is no child to replay to while hidden, so the debounced
      // requestFetch() is the one request that brings the report back.
      service.requestFetch();
      service.setMode('details');
      service.enterPage();
      expect(service.reportVisible).toBe(false);

      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));
      expect(fetches.length).toBe(0);

      service.setFilter({status: 'done'});
      service.setMode('report');
      settle();

      expect(service.reportVisible).toBe(true);
      expect(fetches.length).toBe(1);
      expect(service.mode).toBe('report');
      expect(service.filters.status).toBe('done');
    });
  });

  describe('the Oversigt reset (resetToOverview)', () => {
    it('restores every default and fetches Oversigt once, immediately', () => {
      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));
      service.setFilter({
        propertyId: 7,
        boardIds: [3],
        tagIds: [1, 2],
        siteIds: [9],
        status: 'done',
        periodPreset: '3',
      });
      settle();
      service.setMode('details');
      service.setTotalCount(240);
      service.setPage(5);
      service.setSort('title', false);
      service.setLoading(true);
      const before = fetches.length;

      service.resetToOverview();

      expect(service.filters).toEqual({
        propertyId: null,
        boardIds: [],
        tagIds: [],
        siteIds: [],
        status: 'open',
        periodPreset: 'ytd',
        customFrom: null,
        customTo: null,
      });
      expect(service.mode).toBe('overview');
      expect(service.page).toBe(0);
      expect(service.showAll).toBe(false);
      expect(service.total).toBe(0);
      expect(service.loading).toBe(false);
      expect(service.sort).toBeNull();
      expect(service.isSortDsc).toBe(true);
      expect(service.reportVisible).toBe(true);
      expect(fetches.length).toBe(before + 1);
      settle();
      expect(fetches.length).toBe(before + 1);
    });

    it('resets while already in Oversigt too', () => {
      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));
      service.setFilter({tagIds: [1]});
      settle();
      expect(fetches.length).toBe(1);

      service.resetToOverview();

      expect(service.mode).toBe('overview');
      expect(service.filters.tagIds).toEqual([]);
      expect(fetches.length).toBe(2);
    });

    it('clears a committed custom range and its draft', () => {
      service.setFilter({periodPreset: 'custom'});
      service.stageCustomPeriod({from: new Date(2026, 0, 2), to: new Date(2026, 2, 4)});
      service.commitCustomPeriod();

      service.resetToOverview();

      expect(service.filters.periodPreset).toBe('ytd');
      expect(service.filters.customFrom).toBeNull();
      expect(service.customDraftFrom).toBeNull();
      expect(service.customDraftTo).toBeNull();
      expect(service.isPeriodValid).toBe(true);
    });

    it('recovers from the staged (placeholder) state', () => {
      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));
      service.setFilter({periodPreset: 'custom'});
      expect(service.reportVisible).toBe(false);

      service.resetToOverview();

      expect(service.reportVisible).toBe(true);
      expect(fetches.length).toBe(1);
    });

    it('supersedes a filter fetch still waiting on its debounce', () => {
      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));
      service.requestFetch();
      service.setMode('details');
      service.setFilter({tagIds: [1]});

      service.resetToOverview();
      expect(fetches.length).toBe(2);

      settle();

      expect(fetches.length).toBe(2);
    });
  });

  /**
   * Page entry (#1163 §6, kept by #1185 decision B1). The service is provided
   * by the LAZY module, whose NgModuleRef Angular caches for the lifetime of
   * the app — so every one of these tests is the second visit to the page,
   * modelled by driving the service through a first visit and then calling
   * enterPage() again. Entering is NOT pressing Oversigt: nothing resets.
   */
  describe('page entry', () => {
    it('auto-fetches exactly once when the preserved mode is Oversigt', () => {
      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));

      service.enterPage();

      expect(service.mode).toBe('overview');
      expect(service.reportVisible).toBe(true);
      expect(fetches.length).toBe(1);
    });

    it('does not auto-fetch when re-entered in Detaljer', () => {
      // Visit 1: fetch, then switch to Detaljer and navigate away.
      service.requestFetch();
      service.setMode('details');
      service.setTotalCount(240);

      // Visit 2: new components, same service, same buffered trigger.
      service.enterPage();

      expect(service.mode).toBe('details');
      // The placeholder shows...
      expect(service.reportVisible).toBe(false);
      // ...and the child that mounts next cannot be served the stale trigger,
      // because reportVisible is what gates fetchRequested$.
      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));
      expect(fetches.length).toBe(0);
    });

    it('does not auto-fetch when re-entered in Rapport', () => {
      service.requestFetch();
      service.setMode('report');

      service.enterPage();

      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));
      expect(service.reportVisible).toBe(false);
      expect(fetches.length).toBe(0);
    });

    it('preserves the previous visit\'s filters', () => {
      service.setFilter({propertyId: 7, tagIds: [1]});
      settle();
      service.setMode('details');

      service.enterPage();

      expect(service.filters.propertyId).toBe(7);
      expect(service.filters.tagIds).toEqual([1]);
    });

    it('clears the previous visit\'s pagination and loading state', () => {
      service.requestFetch();
      service.setTotalCount(240);
      service.setMode('details');
      service.setTotalCount(240);
      service.setPage(5);
      service.setLoading(true);

      service.enterPage();

      // No `Viser 51-60 af 240` from the last visit while the placeholder is
      // on screen.
      expect(service.total).toBe(0);
      expect(service.page).toBe(0);
      expect(service.showAll).toBe(false);
      expect(service.loading).toBe(false);
    });

    it('leaves fetching working after a suppressed entry', () => {
      service.requestFetch();
      service.setMode('details');
      service.enterPage();

      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));
      expect(fetches.length).toBe(0);

      service.requestFetch();

      expect(service.reportVisible).toBe(true);
      expect(fetches.length).toBe(1);
    });
  });

  describe('drill-down (the #1164 contract, status per #1185)', () => {
    it('sets the property silently, keeps the status and switches to Detaljer', () => {
      service.requestFetch();
      service.setTotalCount(5);
      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));
      expect(fetches.length).toBe(1); // the replay

      service.drillIntoProperty(12);
      settle();

      expect(service.filters.propertyId).toBe(12);
      // Oversigt's percentage is built on `Ikke udførte opgaver`; the
      // drill-down lists exactly those, never `Alle opgaver`.
      expect(service.filters.status).toBe('open');
      expect(service.mode).toBe('details');
      // The whole point: the already-fetched result survives, and nothing
      // fetches until the Detaljer child mounts and takes the replay.
      expect(service.reportVisible).toBe(true);
      expect(fetches.length).toBe(1);
    });

    it('the Oversigt reset restores the defaults after a drill, regardless of user changes', () => {
      service.requestFetch();
      service.drillIntoProperty(12);
      service.setFilter({propertyId: 34, status: 'done', tagIds: [2]});
      settle();

      service.resetToOverview();

      expect(service.filters.propertyId).toBeNull();
      expect(service.filters.status).toBe('open');
      expect(service.filters.tagIds).toEqual([]);
      expect(service.mode).toBe('overview');
      expect(service.reportVisible).toBe(true);
    });

    it('carries no drill bookkeeping: a second drill is just another drill', () => {
      service.requestFetch();

      service.drillIntoProperty(12);
      service.drillIntoProperty(13);

      expect(service.filters.propertyId).toBe(13);
      expect(service.filters.status).toBe('open');
      expect(service.mode).toBe('details');
    });
  });

  describe('period bounds', () => {
    const jan2 = new Date(2026, 0, 2);

    function withToday(today: Date, fn: () => void): void {
      // The suite already runs under fake timers (see beforeEach); only the
      // clock needs pinning here.
      jest.setSystemTime(today);
      fn();
    }

    it('ytd runs from 1 January to today', () => {
      withToday(new Date(2026, 8, 3), () => {
        const bounds = service.periodBounds;
        expect(bounds.from.getTime()).toBe(new Date(2026, 0, 1).getTime());
        expect(bounds.to.getTime()).toBe(new Date(2026, 8, 3).getTime());
      });
    });

    // today = 3 September 2026 (month index 8)
    const fixedPeriods: [CompliancePeriodPreset, number, number][] = [
      ['1', 2026, 7],
      ['3', 2026, 5],
      ['6', 2026, 2],
      ['12', 2025, 8],
    ];

    it.each(fixedPeriods)(
      '%s months is bounded above by today, never the future',
      (preset, expectedYear, expectedMonth) => {
        withToday(new Date(2026, 8, 3), () => {
          service.setFilter({periodPreset: preset});
          const bounds = service.periodBounds;
          expect(bounds.to.getTime()).toBe(new Date(2026, 8, 3).getTime());
          expect(bounds.from.getFullYear()).toBe(expectedYear);
          expect(bounds.from.getMonth()).toBe(expectedMonth);
        });
      }
    );

    it('an incomplete custom range means no period filter at all', () => {
      service.setFilter({periodPreset: 'custom'});
      expect(service.periodBounds).toBeNull();

      service.setFilter({customFrom: jan2});
      expect(service.periodBounds).toBeNull();
    });

    it('a complete custom range is used verbatim', () => {
      service.setFilter({
        periodPreset: 'custom',
        customFrom: new Date(2026, 0, 2),
        customTo: new Date(2026, 2, 4),
      });

      const bounds = service.periodBounds;
      expect(bounds.from.getTime()).toBe(new Date(2026, 0, 2).getTime());
      expect(bounds.to.getTime()).toBe(new Date(2026, 2, 4).getTime());
    });

    it('rejects a backwards custom range', () => {
      service.setFilter({
        periodPreset: 'custom',
        customFrom: new Date(2026, 2, 4),
        customTo: new Date(2026, 0, 2),
      });

      expect(service.isPeriodValid).toBe(false);
      expect(service.isCommittedPeriodValid).toBe(false);
    });
  });

  describe('addClampedMonths', () => {
    it('clamps 31 May minus 3 months to the end of February', () => {
      // Bare setMonth lands on 3 March. The prototype has that bug
      // (compliance.js:477); the shipped component already fixed it.
      const result = addClampedMonths(new Date(2026, 4, 31), -3);
      expect(result.getMonth()).toBe(1);
      expect(result.getDate()).toBe(28);
    });

    it('clamps 31 March minus 1 month to the end of February in a leap year', () => {
      const result = addClampedMonths(new Date(2024, 2, 31), -1);
      expect(result.getMonth()).toBe(1);
      expect(result.getDate()).toBe(29);
    });

    it('clamps 31 August plus 1 month to 30 September', () => {
      const result = addClampedMonths(new Date(2026, 7, 31), 1);
      expect(result.getMonth()).toBe(8);
      expect(result.getDate()).toBe(30);
    });

    it('leaves a safe date untouched', () => {
      const result = addClampedMonths(new Date(2026, 8, 15), -2);
      expect(result.getMonth()).toBe(6);
      expect(result.getDate()).toBe(15);
    });
  });

  describe('requestModel', () => {
    it('serialises the filter bar into the #1161 request shape', () => {
      service.setFilter({
        propertyId: 3,
        boardIds: [7],
        tagIds: [1, 2],
        siteIds: [9],
        status: 'all',
        periodPreset: 'custom',
        customFrom: new Date(2026, 0, 2),
        customTo: new Date(2026, 2, 4),
      });

      const model = service.requestModel;

      expect(model.propertyId).toBe(3);
      expect(model.boardIds).toEqual([7]);
      expect(model.tagIds).toEqual([1, 2]);
      expect(model.siteIds).toEqual([9]);
      expect(model.status).toBe('all');
      expect(model.dateFrom).toBe('2026-01-02');
      expect(model.dateTo).toBe('2026-03-04');
      expect(model.pageSize).toBe(COMPLIANCE_PAGE_SIZE);
      expect(model.pageIndex).toBe(0);
      expect(model.sort).toBeNull();
      expect(model.isSortDsc).toBe(true);
    });

    it('omits the period bounds entirely when a custom range is incomplete', () => {
      service.setFilter({periodPreset: 'custom', customFrom: new Date(2026, 0, 2), customTo: null});

      const model = service.requestModel;

      // NOT today: a fabricated one-day window is indistinguishable from a
      // real result, and #1169's export path reads requestModel outside the
      // isCommittedPeriodValid gate that requestFetch applies.
      expect('dateFrom' in model).toBe(false);
      expect('dateTo' in model).toBe(false);
    });

    it('asks for the unpaged shape when "Vis alle" is on', () => {
      service.requestFetch();
      service.setTotalCount(3000);
      service.setShowAll();

      // <= 0 is the server's "no paging" contract; #1161 caps it at 5000 rows.
      expect(service.requestModel.pageSize).toBe(0);
      expect(service.requestModel.pageIndex).toBe(0);
    });

    it('carries the page index through', () => {
      service.requestFetch();
      service.setTotalCount(300);
      service.setPage(4);

      expect(service.requestModel.pageIndex).toBe(4);
    });
  });

  describe('pagination chrome', () => {
    function pagesFor(total: number, page = 0): (number | 'gap')[] {
      service.requestFetch();
      service.setTotalCount(total);
      service.setPage(page);
      return service.pageNumbers;
    }

    it('lists every page while there are nine or fewer', () => {
      expect(pagesFor(1 * COMPLIANCE_PAGE_SIZE)).toEqual([0]);
      expect(pagesFor(9 * COMPLIANCE_PAGE_SIZE)).toEqual([0, 1, 2, 3, 4, 5, 6, 7, 8]);
    });

    it('windows with gaps from ten pages up', () => {
      const pages = pagesFor(10 * COMPLIANCE_PAGE_SIZE);

      expect(pages[0]).toBe(0);
      expect(pages).toContain('gap');
      expect(pages[pages.length - 1]).toBe(9);
    });

    it('stays bounded at 300 pages instead of rendering 300 buttons', () => {
      const pages = pagesFor(300 * COMPLIANCE_PAGE_SIZE, 150);

      expect(pages.length).toBeLessThanOrEqual(9);
      expect(pages[0]).toBe(0);
      expect(pages[pages.length - 1]).toBe(299);
      expect(pages).toContain(150);
    });

    it('reports the showing range', () => {
      service.requestFetch();
      service.setTotalCount(35);
      service.setPage(2);

      expect(service.showingFrom).toBe(21);
      expect(service.showingTo).toBe(30);
    });

    it('reports the whole set while showing all', () => {
      service.requestFetch();
      service.setTotalCount(35);
      service.setShowAll();

      expect(service.showingFrom).toBe(1);
      expect(service.showingTo).toBe(35);
    });

    it('reports nothing when there are no rows', () => {
      service.requestFetch();
      service.setTotalCount(0);

      expect(service.showingFrom).toBe(0);
      expect(service.totalPages).toBe(1);
    });

    it('ignores pagination while the report is hidden', () => {
      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));

      service.setPage(3);
      service.setShowAll();

      expect(service.page).toBe(0);
      expect(service.showAll).toBe(false);
      expect(fetches.length).toBe(0);
    });

    it('a filter change followed by paging within the debounce is one fetch, not two', () => {
      // setPage fetches immediately, and that fetch already reads the new
      // filters — the filter fetch still waiting on its debounce would only
      // issue the same query a second time.
      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));
      service.requestFetch();
      expect(fetches.length).toBe(1);

      service.setFilter({status: 'done'});
      service.setPage(2);
      expect(fetches.length).toBe(2);

      settle();

      expect(fetches.length).toBe(2);
      expect(service.page).toBe(2);
      expect(service.filters.status).toBe('done');
    });
  });

  describe('sorting', () => {
    it('re-queries immediately without resetting the filters', () => {
      const fetches: number[] = [];
      // Subscribe BEFORE the fetch: `fetchRequested$` replays its last trigger
      // to a late subscriber (see the "replays the pending trigger" tests), so
      // subscribing afterwards would count that replay as a second fetch.
      service.fetchRequested$.subscribe(() => fetches.push(1));
      service.requestFetch();
      expect(fetches.length).toBe(1);

      service.setSort('propertyName', false);

      expect(service.sort).toBe('propertyName');
      expect(service.isSortDsc).toBe(false);
      expect(service.reportVisible).toBe(true);
      expect(fetches.length).toBe(2);
    });

    it('does not fetch while the report is hidden', () => {
      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));

      service.setSort('title', true);

      expect(fetches.length).toBe(0);
    });
  });
});
