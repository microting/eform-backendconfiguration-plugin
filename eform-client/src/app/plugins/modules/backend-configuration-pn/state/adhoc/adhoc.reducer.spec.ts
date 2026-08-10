import {adhocReducer, adhocInitialState, AdhocFiltrationModel, AdhocHistoryFiltrationModel} from './adhoc.reducer';
import {
  adhocUpdateFilters,
  adhocUpdatePagination,
  adhocUpdateHiddenColumns,
  adhocUpdateHistoryFilters,
} from './adhoc.actions';
import {CommonPaginationState} from 'src/app/common/models';

describe('adhocReducer', () => {
  it('returns the initial state for an unknown action', () => {
    const state = adhocReducer(undefined, {type: '@@INIT'} as any);
    expect(state).toEqual(adhocInitialState);
  });

  it('initial state defaults to status "open", sort "CreatedAt" desc, no hidden columns, period "90"', () => {
    expect(adhocInitialState.filters.status).toBe('open');
    expect(adhocInitialState.pagination.sort).toBe('CreatedAt');
    expect(adhocInitialState.pagination.isSortDsc).toBeTrue();
    expect(adhocInitialState.hiddenColumns).toEqual([]);
    // Mockup default chip: "90 dage" (#1095).
    expect(adhocInitialState.historyFilters.periodPreset).toBe('90');
    expect(adhocInitialState.historyFilters.customFrom).toBeNull();
    expect(adhocInitialState.historyFilters.customTo).toBeNull();
  });

  describe('adhocUpdateFilters', () => {
    it('merges the payload into filters, leaving other filter fields untouched', () => {
      const payload: AdhocFiltrationModel = {
        ...adhocInitialState.filters,
        search: 'leak',
      };

      const state = adhocReducer(adhocInitialState, adhocUpdateFilters(payload));

      expect(state.filters.search).toBe('leak');
      expect(state.filters.status).toBe('open');
      expect(state.filters.tagLogic).toBe('or');
    });

    it('does not mutate pagination, hiddenColumns or historyFilters', () => {
      const payload: AdhocFiltrationModel = {...adhocInitialState.filters, status: 'completed'};

      const state = adhocReducer(adhocInitialState, adhocUpdateFilters(payload));

      expect(state.pagination).toEqual(adhocInitialState.pagination);
      expect(state.hiddenColumns).toEqual(adhocInitialState.hiddenColumns);
      expect(state.historyFilters).toEqual(adhocInitialState.historyFilters);
    });

    it('switching status does not reset tagIds or search', () => {
      const withSearch = adhocReducer(
        adhocInitialState,
        adhocUpdateFilters({...adhocInitialState.filters, search: 'roof', tagIds: [1, 2]})
      );

      const state = adhocReducer(
        withSearch,
        adhocUpdateFilters({...withSearch.filters, status: 'archived'})
      );

      expect(state.filters.status).toBe('archived');
      expect(state.filters.search).toBe('roof');
      expect(state.filters.tagIds).toEqual([1, 2]);
    });
  });

  describe('adhocUpdatePagination', () => {
    it('merges the payload into pagination', () => {
      const payload: CommonPaginationState = {
        ...adhocInitialState.pagination,
        pageIndex: 2,
        offset: 50,
      };

      const state = adhocReducer(adhocInitialState, adhocUpdatePagination(payload));

      expect(state.pagination.pageIndex).toBe(2);
      expect(state.pagination.offset).toBe(50);
      expect(state.pagination.pageSize).toBe(25);
    });

    it('does not mutate filters, hiddenColumns or historyFilters', () => {
      const payload: CommonPaginationState = {...adhocInitialState.pagination, sort: 'Title', isSortDsc: false};

      const state = adhocReducer(adhocInitialState, adhocUpdatePagination(payload));

      expect(state.filters).toEqual(adhocInitialState.filters);
      expect(state.hiddenColumns).toEqual(adhocInitialState.hiddenColumns);
      expect(state.historyFilters).toEqual(adhocInitialState.historyFilters);
    });
  });

  describe('adhocUpdateHiddenColumns', () => {
    it('replaces hiddenColumns wholesale (not a merge)', () => {
      const withColumns = adhocReducer(adhocInitialState, adhocUpdateHiddenColumns(['propertyName', 'areaName']));
      expect(withColumns.hiddenColumns).toEqual(['propertyName', 'areaName']);

      const state = adhocReducer(withColumns, adhocUpdateHiddenColumns(['tags']));
      expect(state.hiddenColumns).toEqual(['tags']);
    });

    it('an empty array clears all hidden columns', () => {
      const withColumns = adhocReducer(adhocInitialState, adhocUpdateHiddenColumns(['propertyName']));
      const state = adhocReducer(withColumns, adhocUpdateHiddenColumns([]));
      expect(state.hiddenColumns).toEqual([]);
    });
  });

  describe('adhocUpdateHistoryFilters', () => {
    it('merges the payload into historyFilters', () => {
      const payload: AdhocHistoryFiltrationModel = {
        ...adhocInitialState.historyFilters,
        periodPreset: 'custom',
        customFrom: '2026-01-01',
        customTo: '2026-06-30',
      };

      const state = adhocReducer(adhocInitialState, adhocUpdateHistoryFilters(payload));

      expect(state.historyFilters.periodPreset).toBe('custom');
      expect(state.historyFilters.customFrom).toBe('2026-01-01');
      expect(state.historyFilters.customTo).toBe('2026-06-30');
    });

    it('AND-only tagIds on history filters are stored independent of the table filters tagLogic', () => {
      const state = adhocReducer(
        adhocInitialState,
        adhocUpdateHistoryFilters({...adhocInitialState.historyFilters, tagIds: [3, 4]})
      );

      expect(state.historyFilters.tagIds).toEqual([3, 4]);
      expect(state.filters.tagLogic).toBe('or');
    });

    it('does not mutate filters, pagination or hiddenColumns', () => {
      const payload: AdhocHistoryFiltrationModel = {...adhocInitialState.historyFilters, propertyId: 7};

      const state = adhocReducer(adhocInitialState, adhocUpdateHistoryFilters(payload));

      expect(state.filters).toEqual(adhocInitialState.filters);
      expect(state.pagination).toEqual(adhocInitialState.pagination);
      expect(state.hiddenColumns).toEqual(adhocInitialState.hiddenColumns);
    });
  });
});
