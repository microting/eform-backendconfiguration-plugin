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
 * The Leder choice for leaderToSend. `choice` is the admin's last choice (the toggle's value, or the value it had
 * before it was disabled); `changedByAdmin` says the admin flipped the toggle in this edit.
 * - Hidden: null ("no choice").
 * - Enabled, or changed by the admin: the choice, even if the toggle ends disabled. A deliberate untick is a revoke;
 *   a deliberate tick on a login that has since lost Web and Arkiv goes to the API, which refuses it.
 * - Disabled and not changed: null when it was disabled at open too (a managed Leder whose Web and Arkiv were
 *   already off would otherwise lose the role on an unrelated save); false when this edit switched access off, as
 *   the API revokes then.
 */
export function leaderChoice(
  state: LeaderToggleState,
  stateAtOpen: LeaderToggleState,
  choice: boolean,
  changedByAdmin: boolean,
): boolean | null {
  if (state === 'hidden') {
    return null;
  }
  if (state === 'enabled' || changedByAdmin) {
    return choice;
  }
  return stateAtOpen === 'disabled' ? null : false;
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
