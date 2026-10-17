/** Mirrors Microting.EformBackendConfigurationBase DocumentDateState. */
export enum DocumentDateState {
  None = 0,
  /** Suggested by the inbox/hub and not yet approved by a person: listed under "Dato skal tjekkes". */
  Suggested = 1,
  Approved = 2,
}

/** A medarbejdergruppe (SDK worker tag) as the archive API returns it. */
export interface ArchiveNamedItem {
  id: number;
  name: string;
}

/**
 * GET archive-folders row (contract, "Plan 2 additions"). `readGroups` are the folder's EFFECTIVE groups
 * (its own, else its parent's; empty = open); `ownGroups` says whether the folder sets them itself
 * (false + restricted = "Arver fra <parent>"). `count`: readable documents linked to the selected
 * properties; a top folder includes its subfolders.
 */
export interface ArchiveFolderModel {
  id: number;
  name: string;
  parentId: number | null;
  isDefault: boolean;
  count: number;
  readGroups: ArchiveNamedItem[];
  ownGroups: boolean;
  restricted: boolean;
}

export interface ArchiveFolderCreateModel {
  name: string;
  parentId: number | null;
}

/** `parentId: null` means "unchanged"; `moveToTop: true` moves the folder to the top level. */
export interface ArchiveFolderUpdateModel {
  name?: string | null;
  parentId?: number | null;
  moveToTop?: boolean;
}

export interface ArchiveFolderAccessModel {
  tagIds: number[];
}

/** Documents whose effective read access changed (D11). */
export interface AccessChangeModel {
  accessChanged: number;
}

export interface FilesMoveModel {
  fileIds: number[];
  folderId: number;
}

export interface FilesMoveResultModel {
  moved: number;
  accessChanged: number;
}

/** `date` is `yyyy-MM-dd` (approves it) or null (clears it). */
export interface FileDateModel {
  date: string | null;
}

/** `ownAccess: false` clears the documents' own groups (they inherit the folder's again). */
export interface FilesAccessModel {
  fileIds: number[];
  ownAccess: boolean;
  tagIds: number[];
}

/** PUT files/properties: replaces the document's properties. */
export interface FilesUpdatePropertiesModel {
  id: number;
  properties: number[];
}
