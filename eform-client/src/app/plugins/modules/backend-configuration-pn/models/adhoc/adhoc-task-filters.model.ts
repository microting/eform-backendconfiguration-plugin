/** Mirrors C# `AdhocTaskStatusFilter`. */
export enum AdhocTaskStatusFilter {
  Open = 0,
  Completed = 1,
  Archived = 2,
}

/**
 * Wire-level filters for `POST adhoc/index`, mirroring C#
 * `AdhocTaskFiltersModel` (M5/P2) - every one of these is pushed into SQL
 * server-side, not applied in-memory. The UI-facing `AdhocFiltrationModel`
 * (ngrx, state/adhoc) uses a friendlier shape (string status/tagLogic); the
 * state facade maps between the two.
 */
export interface AdhocTaskFiltersModel {
  propertyId: number | null;
  areaId: number | null;
  status: AdhocTaskStatusFilter | null;
  tagIds: number[];

  /** true = task must have ALL of tagIds; false = ANY. */
  tagsMatchAll: boolean;

  searchText: string | null;

  pageNumber: number;
  pageSize: number;
  sortColumn: string | null;
  sortAscending: boolean;
}
