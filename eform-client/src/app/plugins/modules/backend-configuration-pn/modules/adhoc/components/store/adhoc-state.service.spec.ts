import {of} from 'rxjs';
import {AdhocStateService} from './adhoc-state.service';
import {AdhocTaskStatusFilter} from '../../../../models';
import {adhocInitialState} from '../../../../state';

/**
 * Spy-store + spy-service unit test for `AdhocStateService` (M5/F5).
 * Authored, not run - jest runs from the host frontend only (repo
 * convention; see the F1-F4 report / `adhoc.reducer.spec.ts` for the same
 * caveat on this milestone).
 */
describe('AdhocStateService', () => {
  let service: AdhocStateService;
  let storeSpy: {select: jasmine.Spy; dispatch: jasmine.Spy};
  let adhocServiceSpy: jasmine.SpyObj<any>;

  function buildService(): AdhocStateService {
    // Constructed directly (not via TestBed), mirroring
    // backend-configuration-pn-adhoc.service.spec.ts's own pattern of
    // spying the constructor dependencies rather than bootstrapping a
    // TestBed module.
    return new AdhocStateService(storeSpy as any, adhocServiceSpy);
  }

  beforeEach(() => {
    storeSpy = {
      select: jasmine.createSpy('select').and.callFake((selector: any) => {
        if (selector.toString().includes('Filters')) {
          return of(adhocInitialState.filters);
        }
        if (selector.toString().includes('Pagination')) {
          return of(adhocInitialState.pagination);
        }
        if (selector.toString().includes('HiddenColumns')) {
          return of(adhocInitialState.hiddenColumns);
        }
        return of(undefined);
      }),
      dispatch: jasmine.createSpy('dispatch'),
    };
    adhocServiceSpy = jasmine.createSpyObj('BackendConfigurationPnAdhocService', [
      'getTasks',
      'getProperties',
      'getTags',
      'getAreas',
      'getWorkers',
    ]);
    service = buildService();
  });

  describe('getTasks (wire-model mapping)', () => {
    it('maps the ngrx status string to the numeric AdhocTaskStatusFilter', () => {
      adhocServiceSpy.getTasks.and.returnValue(of({success: true, model: {} as any}));
      service.getTasks();
      const sentModel = adhocServiceSpy.getTasks.calls.mostRecent().args[0];
      expect(sentModel.status).toBe(AdhocTaskStatusFilter.Open);
    });

    it('maps pageIndex (0-based) to pageNumber (1-based, per AdhocTaskFiltersModel.PageNumber default 1)', () => {
      adhocServiceSpy.getTasks.and.returnValue(of({success: true, model: {} as any}));
      service.currentPagination = {...service.currentPagination, pageIndex: 2, pageSize: 25};
      service.getTasks();
      const sentModel = adhocServiceSpy.getTasks.calls.mostRecent().args[0];
      expect(sentModel.pageNumber).toBe(3);
      expect(sentModel.pageSize).toBe(25);
    });

    it('maps tagLogic "and"/"or" to tagsMatchAll true/false', () => {
      adhocServiceSpy.getTasks.and.returnValue(of({success: true, model: {} as any}));
      service.currentFilters = {...service.currentFilters, tagLogic: 'and', tagIds: [1, 2]};
      service.getTasks();
      let sentModel = adhocServiceSpy.getTasks.calls.mostRecent().args[0];
      expect(sentModel.tagsMatchAll).toBeTrue();
      expect(sentModel.tagIds).toEqual([1, 2]);

      service.currentFilters = {...service.currentFilters, tagLogic: 'or'};
      service.getTasks();
      sentModel = adhocServiceSpy.getTasks.calls.mostRecent().args[0];
      expect(sentModel.tagsMatchAll).toBeFalse();
    });

    it('maps isSortDsc to sortAscending (inverted)', () => {
      adhocServiceSpy.getTasks.and.returnValue(of({success: true, model: {} as any}));
      service.currentPagination = {...service.currentPagination, isSortDsc: true, sort: 'Title'};
      service.getTasks();
      const sentModel = adhocServiceSpy.getTasks.calls.mostRecent().args[0];
      expect(sentModel.sortAscending).toBeFalse();
      expect(sentModel.sortColumn).toBe('Title');
    });
  });

  describe('onSortTable', () => {
    it('resets pageIndex/offset to 0 and dispatches the new sort', () => {
      service.currentPagination = {...service.currentPagination, sort: 'CreatedAt', isSortDsc: true, pageIndex: 3, offset: 75};
      service.onSortTable('Title');
      expect(storeSpy.dispatch).toHaveBeenCalled();
      const dispatched = storeSpy.dispatch.calls.mostRecent().args[0];
      expect(dispatched.payload.sort).toBe('Title');
      expect(dispatched.payload.isSortDsc).toBeFalse();
      expect(dispatched.payload.pageIndex).toBe(0);
      expect(dispatched.payload.offset).toBe(0);
    });
  });

  describe('changePage', () => {
    it('derives pageIndex from offset/pageSize', () => {
      service.changePage({total: 100, pageSize: 25, offset: 50});
      const dispatched = storeSpy.dispatch.calls.mostRecent().args[0];
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
      const filtersDispatch = storeSpy.dispatch.calls.argsFor(0)[0];
      const paginationDispatch = storeSpy.dispatch.calls.argsFor(1)[0];
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
      adhocServiceSpy.getProperties.and.returnValue(of({success: true, model: [{id: 1, name: 'Gård Nord'}]}));
      adhocServiceSpy.getTags.and.returnValue(of({success: true, model: [{id: 1, name: 'Vedligehold', isUserTag: false}]}));

      service.loadProperties().subscribe();
      service.loadTags().subscribe();

      expect(service.properties).toEqual([{id: 1, name: 'Gård Nord'}]);
      expect(service.tags).toEqual([{id: 1, name: 'Vedligehold', isUserTag: false}]);
    });

    it('getAreasForProperty/getWorkersForProperty cache per propertyId (second call does not re-hit the service)', () => {
      adhocServiceSpy.getAreas.and.returnValue(of({success: true, model: [{id: 10, propertyId: 1, name: 'Stald 1'}]}));
      adhocServiceSpy.getWorkers.and.returnValue(of({success: true, model: [{workerId: 100, displayName: 'Mette Hansen', propertyIds: [1]}]}));

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
      adhocServiceSpy.getAreas.and.returnValue(of({success: true, model: [{id: 10, propertyId: 1, name: 'Stald 1'}]}));
      service.getAreasForProperty(1).subscribe();
      service.properties = [{id: 1, name: 'Gård Nord'}];
      service.tags = [{id: 1, name: 'Vedligehold', isUserTag: false}];

      service.resetReferenceData();

      expect(service.properties).toEqual([]);
      expect(service.tags).toEqual([]);
      expect(service.getCachedAreas(1)).toEqual([]);
    });
  });
});
