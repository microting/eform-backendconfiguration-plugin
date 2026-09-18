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

  /**
   * Pressing `Oversigt` (#1299, reversing #1185's reset): the mode switches
   * and Oversigt fetches once, but NO filter is reset — the customer's
   * "Oversigt nulstiller ikke længere filtrene". Every test here fails against
   * the #1185 implementation, which wrote `complianceInitialFilters()`.
   */
  describe('pressing Oversigt (resetToOverview) keeps the filters', () => {
    it('keeps every filter and the sort, switches to Oversigt and fetches once, immediately', () => {
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
        propertyId: 7,
        boardIds: [3],
        tagIds: [1, 2],
        siteIds: [9],
        status: 'done',
        periodPreset: '3',
        customFrom: null,
        customTo: null,
      });
      expect(service.mode).toBe('overview');
      expect(service.page).toBe(0);
      expect(service.showAll).toBe(false);
      expect(service.total).toBe(0);
      expect(service.loading).toBe(false);
      expect(service.sort).toBe('title');
      expect(service.isSortDsc).toBe(false);
      expect(service.reportVisible).toBe(true);
      expect(fetches.length).toBe(before + 1);
      settle();
      expect(fetches.length).toBe(before + 1);
    });

    it('while already in Oversigt it re-fetches and changes nothing', () => {
      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));
      service.setFilter({tagIds: [1], periodPreset: '6'});
      settle();
      expect(fetches.length).toBe(1);

      service.resetToOverview();

      expect(service.mode).toBe('overview');
      expect(service.filters.tagIds).toEqual([1]);
      expect(service.filters.periodPreset).toBe('6');
      expect(fetches.length).toBe(2);
    });

    it('keeps a committed custom range and its draft', () => {
      service.setFilter({periodPreset: 'custom'});
      service.stageCustomPeriod({from: new Date(2026, 0, 2), to: new Date(2026, 2, 4)});
      service.commitCustomPeriod();
      service.setMode('report');

      service.resetToOverview();

      expect(service.filters.periodPreset).toBe('custom');
      expect(service.filters.customFrom.getTime()).toBe(new Date(2026, 0, 2).getTime());
      expect(service.filters.customTo.getTime()).toBe(new Date(2026, 2, 4).getTime());
      expect(service.customDraftFrom.getTime()).toBe(new Date(2026, 0, 2).getTime());
      expect(service.customDraftTo.getTime()).toBe(new Date(2026, 2, 4).getTime());
      expect(service.reportVisible).toBe(true);
    });

    it('from the staged (placeholder) state it switches mode but stays on the placeholder, fetching nothing', () => {
      // An uncommitted "Sæt periode" has no period to query. #1185's reset
      // escaped this state by throwing the custom range away; now the range is
      // kept, so Oversigt waits for "Opdater periode" like every other mode.
      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));
      service.setMode('details');
      service.setFilter({periodPreset: 'custom'});
      expect(service.reportVisible).toBe(false);

      service.resetToOverview();

      expect(service.mode).toBe('overview');
      expect(service.filters.periodPreset).toBe('custom');
      expect(service.reportVisible).toBe(false);
      expect(fetches.length).toBe(0);
    });

    it('supersedes a filter fetch still waiting on its debounce', () => {
      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));
      service.requestFetch();
      service.setMode('details');
      service.setFilter({tagIds: [1]});

      service.resetToOverview();
      expect(fetches.length).toBe(2);
      expect(service.filters.tagIds).toEqual([1]);

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

    it('Oversigt undoes the drilled property but keeps every other filter the user changed (#1299)', () => {
      service.requestFetch();
      service.drillIntoProperty(12);
      service.setFilter({status: 'done', tagIds: [2], periodPreset: '6'});
      settle();

      service.resetToOverview();

      // Back to the Oversigt the user drilled FROM ("Alle ejendomme")...
      expect(service.filters.propertyId).toBeNull();
      // ...with nothing else reset.
      expect(service.filters.status).toBe('done');
      expect(service.filters.tagIds).toEqual([2]);
      expect(service.filters.periodPreset).toBe('6');
      expect(service.mode).toBe('overview');
      expect(service.reportVisible).toBe(true);
    });

    it('Oversigt keeps a property the user picked themselves after the drill', () => {
      service.requestFetch();
      service.drillIntoProperty(12);
      service.setFilter({propertyId: 34, boardIds: [5]});
      settle();

      service.resetToOverview();

      expect(service.filters.propertyId).toBe(34);
      expect(service.filters.boardIds).toEqual([5]);
    });

    it('Oversigt keeps the drilled property once the user has picked a calendar in it', () => {
      service.requestFetch();
      service.drillIntoProperty(12);
      service.setFilter({boardIds: [5]});
      settle();

      service.resetToOverview();

      expect(service.filters.propertyId).toBe(12);
      expect(service.filters.boardIds).toEqual([5]);
    });

    it('the undo is one-shot: a second Oversigt press after a manual property change keeps it', () => {
      service.requestFetch();
      service.drillIntoProperty(12);
      service.resetToOverview();
      expect(service.filters.propertyId).toBeNull();

      service.setFilter({propertyId: 12});
      settle();
      service.resetToOverview();

      expect(service.filters.propertyId).toBe(12);
    });

    it('a second drill keeps the ORIGINAL pre-drill property for the way back', () => {
      service.requestFetch();

      service.drillIntoProperty(12);
      service.drillIntoProperty(13);

      expect(service.filters.propertyId).toBe(13);
      expect(service.filters.status).toBe('open');
      expect(service.mode).toBe('details');

      service.resetToOverview();
      expect(service.filters.propertyId).toBeNull();
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

    /**
     * "År til dato + 1 år" (#1299): 1 January of today's year → today + 1 year
     * via `addClampedMonths(today, 12)`. One row per boundary cell.
     */
    const ytd1yCells: [string, Date, Date, Date][] = [
      ['a normal day (the issue\'s example)', new Date(2026, 8, 18), new Date(2026, 0, 1), new Date(2027, 8, 18)],
      ['29 Feb in a leap year clamps to 28 Feb', new Date(2028, 1, 29), new Date(2028, 0, 1), new Date(2029, 1, 28)],
      ['28 Feb before a leap year stays 28 Feb', new Date(2027, 1, 28), new Date(2027, 0, 1), new Date(2028, 1, 28)],
      ['31 December', new Date(2026, 11, 31), new Date(2026, 0, 1), new Date(2027, 11, 31)],
      ['1 January', new Date(2026, 0, 1), new Date(2026, 0, 1), new Date(2027, 0, 1)],
      ['late evening (date-level, midnight bounds)', new Date(2026, 8, 18, 23, 30), new Date(2026, 0, 1), new Date(2027, 8, 18)],
    ];

    it.each(ytd1yCells)('ytd1y on %s', (_label, today, expectedFrom, expectedTo) => {
      withToday(today, () => {
        service.setFilter({periodPreset: 'ytd1y'});
        const bounds = service.periodBounds;
        expect(bounds.from.getTime()).toBe(expectedFrom.getTime());
        expect(bounds.to.getTime()).toBe(expectedTo.getTime());
      });
    });

    it('ytd1y is sent to the server as plain dates', () => {
      withToday(new Date(2026, 8, 18), () => {
        service.setFilter({periodPreset: 'ytd1y'});
        expect(service.requestModel.dateFrom).toBe('2026-01-01');
        expect(service.requestModel.dateTo).toBe('2027-09-18');
        expect(service.isCommittedPeriodValid).toBe(true);
        expect(service.isPeriodValid).toBe(true);
      });
    });

    // The existing presets are untouched by the new one: still bounded by today.
    const unchangedPresets: [CompliancePeriodPreset, Date][] = [
      ['ytd', new Date(2026, 0, 1)],
      ['1', new Date(2026, 7, 18)],
      ['3', new Date(2026, 5, 18)],
      ['6', new Date(2026, 2, 18)],
      ['12', new Date(2025, 8, 18)],
    ];

    it.each(unchangedPresets)('%s still ends today on 18 Sep 2026', (preset, expectedFrom) => {
      withToday(new Date(2026, 8, 18), () => {
        service.setFilter({periodPreset: preset});
        const bounds = service.periodBounds;
        expect(bounds.from.getTime()).toBe(expectedFrom.getTime());
        expect(bounds.to.getTime()).toBe(new Date(2026, 8, 18).getTime());
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

/**
 * #1290 (reused by #1291): the row the Rapport view lands on after its next
 * response renders. Strictly one-shot — a highlight meant for one re-fetch must
 * never be re-applied by a later, unrelated one.
 */
describe('ComplianceReportStateService — pending row highlight', () => {
  let service: ComplianceReportStateService;

  beforeEach(() => {
    service = new ComplianceReportStateService();
  });

  it('is empty by default', () => {
    expect(service.takePendingRowHighlight()).toBeNull();
  });

  it('hands the key back once, then is empty', () => {
    service.setPendingRowHighlight('case:42');

    expect(service.takePendingRowHighlight()).toBe('case:42');
    expect(service.takePendingRowHighlight()).toBeNull();
  });

  it('a later set replaces an unconsumed one', () => {
    service.setPendingRowHighlight('case:1');
    service.setPendingRowHighlight('compliance:2');

    expect(service.takePendingRowHighlight()).toBe('compliance:2');
  });

  it('setting it never triggers a fetch', () => {
    const fetches: unknown[] = [];
    service.fetchRequested$.subscribe((x) => fetches.push(x));

    service.setPendingRowHighlight('case:42');

    expect(fetches.length).toBe(0);
  });

  it('`Oversigt` (resetToOverview) discards it', () => {
    service.setPendingRowHighlight('case:42');

    service.resetToOverview();

    expect(service.takePendingRowHighlight()).toBeNull();
  });
});

/**
 * #1291 — the return from the shared case page after `Gem`. `enterPage()` is
 * called with the URL's `?highlightId=`; only when it names the case of the
 * return context `setReturnContext()` stored before the navigation does entry
 * re-fetch — page-preserving — and queue the edited row. Every other entry is
 * the #1163 §6 status quo, and the context is dropped on EVERY entry.
 */
describe('ComplianceReportStateService — return from an edit (#1291)', () => {
  let service: ComplianceReportStateService;
  let fetches: number;

  /** Visit 1: Rapport fetched (optionally paged), Rediger pressed on case 102. */
  const leaveForEdit = (opts: {page?: number; showAll?: boolean} = {}) => {
    service.setMode('report');
    service.requestFetch();
    if (opts.page) {
      service.setPage(opts.page);
    }
    if (opts.showAll) {
      service.setShowAll();
    }
    service.setTotalCount(240);
    service.setLoading(true);
    service.setReturnContext(102, 'case:102');
  };

  /** The page's re-mounted child subscribing late, as the ngSwitch does. */
  const subscribeLikeTheChild = () => {
    fetches = 0;
    service.fetchRequested$.subscribe(() => fetches++);
  };

  beforeEach(() => {
    jest.useFakeTimers();
    service = new ComplianceReportStateService();
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  it('with a matching highlightId: re-fetches once, in the mode the user left', () => {
    leaveForEdit();

    service.enterPage(102);
    subscribeLikeTheChild();

    expect(service.mode).toBe('report');
    expect(service.reportVisible).toBe(true);
    expect(fetches).toBe(1);
  });

  it('with a matching highlightId: preserves the page', () => {
    leaveForEdit({page: 3});

    service.enterPage(102);

    expect(service.page).toBe(3);
    expect(service.showAll).toBe(false);
    expect(service.requestModel.pageIndex).toBe(3);
  });

  it('with a matching highlightId: preserves "Vis alle"', () => {
    leaveForEdit({showAll: true});

    service.enterPage(102);

    expect(service.showAll).toBe(true);
    expect(service.requestModel.pageSize).toBe(0);
  });

  it('with a matching highlightId: queues the edited row for the next response, once', () => {
    leaveForEdit();

    service.enterPage(102);

    expect(service.takePendingRowHighlight()).toBe('case:102');
    expect(service.takePendingRowHighlight()).toBeNull();
  });

  it('with a matching highlightId: clears the previous visit\'s total and loading', () => {
    leaveForEdit();

    service.enterPage(102);

    expect(service.total).toBe(0);
    expect(service.loading).toBe(false);
  });

  it('keeps the filters of the visit the user left', () => {
    service.setFilter({propertyId: 7, tagIds: [1], status: 'done'});
    jest.advanceTimersByTime(COMPLIANCE_FILTER_DEBOUNCE_MS);
    leaveForEdit();

    service.enterPage(102);

    expect(service.filters.propertyId).toBe(7);
    expect(service.filters.tagIds).toEqual([1]);
    expect(service.filters.status).toBe('done');
  });

  it('without a highlightId (Back without saving): the placeholder, no fetch, no highlight', () => {
    leaveForEdit({page: 3});

    service.enterPage();
    subscribeLikeTheChild();

    expect(service.mode).toBe('report');
    expect(service.reportVisible).toBe(false);
    expect(service.page).toBe(0);
    expect(fetches).toBe(0);
    expect(service.takePendingRowHighlight()).toBeNull();
  });

  it('with a highlightId for ANOTHER case: the placeholder, no fetch', () => {
    leaveForEdit();

    service.enterPage(999);
    subscribeLikeTheChild();

    expect(service.reportVisible).toBe(false);
    expect(fetches).toBe(0);
  });

  it('with a highlightId but no return context (another caller\'s return URL): the placeholder', () => {
    service.setMode('report');
    service.requestFetch();

    service.enterPage(102);
    subscribeLikeTheChild();

    expect(service.reportVisible).toBe(false);
    expect(fetches).toBe(0);
  });

  it('is strictly one-shot: the next entry from the menu is the placeholder again', () => {
    leaveForEdit();
    service.enterPage(102);
    // Navigate away and come back from the menu (no highlightId) ...
    service.enterPage();
    subscribeLikeTheChild();
    expect(service.reportVisible).toBe(false);
    expect(fetches).toBe(0);

    // ... and even a repeated highlightId finds nothing to return to.
    service.enterPage(102);
    subscribeLikeTheChild();
    expect(service.reportVisible).toBe(false);
    expect(fetches).toBe(0);
  });

  it('an ABANDONED edit (Back without saving) cannot make a later entry fetch', () => {
    leaveForEdit();
    service.enterPage(); // Back, no save: the context is dropped here.

    service.enterPage(102);
    subscribeLikeTheChild();

    expect(service.reportVisible).toBe(false);
    expect(fetches).toBe(0);
  });

  it('refuses while the committed period cannot be queried, falling back to the placeholder', () => {
    service.setMode('report');
    service.requestFetch();
    service.setFilter({periodPreset: 'custom'});
    service.setReturnContext(102, 'case:102');

    service.enterPage(102);
    subscribeLikeTheChild();

    expect(service.reportVisible).toBe(false);
    expect(fetches).toBe(0);
    expect(service.takePendingRowHighlight()).toBeNull();
  });

  it('`Oversigt` (resetToOverview) discards a stored return context', () => {
    leaveForEdit();
    service.resetToOverview();
    service.setMode('report');

    service.enterPage(102);

    expect(service.reportVisible).toBe(false);
  });

  it('storing a return context never fetches on its own', () => {
    service.setMode('report');
    service.requestFetch();
    subscribeLikeTheChild();
    // The late subscriber is served the buffered trigger once.
    expect(fetches).toBe(1);

    service.setReturnContext(102, 'case:102');

    expect(fetches).toBe(1);
  });

  it('entry drops a row highlight still queued from the previous visit', () => {
    service.setMode('report');
    service.requestFetch();
    service.setPendingRowHighlight('case:5');

    service.enterPage();

    expect(service.takePendingRowHighlight()).toBeNull();
  });
});

/**
 * #1299 — the chosen period is remembered per user in localStorage, across
 * reload and login, until the user changes it. `restoreSavedPeriod(userId)` is
 * what the page calls on every visit before `enterPage()`; a "reload" is a NEW
 * service instance (the lazy module is rebuilt), a logout/login in the same tab
 * is the SAME instance seeing the same or a different user id.
 */
describe('ComplianceReportStateService — remembered period (#1299)', () => {
  const USER = 7;
  const OTHER = 8;
  const KEY = 'bcpn.compliance.period.7';
  const OTHER_KEY = 'bcpn.compliance.period.8';
  let service: ComplianceReportStateService;

  /** A page reload: a fresh service, the signed-in user restored. */
  function reload(userId: number): ComplianceReportStateService {
    const fresh = new ComplianceReportStateService();
    fresh.restoreSavedPeriod(userId);
    return fresh;
  }

  beforeEach(() => {
    jest.useFakeTimers();
    jest.setSystemTime(new Date(2026, 8, 18, 10, 0));
    window.localStorage.clear();
    service = new ComplianceReportStateService();
  });

  afterEach(() => {
    jest.restoreAllMocks();
    jest.useRealTimers();
    window.localStorage.clear();
  });

  it('with nothing saved, the first visit keeps the År til dato default', () => {
    service.restoreSavedPeriod(USER);

    expect(service.filters.periodPreset).toBe('ytd');
  });

  it.each(['1', '3', '6', '12', 'ytd', 'ytd1y'] as CompliancePeriodPreset[])(
    'a chosen preset (%s) survives a reload for the same user',
    (preset) => {
      service.restoreSavedPeriod(USER);
      service.setFilter({periodPreset: preset});

      expect(reload(USER).filters.periodPreset).toBe(preset);
    }
  );

  it('a committed custom range survives a reload, committed AND in the pickers', () => {
    service.restoreSavedPeriod(USER);
    service.setFilter({periodPreset: 'custom'});
    service.stageCustomPeriod({from: new Date(2026, 1, 3), to: new Date(2026, 4, 20)});
    service.commitCustomPeriod();

    expect(JSON.parse(window.localStorage.getItem(KEY))).toEqual({
      periodPreset: 'custom', customFrom: '2026-02-03', customTo: '2026-05-20',
    });

    const restored = reload(USER);
    expect(restored.filters.periodPreset).toBe('custom');
    expect(restored.filters.customFrom.getTime()).toBe(new Date(2026, 1, 3).getTime());
    expect(restored.filters.customTo.getTime()).toBe(new Date(2026, 4, 20).getTime());
    expect(restored.customDraftFrom.getTime()).toBe(new Date(2026, 1, 3).getTime());
    expect(restored.customDraftTo.getTime()).toBe(new Date(2026, 4, 20).getTime());
    // The restored range is queryable at once: entry fetches Oversigt with it.
    expect(restored.isCommittedPeriodValid).toBe(true);
    expect(restored.requestModel.dateFrom).toBe('2026-02-03');
    expect(restored.requestModel.dateTo).toBe('2026-05-20');
    const fetches: number[] = [];
    restored.fetchRequested$.subscribe(() => fetches.push(1));
    restored.enterPage();
    expect(fetches.length).toBe(1);
  });

  it('picking "Sæt periode" without committing does not replace the saved period', () => {
    service.restoreSavedPeriod(USER);
    service.setFilter({periodPreset: '6'});
    service.setFilter({periodPreset: 'custom'});
    service.stageCustomPeriod({from: new Date(2026, 1, 3), to: new Date(2026, 4, 20)});

    expect(reload(USER).filters.periodPreset).toBe('6');
  });

  it('a later choice replaces the saved one ("until the period is changed")', () => {
    service.restoreSavedPeriod(USER);
    service.setFilter({periodPreset: 'custom'});
    service.stageCustomPeriod({from: new Date(2026, 1, 3), to: new Date(2026, 4, 20)});
    service.commitCustomPeriod();
    service.setFilter({periodPreset: '3'});

    const restored = reload(USER);
    expect(restored.filters.periodPreset).toBe('3');
    expect(restored.filters.customFrom).toBeNull();
  });

  it('only the period is remembered — the other filters come back at their defaults', () => {
    service.restoreSavedPeriod(USER);
    service.setFilter({propertyId: 4, tagIds: [2], siteIds: [9], status: 'done', periodPreset: '12'});

    const restored = reload(USER);
    expect(restored.filters).toEqual({
      propertyId: null, boardIds: [], tagIds: [], siteIds: [], status: 'open',
      periodPreset: '12', customFrom: null, customTo: null,
    });
  });

  it('pressing Oversigt neither changes nor forgets the saved period', () => {
    service.restoreSavedPeriod(USER);
    service.setFilter({periodPreset: 'ytd1y'});
    service.setMode('details');

    service.resetToOverview();

    expect(service.filters.periodPreset).toBe('ytd1y');
    expect(reload(USER).filters.periodPreset).toBe('ytd1y');
  });

  it('nothing is saved before the user is known', () => {
    service.setFilter({periodPreset: '3'});

    expect(window.localStorage.length).toBe(0);
  });

  it.each([
    ['undefined', undefined],
    ['null', null],
    ['0', 0],
    ['negative', -1],
  ])('an invalid user id (%s) restores nothing and enables no saving', (_label, id) => {
    window.localStorage.setItem(KEY, JSON.stringify({periodPreset: '3', customFrom: null, customTo: null}));

    service.restoreSavedPeriod(id as number);
    service.setFilter({periodPreset: '6'});

    expect(service.filters.periodPreset).toBe('6');
    expect(JSON.parse(window.localStorage.getItem(KEY)).periodPreset).toBe('3');
    expect(window.localStorage.length).toBe(1);
  });

  it('one user\'s saved period is never read for another user', () => {
    service.restoreSavedPeriod(USER);
    service.setFilter({periodPreset: '3'});

    expect(reload(OTHER).filters.periodPreset).toBe('ytd');
    expect(window.localStorage.getItem(OTHER_KEY)).toBeNull();
  });

  it('each user gets their own period back', () => {
    reload(USER).setFilter({periodPreset: '3'});
    reload(OTHER).setFilter({periodPreset: 'ytd1y'});

    expect(reload(USER).filters.periodPreset).toBe('3');
    expect(reload(OTHER).filters.periodPreset).toBe('ytd1y');
  });

  it('re-entry by the same user keeps the in-memory state (e.g. an uncommitted "Sæt periode")', () => {
    service.restoreSavedPeriod(USER);
    service.setFilter({periodPreset: '6'});
    service.setFilter({periodPreset: 'custom', propertyId: 3});

    service.restoreSavedPeriod(USER);

    expect(service.filters.periodPreset).toBe('custom');
    expect(service.filters.propertyId).toBe(3);
  });

  it('a different user signing in in the same tab inherits NOTHING from the previous one', () => {
    // The service lives on the lazy module, which survives a logout.
    window.localStorage.setItem(OTHER_KEY, JSON.stringify({periodPreset: '12', customFrom: null, customTo: null}));
    service.restoreSavedPeriod(USER);
    service.setFilter({propertyId: 4, boardIds: [2], tagIds: [1], siteIds: [9], status: 'done', periodPreset: '3'});
    service.setSort('title', false);
    service.setMode('report');
    service.setReturnContext(55, 'case:55');

    service.restoreSavedPeriod(OTHER);

    expect(service.filters).toEqual({
      propertyId: null, boardIds: [], tagIds: [], siteIds: [], status: 'open',
      periodPreset: '12', customFrom: null, customTo: null,
    });
    expect(service.customDraftFrom).toBeNull();
    expect(service.customDraftTo).toBeNull();
    expect(service.sort).toBeNull();
    expect(service.isSortDsc).toBe(true);
    expect(service.mode).toBe('overview');
    // The previous user's edit round trip is dropped with the rest.
    service.setMode('report');
    service.enterPage(55);
    expect(service.reportVisible).toBe(false);
  });

  it('after a user switch, the new user\'s choices are saved under THEIR key only', () => {
    service.restoreSavedPeriod(USER);
    service.setFilter({periodPreset: '3'});

    service.restoreSavedPeriod(OTHER);
    service.setFilter({periodPreset: 'ytd1y'});

    expect(JSON.parse(window.localStorage.getItem(KEY)).periodPreset).toBe('3');
    expect(JSON.parse(window.localStorage.getItem(OTHER_KEY)).periodPreset).toBe('ytd1y');
  });

  it('switching user does not write the previous user\'s period under the new key', () => {
    service.restoreSavedPeriod(USER);
    service.setFilter({periodPreset: '3'});

    service.restoreSavedPeriod(OTHER);

    expect(window.localStorage.getItem(OTHER_KEY)).toBeNull();
    expect(service.filters.periodPreset).toBe('ytd');
  });

  it('a corrupt saved period falls back to the default', () => {
    window.localStorage.setItem(KEY, '{not json');

    service.restoreSavedPeriod(USER);

    expect(service.filters.periodPreset).toBe('ytd');
  });

  it('a storage that throws on read falls back to the default without breaking', () => {
    jest.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('SecurityError');
    });

    expect(() => service.restoreSavedPeriod(USER)).not.toThrow();
    expect(service.filters.periodPreset).toBe('ytd');
  });

  it('a storage that throws on write still applies the change and fetches', () => {
    service.restoreSavedPeriod(USER);
    jest.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('QuotaExceededError');
    });
    const fetches: number[] = [];
    service.fetchRequested$.subscribe(() => fetches.push(1));

    expect(() => service.setFilter({periodPreset: '3'})).not.toThrow();
    jest.advanceTimersByTime(COMPLIANCE_FILTER_DEBOUNCE_MS);

    expect(service.filters.periodPreset).toBe('3');
    expect(fetches.length).toBe(1);
  });
});
