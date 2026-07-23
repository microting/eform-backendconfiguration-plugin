/**
 * Mirrors the canonical C# read/write models in
 * BackendConfiguration.Pn/Infrastructure/Models/Adhoc/AdhocTaskModel.cs
 * (M3-B2, extended M5-P2/P3) - field names/shapes are the contract; do not
 * invent fields the C# side doesn't have. Notably there are NO *Name
 * projections here (propertyName/areaName/createdByName/etc.) - the wire
 * model only carries ids (propertyId, areaId, createdByWorkerId,
 * assignedWorkerIds, ...); the table/drawer components resolve display
 * names client-side from the reference-data endpoints
 * (getProperties/getAreas/getWorkers/getTags).
 */
export interface AdhocTaskModel {
  id: number;
  createdByWorkerId: number;
  createdAt: string;

  title: string;
  description: string;
  urgent: boolean;

  propertyId: number;
  areaId: number | null;

  tagIds: number[];
  photos: AdhocTaskPhotoModel[];

  visibleFrom: string | null;
  deadline: string | null;

  visibleReminder: boolean;
  deadlineReminder: boolean;

  /** 0 = none, 1 = weekdays (mirrors AdhocTaskEntity.DeadlineReminderRepeat). */
  deadlineReminderRepeat: number;

  /** Minutes from midnight. */
  visibleReminderTimeMinutes: number;
  deadlineReminderTimeMinutes: number;

  /** 0 = assignedOnly, 1 = everyone (mirrors AdhocTaskEntity.ExecutionRule). */
  executionRule: number;

  assignedWorkerIds: number[];
  assignmentLog: AdhocAssignmentLogModel[];

  completed: boolean;
  completedByWorkerId: number | null;
  completedAt: string | null;

  archived: boolean;
  archivedAt: string | null;

  comments: AdhocCommentModel[];
}

/**
 * A photo attached to a task. `id` is the owning `AdhocTaskPhoto` row's PK
 * (what GET/DELETE photos/{id} addresses), not the underlying SDK
 * UploadedData id.
 */
export interface AdhocTaskPhotoModel {
  id: number;
  contentType: string;
}

export interface AdhocCommentModel {
  authorWorkerId: number;
  createdAt: string;
  text: string;
}

export interface AdhocAssignmentLogModel {
  changedByWorkerId: number;
  changedAt: string;
  fromWorkerIds: number[];
  toWorkerIds: number[];
}

/** Body for `POST {id}/completed`. */
export interface AdhocSetCompletedModel {
  completed: boolean;
  completedByWorkerId?: number | null;
}

/** Body for `POST {id}/comments`. */
export interface AdhocCommentCreateModel {
  text: string;
}

/** Body for `POST {id}/copy` (M5/P3). */
export interface AdhocCopyTaskModel {
  includeComments: boolean;
}
