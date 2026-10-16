import {ArchiveFolderModel, ArchiveNamedItem, FilesModel} from '../models';

/** Path separator in labels and breadcrumbs: "Drift › Fakturaer". */
export const ARCHIVE_PATH_SEPARATOR = ' › ';

/** The "Øverste niveau" choice when moving a folder; real folder ids start at 1. */
export const ARCHIVE_TOP_LEVEL_ID = 0;

export interface ArchiveFolderNode {
  folder: ArchiveFolderModel;
  children: ArchiveFolderModel[];
}

/** One entry of the 2-level folder picker; `label` is the full path, so search matches it. */
export interface ArchiveFolderOption {
  id: number;
  label: string;
  depth: 0 | 1;
  restricted: boolean;
  /** The effective groups, for the lock line in the picker. */
  groupsLabel: string;
  isDefault: boolean;
}

/** What an admin may do with a folder; reasons are translation keys shown in the ⋮ menu. */
export interface ArchiveFolderActions {
  canCreateSubfolder: boolean;
  createSubfolderReason: string | null;
  canMove: boolean;
  moveReason: string | null;
  canRestrict: boolean;
  restrictReason: string | null;
  canDelete: boolean;
  deleteReason: string | null;
  /** Counts for the delete reason's {{documents}} and {{subfolders}}. */
  deleteReasonParams: {documents: number; subfolders: number} | null;
}

export type ArchiveMovableFile = Pick<FilesModel, 'id' | 'folderId' | 'ownAccess' | 'readGroups'>;

export interface ArchiveMoveImpact {
  /** Documents whose effective read access the move changes. */
  changed: number;
  /** Who reads them afterwards (empty = everyone with archive access). */
  targetGroups: ArchiveNamedItem[];
}

function byName(a: ArchiveFolderModel, b: ArchiveFolderModel): number {
  return a.name.localeCompare(b.name, 'da', {sensitivity: 'base'});
}

export function findFolder(folders: ArchiveFolderModel[], id: number | null): ArchiveFolderModel | undefined {
  return id === null ? undefined : folders.find(f => f.id === id);
}

function parentOf(folders: ArchiveFolderModel[], folder: ArchiveFolderModel): ArchiveFolderModel | undefined {
  return findFolder(folders, folder.parentId);
}

/**
 * Top folders (the default folder first, then by Danish name) with their subfolders by name.
 * A subfolder whose parent was not returned is shown as a top folder rather than hidden.
 */
export function buildFolderTree(folders: ArchiveFolderModel[]): ArchiveFolderNode[] {
  return folders
    .filter(f => !parentOf(folders, f))
    .sort((a, b) => Number(b.isDefault) - Number(a.isDefault) || byName(a, b))
    .map(folder => ({folder, children: folders.filter(c => c.parentId === folder.id).sort(byName)}));
}

/** ["Drift", "Fakturaer"]; [] for an unknown id. */
export function folderPath(folders: ArchiveFolderModel[], id: number): string[] {
  const folder = findFolder(folders, id);
  if (!folder) {
    return [];
  }
  const parent = parentOf(folders, folder);
  return parent ? [parent.name, folder.name] : [folder.name];
}

export function folderPathLabel(folders: ArchiveFolderModel[], id: number): string {
  return folderPath(folders, id).join(ARCHIVE_PATH_SEPARATOR);
}

/**
 * The folder whose groups apply to `id`: itself when it sets its own groups, else its parent (inherited).
 * Null when it is open, or when it inherits from a parent that is not in the list.
 */
export function accessSourceFolder(folders: ArchiveFolderModel[], id: number): ArchiveFolderModel | null {
  const folder = findFolder(folders, id);
  if (!folder || !folder.readGroups.length) {
    return null;
  }
  if (folder.ownGroups) {
    return folder;
  }
  return parentOf(folders, folder) ?? null;
}

/** Who can read documents of `id` that have no own access; empty = everyone with archive access. */
export function effectiveFolderGroups(folders: ArchiveFolderModel[], id: number): ArchiveNamedItem[] {
  return findFolder(folders, id)?.readGroups ?? [];
}

/** The groups the folder sets itself — what the access dialog edits (inherited groups are not its own). */
export function ownFolderGroups(folder: ArchiveFolderModel): ArchiveNamedItem[] {
  return folder.ownGroups ? folder.readGroups : [];
}

/** Own groups replace the folder's (even an empty set, which opens the document). */
export function effectiveFileGroups(file: ArchiveMovableFile, folders: ArchiveFolderModel[]): ArchiveNamedItem[] {
  return file.ownAccess ? file.readGroups : effectiveFolderGroups(folders, file.folderId);
}

