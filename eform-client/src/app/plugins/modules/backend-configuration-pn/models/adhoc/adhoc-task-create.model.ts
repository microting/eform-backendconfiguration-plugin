/**
 * Mirrors C# `AdhocTaskCreateModel` - the fields a client submits on
 * `CreateTask`/`UpdateTask`, i.e. everything on `AdhocTaskModel` except the
 * server-stamped fields (id, createdBy, createdAt, completedBy, completedAt,
 * archivedAt, assignmentLog, comments).
 */
export interface AdhocTaskCreateModel {
  title: string;
  description: string;
  urgent: boolean;

  propertyId: number;
  areaId: number | null;

  tagIds: number[];

  /**
   * Ids of already-uploaded `AdhocTaskPhoto` rows to keep attached. On
   * create this is always effectively empty (a photo can only be uploaded,
   * via `uploadPhoto`, against an existing task id). On update this is the
   * reconciliation set: any existing photo row for the task whose id is
   * missing from this list is soft-deleted server-side.
   */
  photoIds: number[];

  visibleFrom: string | null;
  deadline: string | null;

  visibleReminder: boolean;
  deadlineReminder: boolean;
  deadlineReminderRepeat: number;
  visibleReminderTimeMinutes: number;
  deadlineReminderTimeMinutes: number;

  executionRule: number;
  assignedWorkerIds: number[];
}
