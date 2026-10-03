/** #1380: mirrors the backend WorkerTagsBulkMode. There is no "replace" on purpose. */
export enum WorkerTagsBulkMode {
  Add = 0,
  Remove = 1,
}

/** Body of PUT properties/assignment/bulk-tags (#1380). */
export interface WorkerTagsBulkUpdateModel {
  siteIds: number[];
  tagIds: number[];
  mode: WorkerTagsBulkMode;
}