export function sameGroups(a: ArchiveNamedItem[], b: ArchiveNamedItem[]): boolean {
  const ids = new Set(a.map(g => g.id));
  return ids.size === new Set(b.map(g => g.id)).size && b.every(g => ids.has(g.id));
}

export function groupNames(groups: ArchiveNamedItem[]): string {
  return groups.map(g => g.name).join(', ');
}

/**
 * D11: own access never changes on a move. The "before" groups are the row's own `readGroups` (effective, from the
 * server), so a document whose folder is not in `folders` still counts correctly.
 */
export function moveImpact(files: ArchiveMovableFile[], folders: ArchiveFolderModel[], targetFolderId: number): ArchiveMoveImpact {
  const targetGroups = effectiveFolderGroups(folders, targetFolderId);
  const changed = files.filter(f =>
    !f.ownAccess
    // Only a short-cut: a document already in the target has the target's groups anyway.
    && f.folderId !== targetFolderId
    && !sameGroups(f.readGroups, targetGroups)).length;
  return {changed, targetGroups};
}

/** D2 + D12. Slet is disabled only on visible evidence (`count` is property-scoped); the server refuses the rest. */
export function folderActions(folder: ArchiveFolderModel, folders: ArchiveFolderModel[]): ArchiveFolderActions {
  const subfolders = folders.filter(f => f.parentId === folder.id).length;
  const hasChildren = subfolders > 0;
  const isSubfolder = folder.parentId !== null;
  const locked = folder.isDefault;
  const notEmpty = !locked && (hasChildren || folder.count > 0);

  let moveReason: string | null = null;
  if (locked) {
    moveReason = 'The default folder cannot be moved';
  } else if (hasChildren) {
    moveReason = 'At most 2 levels';
  }

  let deleteReason: string | null = null;
  if (locked) {
    deleteReason = 'The default folder cannot be deleted';
  } else if (notEmpty) {
    deleteReason = 'Only empty folders can be deleted ({{documents}} documents, {{subfolders}} subfolders)';
  }

  return {
    canCreateSubfolder: !isSubfolder,
    createSubfolderReason: isSubfolder ? 'At most 2 levels' : null,
    canMove: moveReason === null,
    moveReason,
    canRestrict: !locked,
    restrictReason: locked ? 'The default folder cannot have restricted read access' : null,
    canDelete: deleteReason === null,
    deleteReason,
    deleteReasonParams: notEmpty ? {documents: folder.count, subfolders} : null,
  };
}

/** Live groups, then current groups without live members, marked; never dropped (that would open the folder). */
export function accessGroupItems(liveGroups: ArchiveNamedItem[], currentGroups: ArchiveNamedItem[], noMembersLabel: string): ArchiveNamedItem[] {
  const liveIds = new Set(liveGroups.map(g => g.id));
  const stale = currentGroups
    .filter(g => !liveIds.has(g.id))
    .map(g => ({id: g.id, name: `${g.name} (${noMembersLabel})`}));
  return [...liveGroups, ...stale];
}

function toOption(folder: ArchiveFolderModel, folders: ArchiveFolderModel[]): ArchiveFolderOption {
  return {
    id: folder.id,
    label: folderPathLabel(folders, folder.id),
    depth: parentOf(folders, folder) ? 1 : 0,
    restricted: folder.restricted,
    groupsLabel: groupNames(effectiveFolderGroups(folders, folder.id)),
    isDefault: folder.isDefault,
  };
}

/** Both levels in tree order. */
export function folderOptions(folders: ArchiveFolderModel[]): ArchiveFolderOption[] {
  return buildFolderTree(folders)
    .flatMap(({folder, children}) => [folder, ...children])
    .map(f => toOption(f, folders));
}

/** Where `folder` may move: the top level (if it is a subfolder) or under another top folder. */
export function folderMoveOptions(folder: ArchiveFolderModel, folders: ArchiveFolderModel[], topLevelLabel: string): ArchiveFolderOption[] {
  const topLevel: ArchiveFolderOption[] = folder.parentId !== null
    ? [{id: ARCHIVE_TOP_LEVEL_ID, label: topLevelLabel, depth: 0, restricted: false, groupsLabel: '', isDefault: false}]
    : [];
  const parents = buildFolderTree(folders)
    .map(n => n.folder)
    .filter(f => f.id !== folder.id && f.id !== folder.parentId)
    .map(f => toOption(f, folders));
  return [...topLevel, ...parents];
}
