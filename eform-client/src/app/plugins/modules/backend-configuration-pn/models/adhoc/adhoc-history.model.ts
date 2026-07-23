import {AdhocHistoryFiltrationModel} from 'src/app/plugins/modules/backend-configuration-pn/state';

/**
 * One row of the Historik timeline - mirrors C#
 * `AdhocTaskHistoryEventModel` (M5/P2). `eventType` is one of: created,
 * assigned, completed, archived, commented. There is intentionally no
 * "reopened" event type - a reopened task simply returns to the open
 * bucket and disappears from Historik entirely (see the C# model's own doc
 * comment / m5-p1-p3-report.md for the full rationale).
 */
export interface AdhocTaskHistoryEventModel {
  taskId: number;
  taskTitle: string;

  eventType: 'created' | 'assigned' | 'completed' | 'archived' | 'commented';

  /**
   * ISO string on the wire, but a `Date` by the time it reaches components:
   * the host frontend's global DateInterceptor converts every ISO-datetime
   * string in every response body (same reality as e.g.
   * `DocumentModel.endDate`).
   */
  occurredAt: string | Date;

  actorName: string;

  detail: string | null;

  propertyName: string;
  areaName: string | null;

  tagNames: string[];

  lastCommentText: string | null;
  lastCommentAuthor: string | null;
  lastCommentAt: string | Date | null;

  completed: boolean;
  archived: boolean;
}

/**
 * Wire-level filters for `POST adhoc/history/index`, mirroring C#
 * `AdhocHistoryFiltersModel` (M5/P2). `tagIds` is AND-only, unlike
 * `AdhocTaskFiltersModel.tagsMatchAll`'s toggle.
 */
export interface AdhocHistoryFiltersModel {
  dateFrom: string | null;
  dateTo: string | null;

  propertyId: number | null;
  areaId: number | null;

  /** AND-only: a matching event's task must have ALL of these tags. */
  tagIds: number[];

  pageNumber: number;
  pageSize: number;
}

/**
 * Shape parity with `AdhocTaskRequestModel` - the UI-facing (ngrx) history
 * filters the state facade (F5/F8) maps to the wire-level
 * `AdhocHistoryFiltersModel` (date-from/to resolved from the period preset,
 * paging added).
 */
export interface AdhocHistoryRequestModel {
  filters: AdhocHistoryFiltrationModel;
}
