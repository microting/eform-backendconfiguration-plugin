import {DeviceUserModel} from '../../models';

/** The caller's Medarbejdere rights, as every row of index-device-user carries them (Leder contract). */
export interface CallerFlags {
  /** Admin only: grant and revoke Leder. */
  canSetLeder: boolean;
  /** Admin or Leder: change worker groups (Medarbejdergrupper, D-L12). */
  canEditWorkerTags: boolean;
}

export const NO_CALLER_FLAGS: Readonly<CallerFlags> = Object.freeze({canSetLeder: false, canEditWorkerTags: false});

/**
 * Every row carries the same caller flags. With no rows (e.g. a filter that matches nothing) the flags the last
 * non-empty answer carried still hold; only before any such answer does the role decide (contract). The role alone
 * would take worker groups away from a Leder, who is not an admin.
 */
export function callerFlags(
  rows: ReadonlyArray<Partial<DeviceUserModel>> | null | undefined,
  isAdmin: boolean,
  previous: CallerFlags | null = null,
): CallerFlags {
  const first = rows?.[0];
  if (!first) {
    return previous ?? {canSetLeder: isAdmin, canEditWorkerTags: isAdmin};
  }
  return {canSetLeder: first.canSetLeder === true, canEditWorkerTags: first.canEditWorkerTags === true};
}

type RowWith<K extends keyof DeviceUserModel> = Partial<Pick<DeviceUserModel, K>>;

/**
 * The Leder toggle in the worker dialog (amendment §5): admins (canSetLeder) get it on every worker, always enabled.
 * There is no access condition (D-L16): a worker without login, Web or Arkiv can be a Leder. Non-admins never see it.
 */
export function showLeaderToggle(row: RowWith<'canSetLeder'>): boolean {
  return row.canSetLeder === true;
}

/** `isLeader` on save: the new value only when it changed, else null ("unchanged"). A missing stored value counts as off. */
export function leaderToSend(original: boolean | null, current: boolean | null): boolean | null {
  if (current === null) {
    return null;
  }
  return current === (original === true) ? null : current;
}

/** One cell of the Leder column. */
export interface LeaderCell {
  /** Translation key: Ja / Nej. */
  label: 'Yes' | 'No';
  /** The Archive column's status tags (APP_INSTALL_STATE_CLASS 'installed' / 'no-access'). */
  cssClass: 'tag-success' | 'tag-error';
  /** `data-leader`, for e2e. */
  state: 'yes' | 'no';
}

/** The Leder column (amendment §5) for every viewer: Ja only for a stored flag, anything else Nej. */
export function leaderCell(row: RowWith<'isLeader'>): LeaderCell {
  return row.isLeader === true
    ? {label: 'Yes', cssClass: 'tag-success', state: 'yes'}
    : {label: 'No', cssClass: 'tag-error', state: 'no'};
}

/** A Leder's login (e-mail) is changed by an admin only (H2): read-only for everyone else. */
export function isLeaderLoginLocked(row: RowWith<'canSetLeder' | 'isLeader'>): boolean {
  return row.canSetLeder !== true && row.isLeader === true;
}

/**
 * Worker groups on save. A caller who may not change them sends the stored set (the API refuses any difference,
 * D-L12); a disabled field (resigned worker) is missing from the form value and sends the stored set too.
 */
export function workerTagsToSend(
  row: RowWith<'canEditWorkerTags' | 'tags'>,
  edited: number[] | null | undefined,
): number[] {
  const stored = row.tags ?? [];
  return row.canEditWorkerTags === true ? [...(edited ?? stored)] : [...stored];
}

/** The row the create dialog starts from: no Leder yet, the caller's flags, no worker groups. */
export function newWorkerRow(flags: CallerFlags): Partial<DeviceUserModel> {
  return {...flags, isLeader: false, tags: []};
}
