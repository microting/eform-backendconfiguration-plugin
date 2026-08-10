import {of} from 'rxjs';
import {differenceInCalendarDays, parseISO} from 'date-fns';
import {AdhocHistoryComponent} from './adhoc-history.component';
import {AdhocTaskHistoryRowModel, AdhocTaskStatusFilter} from '../../../../models';
import {adhocInitialState} from '../../../../state';

/**
 * Spy-store/spy-service unit test for `AdhocHistoryComponent` (#1095,
 * mockup-parity Historik table). Authored, not run - jest runs from the
 * host frontend only (repo convention).
 *
 * Date-math cases use either relative invariants (day-difference between
 * the sent bounds) or a jasmine mock clock - never fixed "today" calendar
 * dates, which would flake around midnight/month boundaries in CI.
 */
describe('AdhocHistoryComponent', () => {
  let storeSpy: any;
  let adhocServiceSpy: any;
  let adhocStateServiceSpy: any;
  let dialogSpy: any;
  let overlaySpy: any;
  let translateSpy: any;
  let component: AdhocHistoryComponent;

  function makeRow(partial: Partial<AdhocTaskHistoryRowModel>): AdhocTaskHistoryRowModel {
    return {
      taskId: 1,
      taskTitle: 'Task',
      completedAt: '2026-04-15T09:00:00Z',
      completedByName: '',
      status: AdhocTaskStatusFilter.Completed,
      propertyName: 'P',
      areaName: null,
      tagNames: [],
      lastCommentText: null,
      lastCommentAuthor: null,
      lastCommentAt: null,
      ...partial,
    };
  }

  function buildComponent(): AdhocHistoryComponent {
    storeSpy = {
      select: jasmine.createSpy('select').and.returnValue(of({...adhocInitialState.historyFilters})),
      dispatch: jasmine.createSpy('dispatch'),
    };
    adhocServiceSpy = jasmine.createSpyObj('BackendConfigurationPnAdhocService', [
      'getHistory', 'getTask', 'archiveTask',
    ]);
    adhocServiceSpy.getHistory.and.returnValue(of({success: true, model: {total: 0, entities: []}}));
    adhocStateServiceSpy = {
      properties: [],
      tags: [],
      getAreasForProperty: jasmine.createSpy('getAreasForProperty').and.returnValue(of([])),
    };
    dialogSpy = jasmine.createSpyObj('MatDialog', ['open']);
    overlaySpy = {};
    // Key-passthrough with naive {{param}} interpolation - keeps summary/
    // pager label assertions readable without a real TranslateService.
    translateSpy = {
      instant: jasmine.createSpy('instant').and.callFake((key: string, params?: Record<string, unknown>) => {
        if (!params) {
          return key;
        }
        return Object.keys(params).reduce((acc, p) => acc.replace(`{{${p}}}`, String(params[p])), key);
      }),
    };

    const built = new AdhocHistoryComponent(
      storeSpy, adhocServiceSpy, adhocStateServiceSpy, dialogSpy, overlaySpy, translateSpy,
    );
    built.ngOnInit();
    return built;
  }

  beforeEach(() => {
    component = buildComponent();
  });

  function lastSentModel(): any {
    return adhocServiceSpy.getHistory.calls.mostRecent().args[0];
  }

  describe('updateTable / period resolution', () => {
    it('sends AND-only tagIds + property/area + paging to getHistory', () => {
      component.currentFilters = {...component.currentFilters, propertyId: 1, areaId: 10, tagIds: [1, 2]};
      component.updateTable();
      const sentModel = lastSentModel();
      expect(sentModel.propertyId).toBe(1);
      expect(sentModel.areaId).toBe(10);
      expect(sentModel.tagIds).toEqual([1, 2]);
      expect(sentModel.pageNumber).toBe(1);
      expect(sentModel.pageSize).toBe(25);
    });

    it('every preset resolves BOTH a non-null dateFrom and a non-null dateTo (no more null dateTo)', () => {
      for (const preset of component.periodPresets) {
        component.currentFilters = {...component.currentFilters, periodPreset: preset};
        component.updateTable();
        const sentModel = lastSentModel();
        expect(sentModel.dateFrom).not.toBeNull();
        expect(sentModel.dateTo).not.toBeNull();
      }
    });

    it('day presets (30/60/90) resolve a "to" of today and a "from" of today minus (n-1) days, inclusive', () => {
      const cases: Array<['30' | '60' | '90', number]> = [['30', 29], ['60', 59], ['90', 89]];
      for (const [preset, expectedDayGap] of cases) {
        component.currentFilters = {...component.currentFilters, periodPreset: preset};
        component.updateTable();
        const sentModel = lastSentModel();
        const from = parseISO(sentModel.dateFrom);
        const to = parseISO(sentModel.dateTo);
        expect(differenceInCalendarDays(to, from)).toBe(expectedDayGap);
        expect(differenceInCalendarDays(new Date(), to)).toBe(0);
      }
    });

    it('the "6m" preset clamps to the shorter month when the anchor day does not exist in the target month', () => {
      jasmine.clock().install();
      try {
        // Mar 31 - 6 months back lands in Sep (30 days) -> clamp to Sep 30,
        // not roll over into Oct (mockup addCalendarMonthsIso behavior).
        jasmine.clock().mockDate(new Date(2026, 2, 31));
        component.currentFilters = {...component.currentFilters, periodPreset: '6m'};
        component.updateTable();
        const sentModel = lastSentModel();
        expect(sentModel.dateFrom.slice(0, 10)).toBe('2025-09-30');
        expect(sentModel.dateTo.slice(0, 10)).toBe('2026-03-31');
      } finally {
        jasmine.clock().uninstall();
      }
    });

    it('the "12m" preset resolves the same day-of-month 12 months back (clamping identically to 6m)', () => {
      jasmine.clock().install();
      try {
        jasmine.clock().mockDate(new Date(2026, 5, 15));
        component.currentFilters = {...component.currentFilters, periodPreset: '12m'};
        component.updateTable();
        expect(lastSentModel().dateFrom.slice(0, 10)).toBe('2025-06-15');

        // Leap-day anchor: Feb 29 2028 - 12 months -> Feb 28 2027 (clamped).
        jasmine.clock().mockDate(new Date(2028, 1, 29));
        component.updateTable();
        expect(lastSentModel().dateFrom.slice(0, 10)).toBe('2027-02-28');
      } finally {
        jasmine.clock().uninstall();
      }
    });

    it('the "custom" preset with no dates picked falls back to today-today', () => {
      component.currentFilters = {...component.currentFilters, periodPreset: 'custom', customFrom: null, customTo: null};
      component.updateTable();
      const sentModel = lastSentModel();
      expect(sentModel.dateFrom).toEqual(sentModel.dateTo);
      expect(differenceInCalendarDays(new Date(), parseISO(sentModel.dateTo))).toBe(0);
    });

    it('the "custom" preset defaults a missing side to the other side', () => {
      component.currentFilters = {
        ...component.currentFilters, periodPreset: 'custom', customFrom: '2026-05-05', customTo: null,
      };
      component.updateTable();
      const sentModel = lastSentModel();
      expect(sentModel.dateFrom.slice(0, 10)).toBe('2026-05-05');
      expect(sentModel.dateTo.slice(0, 10)).toBe('2026-05-05');
    });

    it('custom range with from > to swaps them before being sent to getHistory', () => {
      component.currentFilters = {
        ...component.currentFilters, periodPreset: 'custom', customFrom: '2026-05-10', customTo: '2026-05-01',
      };
      component.updateTable();
      const sentModel = lastSentModel();
      expect(sentModel.dateFrom.slice(0, 10)).toBe('2026-05-01');
      expect(sentModel.dateTo.slice(0, 10)).toBe('2026-05-10');
    });

    it('switching from "custom" to a day preset discards any pending custom dates from the resolved range', () => {
      component.currentFilters = {
        ...component.currentFilters, periodPreset: 'custom', customFrom: '2020-01-01', customTo: '2020-02-01',
      };
      component.onPeriodChange('30');
      const sentModel = lastSentModel();
      const from = parseISO(sentModel.dateFrom);
      const to = parseISO(sentModel.dateTo);
      expect(differenceInCalendarDays(to, from)).toBe(29);
      expect(sentModel.dateFrom.slice(0, 10)).not.toBe('2020-01-01');
    });
  });

  describe('custom range mutators', () => {
    it('picking a custom "from" date force-switches the preset to custom and persists an ISO date', () => {
      component.currentFilters = {...component.currentFilters, periodPreset: '90'};
      component.onCustomFromChange(new Date(2026, 0, 15));
      const dispatched = storeSpy.dispatch.calls.mostRecent().args[0];
      expect(dispatched.periodPreset).toBe('custom');
      expect(dispatched.customFrom).toBe('2026-01-15');
    });

    it('picking a custom "to" date force-switches the preset to custom', () => {
      component.currentFilters = {...component.currentFilters, periodPreset: '30'};
      component.onCustomToChange(new Date(2026, 5, 30));
      const dispatched = storeSpy.dispatch.calls.mostRecent().args[0];
      expect(dispatched.periodPreset).toBe('custom');
      expect(dispatched.customTo).toBe('2026-06-30');
    });
  });

  describe('filter summary lines', () => {
    it('filterSummaryLineA returns the chip label key for non-custom presets', () => {
      component.currentFilters = {...component.currentFilters, periodPreset: '90'};
      expect(component.filterSummaryLineA()).toBe('90 days');
      component.currentFilters = {...component.currentFilters, periodPreset: '6m'};
      expect(component.filterSummaryLineA()).toBe('6 months period');
    });

    it('filterSummaryLineA returns a formatted dd.MM.yyyy range for the custom preset', () => {
      component.currentFilters = {
        ...component.currentFilters, periodPreset: 'custom', customFrom: '2026-01-01', customTo: '2026-01-31',
      };
      expect(component.filterSummaryLineA()).toBe('01.01.2026 — 31.01.2026');
    });

    it('filterSummaryLineB defaults to All properties / All areas / None selected', () => {
      expect(component.filterSummaryLineB())
        .toBe('Property: All properties · Areas: All areas · Tags in history (AND): None selected');
    });

    it('filterSummaryLineB narrows as property, area and tags are selected (tags Danish-locale sorted)', () => {
      adhocStateServiceSpy.properties = [{id: 1, name: 'Gården'}];
      adhocStateServiceSpy.tags = [{id: 5, name: 'Ølager'}, {id: 6, name: 'Andet'}];
      component.areas = [{id: 10, propertyId: 1, name: 'Laden'}];
      component.currentFilters = {...component.currentFilters, propertyId: 1, areaId: 10, tagIds: [5, 6]};
      expect(component.filterSummaryLineB())
        .toBe('Property: Gården · Areas: Laden · Tags in history (AND): Andet, Ølager');
    });

    it('activePeriodSummaryLabel composes the aria-live "Active period" line from the resolved range', () => {
      component.currentFilters = {
        ...component.currentFilters, periodPreset: 'custom', customFrom: '2026-02-01', customTo: '2026-02-28',
      };
      expect(component.activePeriodSummaryLabel())
        .toBe('Active period: 01.02.2026 to 28.02.2026 inclusive of both days');
    });
  });

  describe('property/area coupling', () => {
    it('selecting "All properties" (null) clears areaId and empties the area list', () => {
      component.currentFilters = {...component.currentFilters, propertyId: 1, areaId: 10};
      component.areas = [{id: 10, propertyId: 1, name: 'Laden'}];
      component.onPropertyChange(null);
      const dispatched = storeSpy.dispatch.calls.mostRecent().args[0];
      expect(dispatched.propertyId).toBeNull();
      expect(dispatched.areaId).toBeNull();
      expect(component.areas).toEqual([]);
    });

    it('selecting a concrete property preserves a previously-selected areaId still valid for it', () => {
      // Behavior change vs the old component (which unconditionally nulled
      // areaId) - mirrors the mockup's fillHistoryOmraadeSelect keep-valid rule.
      component.currentFilters = {...component.currentFilters, propertyId: 1, areaId: 10};
      adhocStateServiceSpy.getAreasForProperty.and.returnValue(of([
        {id: 10, propertyId: 2, name: 'Laden'},
        {id: 11, propertyId: 2, name: 'Stalden'},
      ]));
      component.onPropertyChange(2);
      const dispatched = storeSpy.dispatch.calls.mostRecent().args[0];
      expect(dispatched.propertyId).toBe(2);
      expect(dispatched.areaId).toBe(10);
    });

    it('selecting a concrete property drops an areaId that is not valid for it', () => {
      component.currentFilters = {...component.currentFilters, propertyId: 1, areaId: 10};
      adhocStateServiceSpy.getAreasForProperty.and.returnValue(of([{id: 20, propertyId: 2, name: 'Marken'}]));
      component.onPropertyChange(2);
      const dispatched = storeSpy.dispatch.calls.mostRecent().args[0];
      expect(dispatched.propertyId).toBe(2);
      expect(dispatched.areaId).toBeNull();
    });
  });

  describe('tag panel', () => {
    it('onToggleTag adds/removes and dispatches + reloads', () => {
      component.onToggleTag(5);
      expect(storeSpy.dispatch).toHaveBeenCalled();
      expect(adhocServiceSpy.getHistory).toHaveBeenCalled();
    });

    it('filteredTags is a live case-insensitive substring filter', () => {
      adhocStateServiceSpy.tags = [
        {id: 1, name: 'Maskiner'}, {id: 2, name: 'Olieskift'}, {id: 3, name: 'Case 123'},
      ];
      component.tagSearchQuery = '';
      expect(component.filteredTags().length).toBe(3);
      component.tagSearchQuery = 'mask';
      expect(component.filteredTags().map((t: any) => t.name)).toEqual(['Maskiner']);
      component.tagSearchQuery = 'IE';
      expect(component.filteredTags().map((t: any) => t.name)).toEqual(['Olieskift']);
      component.tagSearchQuery = 'zzz';
      expect(component.filteredTags()).toEqual([]);
    });

    it('selectedTags mirrors the AND-only tagIds selection (for the click-to-deselect pill row)', () => {
      adhocStateServiceSpy.tags = [{id: 1, name: 'A'}, {id: 2, name: 'B'}];
      component.currentFilters = {...component.currentFilters, tagIds: [2]};
      expect(component.selectedTags().map((t: any) => t.id)).toEqual([2]);
    });
  });

  describe('pager', () => {
    it('pager text is "Page X of Y" with the correct pluralized hit count (plural)', () => {
      component.total = 5;
      component.pageSize = 2;
      component.pageIndex = 0;
      expect(component.pagerInfoLabel()).toBe('Page 1 of 3 · 5 hits');
    });

    it('pager text uses the singular "hit" for exactly one hit', () => {
      component.total = 1;
      component.pageSize = 25;
      component.pageIndex = 0;
      expect(component.pagerInfoLabel()).toBe('Page 1 of 1 · 1 hit');
    });

    it('totalPages is 1 (pager hidden via *ngIf) when the result set fits on one page', () => {
      component.total = 0;
      expect(component.totalPages).toBe(1);
      component.total = 25;
      component.pageSize = 25;
      expect(component.totalPages).toBe(1);
    });

    it('onPagerNext advances and refetches with the next pageNumber; onPagerPrev at 0 is a no-op', () => {
      component.total = 5;
      component.pageSize = 2;
      component.pageIndex = 0;
      const callsBefore = adhocServiceSpy.getHistory.calls.count();
      component.onPagerNext();
      expect(component.pageIndex).toBe(1);
      expect(lastSentModel().pageNumber).toBe(2);
      component.pageIndex = 0;
      component.onPagerPrev();
      expect(component.pageIndex).toBe(0);
      expect(adhocServiceSpy.getHistory.calls.count()).toBe(callsBefore + 1);
    });

    it('page resets to 0 on any filter change (period, property, area, tag, custom date)', () => {
      component.pageIndex = 3;
      component.onPeriodChange('30');
      expect(component.pageIndex).toBe(0);

      component.pageIndex = 3;
      component.onPropertyChange(null);
      expect(component.pageIndex).toBe(0);

      component.pageIndex = 3;
      component.onAreaChange(null);
      expect(component.pageIndex).toBe(0);

      component.pageIndex = 3;
      component.onToggleTag(1);
      expect(component.pageIndex).toBe(0);

      component.pageIndex = 3;
      component.onCustomFromChange(new Date(2026, 0, 1));
      expect(component.pageIndex).toBe(0);
    });
  });

  describe('help panel toggle', () => {
    it('toggleHelpPanel flips helpPanelOpen (button label is template-driven off this flag)', () => {
      expect(component.helpPanelOpen).toBeFalse();
      component.toggleHelpPanel();
      expect(component.helpPanelOpen).toBeTrue();
      component.toggleHelpPanel();
      expect(component.helpPanelOpen).toBeFalse();
    });
  });

  describe('status chip derivation', () => {
    it('a wire status of Completed (1) derives the resolved/green chip and allows Arkiver', () => {
      const row = makeRow({status: AdhocTaskStatusFilter.Completed});
      expect(component.statusOf(row)).toBe('completed');
      expect(component.statusLabelKey(row)).toBe('Task resolved status');
      expect(component.statusChipClass(row)).toContain('status-chip--completed');
      expect(component.canArchive(row)).toBeTrue();
    });

    it('a wire status of Archived (2) derives the archived/grey chip and hides Arkiver', () => {
      const row = makeRow({status: AdhocTaskStatusFilter.Archived});
      expect(component.statusOf(row)).toBe('archived');
      expect(component.statusLabelKey(row)).toBe('archived');
      expect(component.statusChipClass(row)).toContain('status-chip--archived');
      expect(component.canArchive(row)).toBeFalse();
    });
  });

  describe('row actions', () => {
    it('onArchive calls archiveTask(taskId) and reloads on success', () => {
      adhocServiceSpy.archiveTask.and.returnValue(of({success: true}));
      const callsBefore = adhocServiceSpy.getHistory.calls.count();
      component.onArchive(makeRow({taskId: 42}));
      expect(adhocServiceSpy.archiveTask).toHaveBeenCalledWith(42);
      expect(adhocServiceSpy.getHistory.calls.count()).toBeGreaterThan(callsBefore);
    });

    it('onRowClick fetches the full task and opens the drawer in view mode', () => {
      const task = {id: 42, title: 'Fix roof'};
      adhocServiceSpy.getTask.and.returnValue(of({success: true, model: task}));
      dialogSpy.open.and.returnValue({afterClosed: () => of(false)});
      component.onRowClick(makeRow({taskId: 42}));
      expect(adhocServiceSpy.getTask).toHaveBeenCalledWith(42);
      const openArgs = dialogSpy.open.calls.mostRecent().args;
      expect(openArgs[1].data.mode).toBe('view');
      expect(openArgs[1].data.task).toBe(task);
    });

    it('onCopy opens the copy modal and, on a returned copy, opens the drawer in edit mode', () => {
      const copiedTask = {id: 99, title: 'Fix roof (copy)'};
      dialogSpy.open.and.returnValues(
        {afterClosed: () => of(copiedTask)},
        {afterClosed: () => of(true)},
      );
      component.onCopy(makeRow({taskId: 42, taskTitle: 'Fix roof'}));
      expect(dialogSpy.open).toHaveBeenCalledTimes(2);
      const drawerArgs = dialogSpy.open.calls.argsFor(1);
      expect(drawerArgs[1].data.mode).toBe('edit');
      expect(drawerArgs[1].data.task).toBe(copiedTask);
    });
  });
});
