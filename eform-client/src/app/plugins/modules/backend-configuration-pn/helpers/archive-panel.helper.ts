import {DocumentDateState, FileModel} from '../models';
import {ServerDate, serverDay} from '../modules/tail-bite/shared/tail-bite-dates';

/** The editable state of the document panel. Dates are `yyyy-MM-dd`. */
export interface ArchivePanelValue {
  name: string;
  propertyIds: number[];
  tagIds: number[];
  folderId: number;
  documentDate: string | null;
  documentDateState: DocumentDateState;
  ownAccess: boolean;
  /** Own groups; empty while the document inherits its folder's. */
  readTagIds: number[];
}

/** One flag per API call the panel's save makes. */
export interface ArchivePanelChanges {
  nameOrProperties: boolean;
  tags: boolean;
  folder: boolean;
  date: boolean;
  access: boolean;
}

/** `yyyy-MM-dd` of a server date via `serverDay`; null when missing or an Invalid Date. */
export function dateOnlyOf(value: ServerDate | null | undefined): string | null {
  if (!value || (value instanceof Date && isNaN(value.getTime()))) {
    return null;
  }
  return serverDay(value);
}

export function panelValueOf(file: FileModel): ArchivePanelValue {
  return {
    name: file.fileName,
    propertyIds: [...(file.properties ?? [])],
    tagIds: (file.tags ?? []).map(t => t.id),
    folderId: file.folderId,
    documentDate: dateOnlyOf(file.documentDate),
    documentDateState: file.documentDateState ?? DocumentDateState.None,
    ownAccess: !!file.ownAccess,
    readTagIds: file.ownAccess ? (file.readGroups ?? []).map(g => g.id) : [],
  };
}

function sameIds(a: number[], b: number[]): boolean {
  const sortedA = [...a].sort((x, y) => x - y);
  const sortedB = [...b].sort((x, y) => x - y);
  return sortedA.length === sortedB.length && sortedA.every((id, i) => id === sortedB[i]);
}

export function panelChanges(original: ArchivePanelValue, edited: ArchivePanelValue): ArchivePanelChanges {
  return {
    nameOrProperties: original.name.trim() !== edited.name.trim() || !sameIds(original.propertyIds, edited.propertyIds),
    tags: !sameIds(original.tagIds, edited.tagIds),
    folder: original.folderId !== edited.folderId,
    date: original.documentDate !== edited.documentDate || original.documentDateState !== edited.documentDateState,
    access: original.ownAccess !== edited.ownAccess || (edited.ownAccess && !sameIds(original.readTagIds, edited.readTagIds)),
  };
}

export function hasPanelChanges(changes: ArchivePanelChanges): boolean {
  return Object.values(changes).some(Boolean);
}

/** Navn, Ejendomme and Mappe are required. */
export function isPanelValid(value: ArchivePanelValue): boolean {
  return value.name.trim().length > 0 && value.propertyIds.length > 0 && value.folderId > 0;
}
