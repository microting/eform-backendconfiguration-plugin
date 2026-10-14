import {ChemicalPermissionFlagsModel, ChemicalSetWorkerPermissionCommand, ChemicalWorkerPermissionModel} from '../../../models';

export type ChemicalFlagKey = keyof ChemicalPermissionFlagsModel;

/** Column order of the W4 matrix and app screen 8. */
export const CHEMICAL_FLAG_KEYS: ChemicalFlagKey[] = ['view', 'register', 'remove', 'stock', 'manageLocations', 'admin'];

export const NO_CHEMICAL_FLAGS: ChemicalPermissionFlagsModel = {
  view: false, register: false, remove: false, stock: false, manageLocations: false, admin: false,
};

/** One worker in the matrix: what the server has (stored) and what the admin is editing (current). */
export interface ChemicalPermissionRow {
  workerId: number;
  workerName: string;
  stored: ChemicalPermissionFlagsModel;
  current: ChemicalPermissionFlagsModel;
}

/** Admin implies every flag (spec §8). Stored flags are never rewritten by this. */
export function effectiveFlags(flags: ChemicalPermissionFlagsModel): ChemicalPermissionFlagsModel {
  return flags.admin
    ? {view: true, register: true, remove: true, stock: true, manageLocations: true, admin: true}
    : {...flags};
}

/** "Følger af Admin": locked, shown as a light dashed tick (mockup W4). */
export function isImpliedFlag(flags: ChemicalPermissionFlagsModel, key: ChemicalFlagKey): boolean {
  return flags.admin && key !== 'admin';
}

export function toggleFlag(flags: ChemicalPermissionFlagsModel, key: ChemicalFlagKey): ChemicalPermissionFlagsModel {
  return isImpliedFlag(flags, key) ? flags : {...flags, [key]: !flags[key]};
}

export function sameFlags(a: ChemicalPermissionFlagsModel, b: ChemicalPermissionFlagsModel): boolean {
  return CHEMICAL_FLAG_KEYS.every((key) => a[key] === b[key]);
}

export function buildPermissionRows(list: ChemicalWorkerPermissionModel[]): ChemicalPermissionRow[] {
  return [...(list ?? [])]
    .sort((a, b) => a.workerName.localeCompare(b.workerName, 'da'))
    .map((p) => ({workerId: p.workerId, workerName: p.workerName, stored: {...p.flags}, current: {...p.flags}}));
}

/**
 * The W4 save body: workers whose effective flags differ from the server's, with
 * their stored (unexpanded) flags. The server applies the whole list in one
 * transaction, so the order of the entries does not matter.
 */
export function changedPermissions(rows: ChemicalPermissionRow[]): ChemicalSetWorkerPermissionCommand[] {
  return rows
    .filter((r) => !sameFlags(effectiveFlags(r.current), effectiveFlags(r.stored)))
    .map((r) => ({workerId: r.workerId, flags: {...r.current}}));
}

/** "Kopiér fra anden ejendom" (spec §8): only workers on both properties change; nothing is saved. */
export function copyPermissionsFrom(rows: ChemicalPermissionRow[], source: ChemicalWorkerPermissionModel[]): ChemicalPermissionRow[] {
  const byWorker = new Map((source ?? []).map((p) => [p.workerId, p.flags]));
  return rows.map((r) => (byWorker.has(r.workerId) ? {...r, current: {...byWorker.get(r.workerId)}} : r));
}

export function resetPermissionRows(rows: ChemicalPermissionRow[]): ChemicalPermissionRow[] {
  return rows.map((r) => ({...r, current: {...r.stored}}));
}
