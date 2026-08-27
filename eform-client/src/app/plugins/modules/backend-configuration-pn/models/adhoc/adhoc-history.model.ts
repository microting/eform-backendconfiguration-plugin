import {AdhocHistoryFiltrationModel} from 'src/app/plugins/modules/backend-configuration-pn/state';
import {AdhocTaskStatusFilter} from './adhoc-task-filters.model';

/**
 * One row of the Historik table (#1095, mockup parity) - mirrors C#
 * `AdhocTaskHistoryRowModel`: one row per task currently Completed ("Løst")
 * or Archived, sorted by `completedAt` descending. Open tasks never appear.
 * Replaces the old per-event `AdhocTaskHistoryEventModel` (created/assigned/
 * completed/archived/commented timeline).
 */
export interface AdhocTaskHistoryRowModel {
  taskId: number;
  taskTitle: string;

  /**
   * The "Løst" date - never the archive date. ISO string on the wire, but a
   * `Date` by the time it reaches components: the host frontend's global
   * DateInterceptor converts every ISO-datetime string in every response
   * body (same reality as e.g. `DocumentModel.endDate`).
   */
  completedAt: string | Date;

  /** Empty string when the task was completed with no recorded performer. */
  completedByName: string;

  /**
   * Wire value of C# `AdhocTaskStatusFilter` - only `Completed` (1) or
   * `Archived` (2) ever occur in Historik. The component maps it to its
   * 'completed' | 'archived' display union.
   */
  status: AdhocTaskStatusFilter;

  propertyName: string;
  areaName: string | null;

  tagNames: string[];

  lastCommentText: string | null;
  lastCommentAuthor: string | null;
  lastCommentAt: string | Date | null;
}

/**
 * Wire-level filters for `POST adhoc/history/index`, mirroring C#
 * `AdhocHistoryFiltersModel` (#1095). `dateFrom`/`dateTo` are date-only
 * values compared date-truncated and inclusive of both ends against each
 * row's `completedAt`. `tagIds` is AND-only, unlike
 * `AdhocTaskFiltersModel.tagsMatchAll`'s toggle.
 */
export interface AdhocHistoryFiltersModel {
  dateFrom: string | null;
  dateTo: string | null;

  propertyId: number | null;
  areaId: number | null;

  /** AND-only: a matching row's task must have ALL of these tags. */
  tagIds: number[];

  pageNumber: number;
  pageSize: number;
}

/**
 * Shape parity with `AdhocTaskRequestModel` - the UI-facing (ngrx) history
 * filters (period preset + custom range + property/area/tags) the component
 * maps to the wire-level `AdhocHistoryFiltersModel` (date-from/to resolved
 * from the period preset, paging added).
 */
export interface AdhocHistoryRequestModel {
  filters: AdhocHistoryFiltrationModel;
}
