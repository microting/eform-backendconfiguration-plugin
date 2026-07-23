/**
 * Mirrors C# `AdhocTagModel`. `isUserTag` is caller-relative: true when the
 * tag's owner is the worker who asked for it (a personal tag); false for
 * global tags (dashboard-admin-created, per M5/P3's admin-global semantics).
 */
export interface AdhocTagModel {
  id: number;
  name: string;
  isUserTag: boolean;
}

/** Body for `POST tags` / `PUT tags/{id}`. */
export interface AdhocTagCreateModel {
  name: string;
}
