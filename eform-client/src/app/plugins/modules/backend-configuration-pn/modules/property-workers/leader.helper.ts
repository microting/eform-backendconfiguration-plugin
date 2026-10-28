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

export type LeaderRow = Partial<Pick<DeviceUserModel, 'canSetLeder' | 'isAdminLogin' | 'isLeader' | 'loginIsManaged'>>;
type RowWith<K extends keyof DeviceUserModel> = Partial<Pick<DeviceUserModel, K>>;
export type LeaderToggleState = 'hidden' | 'disabled' | 'enabled';

/**
 * The Leder toggle in the worker dialog (contract "Web (Plan 3)"). Hidden for non-admins and on admin logins.
 * Disabled when the row has no login, or when the plugin manages the login and Web and Arkiv are both off (the API
 * would answer LederRequiresLogin). An owner-group login reads Web = Arkiv = off but has its own group: enabled.
 */
export function leaderToggleState(row: LeaderRow, webOn: boolean, archiveOn: boolean): LeaderToggleState {
  if (row.canSetLeder !== true || row.isAdminLogin === true) {
    return 'hidden';
  }
  if (row.isLeader === null || row.isLeader === undefined) {
    return 'disabled';
  }
  if (row.loginIsManaged === true && !webOn && !archiveOn) {
    return 'disabled';
  }
  return 'enabled';
}

/** `isLeader` on save: the new value only when it changed, else null ("unchanged"). No login (null) counts as off. */
export function leaderToSend(original: boolean | null, current: boolean | null): boolean | null {
  if (current === null) {
    return null;
  }
  return current === (original === true) ? null : current;
}

/**
 * The toggle's value as a choice for leaderToSend: null ("no choice") when it is hidden, or when it is disabled now
 * and was disabled when the dialog opened. A managed Leder whose Web and Arkiv were already off starts disabled at
 * false; saving that untouched would revoke the role. Only switching access off in this edit (enabled at open,
 * disabled now) sends the false, as the API revokes then.
 */
export function leaderChoice(state: LeaderToggleState, stateAtOpen: LeaderToggleState, value: boolean): boolean | null {
  if (state === 'hidden' || (state === 'disabled' && stateAtOpen === 'disabled')) {
    return null;
  }
  return value;
}

/** A Leder's login (e-mail) is changed by an admin only (spec H2): read-only for everyone else. */
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

/**
 * The row the create dialog starts from. The save creates a plugin-managed login when Web or Arkiv is on, so the
 * toggle follows those two; an address that links to an existing login is judged by the API (LederRequiresLogin).
 */
export function newWorkerRow(flags: CallerFlags): Partial<DeviceUserModel> {
  return {...flags, isLeader: false, loginIsManaged: true, isAdminLogin: false, tags: []};
}
