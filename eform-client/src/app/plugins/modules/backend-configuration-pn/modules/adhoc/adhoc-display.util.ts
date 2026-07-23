import {
  AdhocAreaModel,
  AdhocPropertyModel,
  AdhocTagModel,
  AdhocWorkerModel,
} from '../../models';

/**
 * Client-side id -> name resolution + display helpers for the Adhoc
 * dashboard (M5/F5). `AdhocTaskModel` (the wire model, M3-B2/M5-P2/P3)
 * carries only ids (`propertyId`, `areaId`, `createdByWorkerId`,
 * `assignedWorkerIds`, `tagIds`, `completedByWorkerId`) - no name
 * projections - so the table (adhoc-table.component) and drawer
 * (adhoc-task-drawer.component) resolve display names from the
 * reference-data lists the state facade (AdhocStateService) loads via
 * `getProperties`/`getAreas`/`getWorkers`/`getTags`. Kept as pure functions
 * (no DI) so they're trivially unit-testable and reusable from any
 * component.
 */

export function resolvePropertyName(
  properties: AdhocPropertyModel[],
  id: number | null | undefined
): string {
  if (id == null) {
    return '';
  }
  return properties.find((p) => p.id === id)?.name ?? '';
}

export function resolveAreaName(
  areas: AdhocAreaModel[],
  id: number | null | undefined
): string {
  if (id == null) {
    return '';
  }
  return areas.find((a) => a.id === id)?.name ?? '';
}

export function resolveWorkerName(
  workers: AdhocWorkerModel[],
  workerId: number | null | undefined
): string {
  if (workerId == null) {
    return '';
  }
  return workers.find((w) => w.workerId === workerId)?.displayName ?? '';
}

export function resolveWorkerNames(
  workers: AdhocWorkerModel[],
  workerIds: number[] | null | undefined
): string[] {
  return (workerIds ?? [])
    .map((id) => resolveWorkerName(workers, id))
    .filter((name) => !!name);
}

export function resolveTagNames(
  tags: AdhocTagModel[],
  tagIds: number[] | null | undefined
): string[] {
  return (tagIds ?? [])
    .map((id) => tags.find((t) => t.id === id)?.name)
    .filter((name): name is string => !!name);
}

export interface AssignedToDisplay {
  /** true when the task's `executionRule` is "everyone" (mockup: ALLE pill; no concrete assignees to resolve). */
  everyone: boolean;
  names: string[];
}

/**
 * Mirrors the mockup's `renderTildeltCell`/mobile's assignment display:
 * `executionRule` 1 ("everyone") means the task has no concrete assignees
 * and renders as a single "Alle"/"Everyone" pill; 0 ("assignedOnly") means
 * one pill per assigned worker's resolved name. NO teams (deviation from
 * the mockup/mobile, which also has a team concept - out of scope per the
 * M5 plan).
 */
export function assignedToDisplay(
  executionRule: number,
  assignedWorkerIds: number[] | null | undefined,
  workers: AdhocWorkerModel[]
): AssignedToDisplay {
  if (executionRule === 1) {
    return {everyone: true, names: []};
  }
  return {everyone: false, names: resolveWorkerNames(workers, assignedWorkerIds)};
}

export type DeadlineBand = 'none' | 'overdue' | 'today' | 'soon' | 'far';

export interface DeadlineVisual {
  band: DeadlineBand;
  /** 0-100: width of the progress-bar fill. */
  pct: number;
  /** Whole days from `now` to the deadline (negative = overdue, 0 = today). Null when there is no deadline. */
  days: number | null;
}

/**
 * Deadline progress-bar banding + colors - mirrors the mockup's
 * `deadlineVisual`/`sidsteFristBarColor` (dashboard-mockup/index.html)
 * exactly (band thresholds and fill-% formula), and the M5 plan's own
 * color assignments: overdue #b3261e, today #ef6c00, 1-5 days left
 * #f9a825, 6+ days #2e7d32.
 */
export const DEADLINE_BAND_COLORS: Record<DeadlineBand, string> = {
  overdue: '#b3261e',
  today: '#ef6c00',
  soon: '#f9a825',
  far: '#2e7d32',
  none: '#9e9e9e',
};

function dateOnly(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate());
}

function daysBetween(from: Date, to: Date): number {
  const ms = dateOnly(to).getTime() - dateOnly(from).getTime();
  return Math.round(ms / (24 * 60 * 60 * 1000));
}

export function deadlineVisual(
  deadline: string | null | undefined,
  now: Date = new Date()
): DeadlineVisual {
  if (!deadline) {
    return {band: 'none', pct: 0, days: null};
  }
  const deadlineDate = new Date(deadline);
  const d = daysBetween(now, deadlineDate);
  if (d < 0) {
    return {band: 'overdue', pct: 100, days: d};
  }
  if (d === 0) {
    return {band: 'today', pct: 94, days: 0};
  }
  if (d <= 5) {
    return {band: 'soon', pct: 88 - (d - 1) * 5, days: d};
  }
  const pct = Math.max(12, 58 - Math.min(d - 6, 24) * 1.5);
  return {band: 'far', pct: Math.round(pct), days: d};
}
