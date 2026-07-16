export type CalendarRepeatRule =
  | 'none'
  | 'daily'
  | 'weeklyOne'
  | 'weeklyAll'
  | 'weekdays'
  | 'monthlyDom'
  | 'monthlyByDay'
  | 'yearlyOne'
  | 'custom';

export interface CalendarTaskModel {
  id: number;
  title: string;
  startHour: number;         // fractional hour, e.g. 9.5 = 09:30
  duration: number;           // duration in hours
  startText: string;         // "09:30"
  endText: string;           // "10:00"
  tags: string[];
  assigneeIds: number[];
  workerNames: string[];
  // Worker-tag assignment (distinct from the item-planning `tags` above).
  // Backend CalendarTaskResponseModel.WorkerTagIds — ids only; the container
  // resolves them to display names (workerTagNames) using its `teams` list.
  workerTagIds?: number[];
  workerTagNames?: string[];
  boardId: number;
  color: string;
  descriptionHtml: string;
  // Per-language Title + Description for edit-mode prefill of the multi-language
  // fields (mirrors the backend CalendarTaskResponseModel.Translations).
  translations?: { languageId: number; name: string; description: string }[];
  repeatRule: CalendarRepeatRule;
  repeatMeta?: CalendarRepeatMeta;
  taskDate: string;          // ISO "YYYY-MM-DD"
  repeatSeriesId?: string;
  completed: boolean;
  status: boolean;
  complianceEnabled: boolean;
  driveLink?: string;
  propertyId: number;
  isFromCompliance?: boolean;
  complianceId?: number;
  deadline?: string;
  nextExecutionTime?: string;
  planningId?: number;
  isAllDay?: boolean;
  exceptionId?: number;

  // Surfaced from the AreaRulePlanning by the calendar `tasks/index` endpoint so
  // the "Opgaver og handlinger" table can resolve the eForm label and the
  // "Overskrift" (planning-tag) name from the option lists. Both optional —
  // populated by both the tasks/index and week endpoints.
  eformId?: number | null;
  itemPlanningTagId?: number | null;

  // Persisted custom-repeat fields surfaced from AreaRulePlanning so the
  // edit-modal can reconstruct a full CalendarRepeatMeta for an existing row.
  // All optional/nullable — older backends and rows without a custom rule
  // simply omit them.
  repeatType?: number | null;
  repeatEvery?: number | null;
  repeatEndMode?: number | null;
  repeatOccurrences?: number | null;
  repeatUntilDate?: string | null;
  dayOfWeek?: number | null;
  dayOfMonth?: number | null;
  repeatWeekdaysCsv?: string | null;
  repeatOrdinalWeek?: number | null;

  // File attachments persisted on the AreaRulePlanning. All occurrences of a
  // recurring rule share the same attachment list — see
  // 2026-05-06-calendar-event-attachments-design.md (Q1 master-rule scope).
  attachments?: CalendarTaskAttachment[];

  // Completed-case fields for the historical preview card (backend
  // CalendarTaskResponseModel.SdkCaseId / DoneByName / DoneAt). `sdkCaseId`
  // enables the Picture-thumbnail row; `doneByName` feeds the "Completed by"
  // group row. All optional — future/uncompleted rows omit them.
  sdkCaseId?: number | null;
  doneByName?: string | null;
  doneAt?: string | null;
}

export interface CalendarTaskAttachment {
  id: number;
  originalFileName: string;
  mimeType: string;
  sizeBytes: number;
  downloadUrl: string;

  // Drive-mirrored attachments. Both fields are null/undefined for
  // regular form-data uploads. The frontend uses `driveFileId` to render the
  // "Drive" badge and to synthesize the view-source link as
  // `https://drive.google.com/file/d/{driveFileId}/view` (the link is NOT
  // returned by the backend — it's deterministic from the id alone).
  driveFileId?: string;
  driveModifiedTime?: string;

  // PR-8: timestamp of the most recent successful refresh (the change-
  // processor advances `driveModifiedTime` on every accepted refetch — the
  // backend uses that as the proxy for "last refreshed at"). Used by the
  // attachment row's "Last refreshed N ago" label/tooltip.
  lastRefreshedAt?: string;

  // PR-8: true when the backing GoogleOAuthToken has been revoked (user
  // disconnected, or Google reported invalid_grant). The attachment row
  // renders a red "Google Drive disconnected — reconnect to resume sync"
  // badge in place of the regular Drive badge when this is set.
  driveRevoked?: boolean;
}

// Request models for the calendar "task list" page (POST /calendar/tasks/index).
// Mirrors the backend CalendarTaskListRequestModel: a filter block + pagination.
export interface CalendarTaskListFiltrationModel {
  propertyIds: number[];
  boardIds: number[];
  eformIds: number[];
  assignToIds: number[];
  tagIds: number[];
  status: boolean | null;
  complianceEnabled: boolean | null;
  nameFilter: string | null;
}

export interface CalendarTaskListPaginationModel {
  sort: string;
  isSortDsc: boolean;
}

export interface CalendarTaskIndexRequestModel {
  filters: CalendarTaskListFiltrationModel;
  pagination: CalendarTaskListPaginationModel;
}

export interface CalendarRepeatMeta {
  kind: string;
  n?: number;
  weekday?: number;
  weekdays?: number[];
  dom?: number;
  month?: number;
  ordinalWeek?: number;
  endMode: 'never' | 'after' | 'until';
  afterCount?: number;
  untilTs?: number;
}

export interface CalendarTaskLayoutModel extends CalendarTaskModel {
  // Google-Calendar-style equal-divide-with-overlap layout.
  // For N tasks in a conflict group:
  //   _width  = 100 * 1.8 / N  (overlap factor 1.8; cards overlap their neighbours)
  //   _left   = i * (100 - _width) / (N - 1)
  //   _zIndex = 10 + i  (later cards layer on top)
  // For N == 1, _left=0, _width=100, _zIndex=10.
  _left: number;    // left edge in % of day-column usable width
  _width: number;   // width in % of day-column usable width
  _zIndex: number;  // default stacking order within the conflict group
  // True when this card belongs to a multi-card overlap group. Drives the
  // click-to-raise behaviour (isInCascade) — geometry alone can't tell, since
  // the leftmost card in a group has _width === 100 just like a solo card.
  _inGroup?: boolean;
}

// Result returned by `PUT /calendar/tasks/{id}/complete`. Three shapes the
// frontend has to handle:
//  1) success && requiresForm === true — the linked eForm has mandatory
//     fields; the rest of the fields carry the route params for the
//     compliance/case form. Frontend navigates.
//  2) success && requiresForm === false — the eForm has no mandatory
//     fields; the backend already set Case.Status = 100 in place.
//     Frontend reloads the calendar so the event re-renders as completed.
//  3) success === false (carried on the wrapping OperationDataResult, not
//     this model) — covers non-compliance taps (TaskHasNoComplianceCase),
//     missing planning, uncomplete attempt, etc. Frontend silently no-ops
//     today; existing failure-path UX.
export interface CalendarToggleCompleteResult {
  requiresForm: boolean;
  sdkCaseId?: number;
  templateId?: number;
  propertyId?: number;
  complianceId?: number;
  workerId?: number;
  deadline?: string;
  /**
   * Calendar event start (Compliance.Deadline day + CalendarConfiguration.StartHour),
   * ISO 8601 UTC with millisecond precision. The compliance modal defaults its
   * doneAt picker to this value so Case.DoneAt records the scheduled moment
   * rather than when the user happened to click Save.
   */
  eventStart?: string;
}
