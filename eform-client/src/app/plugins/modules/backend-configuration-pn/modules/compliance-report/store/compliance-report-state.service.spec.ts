import {
  addClampedMonths,
  COMPLIANCE_PAGE_SIZE,
  CompliancePeriodPreset,
  ComplianceReportStateService,
} from './compliance-report-state.service';

/**
 * Unit spec for the page shell's state machine (#1163 §13). This is where the
 * blank-on-change contract is genuinely testable without a browser, and where
 * the single most likely way to break #1164 — a drill-down that blanks the page
 * it just navigated to — is pinned.
 *
 * Constructed directly rather than through a TestBed: the service has no
 * dependencies, matching adhoc-state.service.spec.ts's own pattern of avoiding
 * a module bootstrap for a plain class.
 */
describe('ComplianceReportStateService', () => {
  let service: ComplianceReportStateService;

  beforeEach(() => {
    service = new ComplianceReportStateService();
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

  describe('the blank-on-change state machine', () => {
    it('setFilter invalidates: page 1, no show-all, report hidden', () => {
      service.requestFetch();
      service.setTotalCount(42);
      service.setShowAll();
      expect(service.reportVisible).toBe(true);

      service.setFilter({status: 'done'});

      expect(service.filters.status).toBe('done');
      expect(service.reportVisible).toBe(false);
      expect(service.page).toBe(0);
      expect(service.showAll).toBe(false);
      expect(service.total).toBe(0);
    });

    it('setFilter emits no fetch request', () => {
      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));

      service.setFilter({propertyId: 7});
      service.setFilter({tagIds: [1, 2]});

      expect(fetches.length).toBe(0);
    });

    it('setFilterSilently does NOT invalidate', () => {
      service.requestFetch();
      service.setTotalCount(42);

      service.setFilterSilently({propertyId: 9, status: 'all'});

      expect(service.filters.propertyId).toBe(9);
      expect(service.filters.status).toBe('all');
      expect(service.reportVisible).toBe(true);
      expect(service.total).toBe(42);
    });

    it('requestFetch is the only thing that fires fetchRequested$', () => {
      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));

      service.requestFetch();

      expect(fetches.length).toBe(1);
      expect(service.reportVisible).toBe(true);
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
      // which is what keeps the ordinary `Opdater tabel` path single-fetch.
      expect(second.length).toBe(1);
      expect(first.length).toBe(1);
    });

    it('does not replay a stale trigger once a filter change has invalidated', () => {
      service.requestFetch();
      service.setFilter({status: 'done'});

      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));

      expect(service.reportVisible).toBe(false);
      expect(fetches.length).toBe(0);
    });

    it('requestFetch is a no-op while a custom range is invalid', () => {
      const fetches: number[] = [];
      service.fetchRequested$.subscribe(() => fetches.push(1));
      service.setFilter({periodPreset: 'custom'});

      service.requestFetch();

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
  });

  describe('drill-down (the #1164 contract)', () => {
    it('sets property and status silently and switches to Detaljer', () => {
      service.requestFetch();
      service.setTotalCount(5);

      service.drillIntoProperty(12);

      expect(service.filters.propertyId).toBe(12);
      // Oversigt counts done and not-done together, so the drill-down must
      // show both or the numbers do not add up.
      expect(service.filters.status).toBe('all');
      expect(service.mode).toBe('details');
      // The whole point: the already-fetched result survives.
      expect(service.reportVisible).toBe(true);
    });

    it('restores property AND status on the way back to Oversigt', () => {
      service.requestFetch();
      service.drillIntoProperty(12);

      service.setMode('overview');

      expect(service.filters.propertyId).toBeNull();
      expect(service.filters.status).toBe('open');
      expect(service.reportVisible).toBe(true);
    });

    it('leaves a property the user changed while drilled in alone', () => {
      service.requestFetch();
      service.drillIntoProperty(12);
      service.setFilter({propertyId: 34});

      service.setMode('overview');

      expect(service.filters.propertyId).toBe(34);
    });

    it('leaves a status the user changed while drilled in alone', () => {
      service.requestFetch();
      service.drillIntoProperty(12);
      service.setFilter({status: 'done'});

      service.setMode('overview');

      expect(service.filters.status).toBe('done');
    });

    it('clears the drill-down bookkeeping after unwinding', () => {
      service.drillIntoProperty(12);
      service.setMode('overview');

      expect(service.drilledProperty).toBeNull();
    });
  });

  describe('period bounds', () => {
    const jan2 = new Date(2026, 0, 2);

    function withToday(today: Date, fn: () => void): void {
      jest.useFakeTimers();
      jest.setSystemTime(today);
      try {
        fn();
      } finally {
        jest.useRealTimers();
      }
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
      // isPeriodValid gate that requestFetch applies.
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
  });

  describe('sorting', () => {
    it('re-queries without invalidating', () => {
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
