import {of} from 'rxjs';
import {AdhocStateService} from './adhoc-state.service';
import {AdhocTaskStatusFilter} from '../../../../models';
import {
  adhocInitialState,
  selectAdhocFilters,
  selectAdhocHiddenColumns,
  selectAdhocPagination,
} from '../../../../state';

/**
 * Spy-store + spy-service unit test for `AdhocStateService` (M5/F5).
 * Authored, not run - jest runs from the host frontend only (repo
 * convention; see the F1-F4 report / `adhoc.reducer.spec.ts` for the same
 * caveat on this milestone).
 */
describe('AdhocStateService', () => {
  let service: AdhocStateService;
  let storeSpy: {select: jest.Mock; dispatch: jest.Mock};
  let adhocServiceSpy: any;

  function buildService(): AdhocStateService {
    // Constructed directly (not via TestBed), mirroring
    // backend-configuration-pn-adhoc.service.spec.ts's own pattern of
    // spying the constructor dependencies rather than bootstrapping a
    // TestBed module.
    return new AdhocStateService(storeSpy as any, adhocServiceSpy);
  }

  beforeEach(() => {
    storeSpy = {
      // Match selectors by identity — the memoized functions createSelector
      // returns do not stringify to anything containing the selector name,
      // so toString()-based matching silently returns undefined for all
      // three slices.
      select: jest.fn().mockImplementation((selector: any) => {
        if (selector === selectAdhocFilters) {
          return of(adhocInitialState.filters);
        }
        if (selector === selectAdhocPagination) {
          return of(adhocInitialState.pagination);
        }
        if (selector === selectAdhocHiddenColumns) {
          return of(adhocInitialState.hiddenColumns);
        }
        return of(undefined);
      }),
      dispatch: jest.fn(),
    };
    adhocServiceSpy = {
      getTasks: jest.fn(),
      getProperties: jest.fn(),
      getTags: jest.fn(),
      getAreas: jest.fn(),
      getWorkers: jest.fn(),
      createAreas: jest.fn(),
      renameArea: jest.fn(),
      deleteArea: jest.fn(),
    };
    service = buildService();
  });

  describe('getTasks (wire-model mapping)', () => {
    it('maps the ngrx status string to the numeric AdhocTaskStatusFilter', () => {
      adhocServiceSpy.getTasks.mockReturnValue(of({success: true, model: {} as any}));
      service.getTasks();
      const sentModel = adhocServiceSpy.getTasks.mock.lastCall[0];
      expect(sentModel.status).toBe(AdhocTaskStatusFilter.Open);
    });

    it('maps pageIndex (0-based) to pageNumber (1-based, per AdhocTaskFiltersModel.PageNumber default 1)', () => {
      adhocServiceSpy.getTasks.mockReturnValue(of({success: true, model: {} as any}));
      service.currentPagination = {...service.currentPagination, pageIndex: 2, pageSize: 25};
      service.getTasks();
      const sentModel = adhocServiceSpy.getTasks.mock.lastCall[0];
      expect(sentModel.pageNumber).toBe(3);
      expect(sentModel.pageSize).toBe(25);
    });

    it('maps tagLogic "and"/"or" to tagsMatchAll true/false', () => {
      adhocServiceSpy.getTasks.mockReturnValue(of({success: true, model: {} as any}));
      service.currentFilters = {...service.currentFilters, tagLogic: 'and', tagIds: [1, 2]};
      service.getTasks();
      let sentModel = adhocServiceSpy.getTasks.mock.lastCall[0];
      expect(sentModel.tagsMatchAll).toBe(true);
      expect(sentModel.tagIds).toEqual([1, 2]);

      service.currentFilters = {...service.currentFilters, tagLogic: 'or'};
      service.getTasks();
      sentModel = adhocServiceSpy.getTasks.mock.lastCall[0];
      expect(sentModel.tagsMatchAll).toBe(false);
    });

    it('dispatches the fetched total into the pagination state (I2: eform-pagination [length])', () => {
      adhocServiceSpy.getTasks.mockReturnValue(
        of({success: true, model: {total: 42, entities: [], openCount: 0, completedCount: 0, archivedCount: 0} as any})
      );
      service.getTasks().subscribe();
      expect(storeSpy.dispatch).toHaveBeenCalled();
      const dispatched = storeSpy.dispatch.mock.lastCall[0];
      expect(dispatched.type).toBe('[Adhoc] Update pagination');
      expect(dispatched.payload.total).toBe(42);
    });

    it('does not dispatch pagination state when the request fails', () => {
      adhocServiceSpy.getTasks.mockReturnValue(of({success: false} as any));
      service.getTasks().subscribe();
      expect(storeSpy.dispatch).not.toHaveBeenCalled();
    });

    it('maps isSortDsc to sortAscending (inverted)', () => {
      adhocServiceSpy.getTasks.mockReturnValue(of({success: true, model: {} as any}));
      service.currentPagination = {...service.currentPagination, isSortDsc: true, sort: 'Title'};
      service.getTasks();
      const sentModel = adhocServiceSpy.getTasks.mock.lastCall[0];
      expect(sentModel.sortAscending).toBe(false);
      expect(sentModel.sortColumn).toBe('Title');
    });
  });

  describe('onSortTable', () => {
    it('resets pageIndex/offset to 0 and dispatches the new sort', () => {
      service.currentPagination = {...service.currentPagination, sort: 'CreatedAt', isSortDsc: true, pageIndex: 3, offset: 75};
      service.onSortTable('Title');
      expect(storeSpy.dispatch).toHaveBeenCalled();
      const dispatched = storeSpy.dispatch.mock.lastCall[0];
      expect(dispatched.payload.sort).toBe('Title');
      expect(dispatched.payload.isSortDsc).toBe(false);
      expect(dispatched.payload.pageIndex).toBe(0);
      expect(dispatched.payload.offset).toBe(0);
    });
  });

  describe('changePage', () => {
    it('derives pageIndex from offset/pageSize', () => {
      service.changePage({total: 100, pageSize: 25, offset: 50});
      const dispatched = storeSpy.dispatch.mock.lastCall[0];
      expect(dispatched.payload.pageIndex).toBe(2);
      expect(dispatched.payload.offset).toBe(50);
      expect(dispatched.payload.pageSize).toBe(25);
    });
  });

  describe('updateFilters', () => {
    it('dispatches the merged filters and resets pagination to page 1', () => {
      service.currentPagination = {...service.currentPagination, pageIndex: 4, offset: 100};
      service.updateFilters({search: 'roof'});
      expect(storeSpy.dispatch).toHaveBeenCalledTimes(2);
      const filtersDispatch = storeSpy.dispatch.mock.calls[0][0];
      const paginationDispatch = storeSpy.dispatch.mock.calls[1][0];
      expect(filtersDispatch.payload.search).toBe('roof');
      expect(paginationDispatch.payload.pageIndex).toBe(0);
      expect(paginationDispatch.payload.offset).toBe(0);
    });

    it('is a no-op when the merged filters are unchanged', () => {
      service.updateFilters({search: service.currentFilters.search});
      expect(storeSpy.dispatch).not.toHaveBeenCalled();
    });
  });

  describe('reference data caches', () => {
    it('loadProperties/loadTags populate the facade fields from the response model', () => {
      adhocServiceSpy.getProperties.mockReturnValue(of({success: true, model: [{id: 1, name: 'Gård Nord'}]}));
      adhocServiceSpy.getTags.mockReturnValue(of({success: true, model: [{id: 1, name: 'Vedligehold', isUserTag: false}]}));

      service.loadProperties().subscribe();
      service.loadTags().subscribe();

      expect(service.properties).toEqual([{id: 1, name: 'Gård Nord'}]);
      expect(service.tags).toEqual([{id: 1, name: 'Vedligehold', isUserTag: false}]);
    });

    it('getAreasForProperty/getWorkersForProperty cache per propertyId (second call does not re-hit the service)', () => {
      adhocServiceSpy.getAreas.mockReturnValue(of({success: true, model: [{id: 10, propertyId: 1, name: 'Stald 1'}]}));
      adhocServiceSpy.getWorkers.mockReturnValue(of({success: true, model: [{workerId: 100, displayName: 'Mette Hansen', propertyIds: [1]}]}));

      service.getAreasForProperty(1).subscribe();
      service.getAreasForProperty(1).subscribe();
      service.getWorkersForProperty(1).subscribe();
      service.getWorkersForProperty(1).subscribe();

      expect(adhocServiceSpy.getAreas).toHaveBeenCalledTimes(1);
      expect(adhocServiceSpy.getWorkers).toHaveBeenCalledTimes(1);
      expect(service.getCachedAreas(1)).toEqual([{id: 10, propertyId: 1, name: 'Stald 1'}]);
      expect(service.getCachedWorkers(1)).toEqual([{workerId: 100, displayName: 'Mette Hansen', propertyIds: [1]}]);
    });

    it('resetReferenceData clears every cache', () => {
      adhocServiceSpy.getAreas.mockReturnValue(of({success: true, model: [{id: 10, propertyId: 1, name: 'Stald 1'}]}));
      service.getAreasForProperty(1).subscribe();
      service.properties = [{id: 1, name: 'Gård Nord'}];
      service.tags = [{id: 1, name: 'Vedligehold', isUserTag: false}];

      service.resetReferenceData();

      expect(service.properties).toEqual([]);
      expect(service.tags).toEqual([]);
      expect(service.getCachedAreas(1)).toEqual([]);
    });
  });

  describe('area mutations (active cache refresh)', () => {
    it('createAreas overwrites the cache from the create response (not merely invalidating it)', () => {
      adhocServiceSpy.getAreas.mockReturnValue(of({success: true, model: [{id: 10, propertyId: 1, name: 'Stald 1'}]}));
      service.getAreasForProperty(1).subscribe();
      expect(service.getCachedAreas(1)).toEqual([{id: 10, propertyId: 1, name: 'Stald 1'}]);

      adhocServiceSpy.createAreas.mockReturnValue(
        of({success: true, model: [{id: 10, propertyId: 1, name: 'Stald 1'}, {id: 11, propertyId: 1, name: 'Stald 2'}]})
      );
      service.createAreas(1, ['Stald 2']).subscribe();

      expect(adhocServiceSpy.createAreas).toHaveBeenCalledWith(1, ['Stald 2']);
      expect(service.getCachedAreas(1)).toEqual([
        {id: 10, propertyId: 1, name: 'Stald 1'},
        {id: 11, propertyId: 1, name: 'Stald 2'},
      ]);
    });

    it('createAreas leaves the cache untouched (does not poison it with []) when the create reports failure', () => {
      adhocServiceSpy.getAreas.mockReturnValue(of({success: true, model: [{id: 10, propertyId: 1, name: 'Stald 1'}]}));
      service.getAreasForProperty(1).subscribe();
      expect(service.getCachedAreas(1)).toEqual([{id: 10, propertyId: 1, name: 'Stald 1'}]);

      adhocServiceSpy.createAreas.mockReturnValue(of({success: false, model: null}));
      let emitted: any;
      service.createAreas(1, ['Stald 2']).subscribe((areas) => (emitted = areas));

      expect(emitted).toEqual([]);
      expect(service.getCachedAreas(1)).toEqual([{id: 10, propertyId: 1, name: 'Stald 1'}]);
    });

    it('renameArea re-fetches via getAreas and overwrites the cache with the fresh list (active refresh, not invalidate-only)', () => {
      adhocServiceSpy.getAreas.mockReturnValue(of({success: true, model: [{id: 10, propertyId: 1, name: 'Stald 1'}]}));
      service.getAreasForProperty(1).subscribe();
      expect(service.getCachedAreas(1)).toEqual([{id: 10, propertyId: 1, name: 'Stald 1'}]);

      adhocServiceSpy.renameArea.mockReturnValue(of({success: true, model: {id: 10, propertyId: 1, name: 'Stald renamed'}}));
      adhocServiceSpy.getAreas.mockReturnValue(of({success: true, model: [{id: 10, propertyId: 1, name: 'Stald renamed'}]}));

      let result: boolean | undefined;
      service.renameArea(1, 10, 'Stald renamed').subscribe((success) => (result = success));

      expect(adhocServiceSpy.renameArea).toHaveBeenCalledWith(10, 'Stald renamed');
      // Cache is not merely cleared - it is repopulated with the re-fetched entities.
      expect(adhocServiceSpy.getAreas).toHaveBeenCalledWith(1);
      expect(service.getCachedAreas(1)).toEqual([{id: 10, propertyId: 1, name: 'Stald renamed'}]);
      expect(result).toBe(true);
    });

    it('deleteArea re-fetches via getAreas and overwrites the cache with the fresh list (active refresh, not invalidate-only)', () => {
      adhocServiceSpy.getAreas.mockReturnValue(
        of({success: true, model: [{id: 10, propertyId: 1, name: 'Stald 1'}, {id: 11, propertyId: 1, name: 'Stald 2'}]})
      );
      service.getAreasForProperty(1).subscribe();
      expect(service.getCachedAreas(1).length).toBe(2);

      adhocServiceSpy.deleteArea.mockReturnValue(of({success: true}));
      adhocServiceSpy.getAreas.mockReturnValue(of({success: true, model: [{id: 11, propertyId: 1, name: 'Stald 2'}]}));

      let result: boolean | undefined;
      service.deleteArea(1, 10).subscribe((success) => (result = success));

      expect(adhocServiceSpy.deleteArea).toHaveBeenCalledWith(10);
      expect(adhocServiceSpy.getAreas).toHaveBeenCalledWith(1);
      expect(service.getCachedAreas(1)).toEqual([{id: 11, propertyId: 1, name: 'Stald 2'}]);
      expect(result).toBe(true);
    });

    it('renameArea propagates failure while still refreshing the cache', () => {
      adhocServiceSpy.renameArea.mockReturnValue(of({success: false}));
      adhocServiceSpy.getAreas.mockReturnValue(of({success: true, model: [{id: 10, propertyId: 1, name: 'Stald 1'}]}));

      let result: boolean | undefined;
      service.renameArea(1, 10, 'x').subscribe((success) => (result = success));

      expect(result).toBe(false);
      expect(service.getCachedAreas(1)).toEqual([{id: 10, propertyId: 1, name: 'Stald 1'}]);
    });
  });
});
