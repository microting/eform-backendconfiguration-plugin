import {CommonPaginationState} from 'src/app/common/models';
import {Action, createReducer, on} from '@ngrx/store';
import {
  adhocUpdateFilters,
  adhocUpdatePagination,
  adhocUpdateHiddenColumns,
  adhocUpdateHistoryFilters,
} from './adhoc.actions';

/**
 * Toolbar filter state for the "Adhoc overblik" task table (mockup
 * `.toolbar-card`). `status` mirrors the C# `AdhocTaskStatusFilter` enum
 * (Open/Completed/Archived) as a string union on the Angular side; the
 * facade (F5) maps it to the numeric wire value when building
 * `AdhocTaskFiltersModel`. `tagLogic` drives the tag popup's OG/ELLER
 * toggle (AND/OR), independent of the Historik tab's AND-only tag filter.
 */
export interface AdhocFiltrationModel {
  propertyId: number | null;
  areaId: number | null;
  status: 'open' | 'completed' | 'archived';
  tagIds: number[];
  tagLogic: 'and' | 'or';
  search: string;
}

/**
 * Historik tab filter state (mockup period chips 30d/60d/90d/6m/12m/24m +
 * custom range). `tagIds` here is AND-only per the mockup's
 * «Tags i historik (OG)» - unlike `AdhocFiltrationModel.tagLogic`.
 */
export interface AdhocHistoryFiltrationModel {
  periodPreset: '30d' | '60d' | '90d' | '6m' | '12m' | '24m' | 'custom';
  dateFrom: string | null;
  dateTo: string | null;
  propertyId: number | null;
  areaId: number | null;
  tagIds: number[];
}

export interface AdhocState {
  pagination: CommonPaginationState;
  filters: AdhocFiltrationModel;
  hiddenColumns: string[];
  historyFilters: AdhocHistoryFiltrationModel;
}

export const adhocInitialState: AdhocState = {
  pagination: {
    pageSize: 25,
    sort: 'CreatedAt',
    isSortDsc: true,
    offset: 0,
    pageIndex: 0,
    total: 0,
  },
  filters: {
    propertyId: null,
    areaId: null,
    status: 'open',
    tagIds: [],
    tagLogic: 'or',
    search: '',
  },
  hiddenColumns: [],
  historyFilters: {
    periodPreset: '30d',
    dateFrom: null,
    dateTo: null,
    propertyId: null,
    areaId: null,
    tagIds: [],
  },
};

export const _adhocReducer = createReducer(
  adhocInitialState,
  on(adhocUpdateFilters, (state, {payload}) => ({
      ...state,
      filters: {...state.filters, ...payload},
    }
  )),
  on(adhocUpdatePagination, (state, {payload}) => ({
      ...state,
      pagination: {...state.pagination, ...payload},
    }
  )),
  on(adhocUpdateHiddenColumns, (state, {payload}) => ({
      ...state,
      hiddenColumns: [...payload],
    }
  )),
  on(adhocUpdateHistoryFilters, (state, {payload}) => ({
      ...state,
      historyFilters: {...state.historyFilters, ...payload},
    }
  )),
);

export function adhocReducer(state: AdhocState | undefined, action: Action) {
  return _adhocReducer(state, action);
}
