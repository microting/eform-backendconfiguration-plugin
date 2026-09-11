import {CalendarRepeatMeta, CalendarRepeatRule} from './calendar-task.model';

export interface CalendarTaskCreateModel {
  title: string;
  startHour: number;
  duration: number;
  tags: string[];
  assigneeIds: number[];
  boardId: number;
  color: string;
  descriptionHtml: string;
  repeatRule: CalendarRepeatRule;
  repeatMeta?: CalendarRepeatMeta;
  taskDate: string;
  driveLink?: string;
  propertyId: number;
  // Selected eForm (SDK CheckList id), null when the event has no eForm.
  // Declared explicitly so the field is compiler-checked: it used to reach the
  // backend only because the modal typed its payload `any`, which made a
  // rename/typo silently drop the eForm from the request.
  eformId: number | null;
  // CSV of JS getDay() weekday indices ("1,3,5") — only populated for
  // multi-day weekly custom rules. Cleared (sent as null) for any non-custom
  // rule so the backend column is wiped on rule change.
  repeatWeekdaysCsv?: string | null;
}

export interface CalendarTaskUpdateModel extends CalendarTaskCreateModel {
  id: number;
  repeatSeriesId?: string;
  // Pre-edit occurrence date ("YYYY-MM-DD") for scope-aware edits (#885).
  originalDate?: string;
}

export type RepeatEditScope = 'this' | 'thisAndFollowing' | 'all';
export type RepeatDeleteScope = 'this' | 'thisAndFollowing' | 'thisAndFollowingIncludingCompleted' | 'all';

/**
 * The POST body of `calendar/tasks/week` — the grid's only read path, shared by
 * the week/day/schedule load and by each of the six calls the month view's
 * forkJoin makes.
 *
 * An object rather than a positional argument list: the server model
 * (`CalendarTaskRequestModel`) has grown a filter per epic, and the last
 * positional form already ended in three same-typed collections, where a
 * transposed pair compiles cleanly and silently filters by the wrong thing.
 *
 * `siteIds` and `workerTagIds` are two assignee filters the server ORs (#1212),
 * but they are NOT symmetric. Before matching, the server widens the tag set to
 * `workerTagIds` plus every worker tag the selected `siteIds` belong to; a task
 * survives when its explicit assignees intersect `siteIds` OR its assigned
 * worker tags intersect that widened set.
 *
 * The widening runs one way only, sites → tags — there is no tags → sites
 * expansion. So picking an individual ALSO matches tasks assigned to a team
 * that individual is in, while picking a team does NOT match that team's
 * members' own individually-assigned events. An empty `workerTagIds` therefore
 * does not mean "no tag matching" whenever `siteIds` is non-empty.
 *
 * The lists are never intersected, and the client never post-filters —
 * everything the grid narrows by travels on this model.
 *
 * `tagNames` is the planning/eForm tag filter and has nothing to do with
 * `workerTagIds`; see the note on the server model of the same name.
 */
export interface CalendarTaskWeekRequestModel {
  propertyId: number;
  weekStart: string;
  weekEnd: string;
  boardIds: number[];
  tagNames: string[];
  /** Explicit assignee (site) ids. Empty = no narrowing by individual. */
  siteIds: number[];
  /** Worker-tag ("team") ids. Empty = no narrowing by team. */
  workerTagIds: number[];
}
