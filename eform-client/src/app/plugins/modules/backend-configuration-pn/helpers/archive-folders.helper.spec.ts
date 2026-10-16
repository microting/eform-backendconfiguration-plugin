import {ArchiveFolderModel} from '../models';
import {
  ARCHIVE_TOP_LEVEL_ID,
  accessGroupItems,
  accessSourceFolder,
  buildFolderTree,
  effectiveFileGroups,
  effectiveFolderGroups,
  folderActions,
  folderMoveOptions,
  folderOptions,
  folderPath,
  folderPathLabel,
  findFolder,
  groupNames,
  moveImpact,
  ownFolderGroups,
  sameGroups,
} from './archive-folders.helper';

const ledelse = {id: 5, name: 'Ledelse'};
const bogholderi = {id: 6, name: 'Bogholderi'};

function folder(id: number, name: string, parentId: number | null = null, extra: Partial<ArchiveFolderModel> = {}): ArchiveFolderModel {
  return {id, name, parentId, isDefault: false, count: 0, readGroups: [], ownGroups: false, restricted: false, ...extra};
}

// The mockup tree: Arkiv (default), Drift › Fakturaer/Serviceaftaler, Miljø (Ledelse) › Tilsyn (inherits) /
// Kontrakter (own: Bogholderi), Økonomi (Bogholderi). readGroups are effective, as the API sends them.
const folders: ArchiveFolderModel[] = [
  folder(1, 'Arkiv', null, {isDefault: true, count: 214}),
  folder(2, 'Økonomi', null, {readGroups: [bogholderi], ownGroups: true, restricted: true, count: 22}),
  folder(3, 'Drift', null, {count: 46}),
  folder(4, 'Fakturaer', 3, {count: 31}),
  folder(5, 'Serviceaftaler', 3, {count: 9}),
  folder(6, 'Miljø', null, {readGroups: [ledelse], ownGroups: true, restricted: true, count: 18}),
  folder(7, 'Tilsyn', 6, {readGroups: [ledelse], restricted: true, count: 7}),
  folder(8, 'Kontrakter', 6, {readGroups: [bogholderi], ownGroups: true, restricted: true}),
  folder(9, 'Tom', null),
];

describe('archive folder helpers', () => {
  describe('buildFolderTree', () => {
    it('puts the default folder first, then top folders in Danish order', () => {
      expect(buildFolderTree(folders).map(n => n.folder.name)).toEqual(['Arkiv', 'Drift', 'Miljø', 'Tom', 'Økonomi']);
    });

    it('nests subfolders by name under their parent', () => {
      const drift = buildFolderTree(folders).find(n => n.folder.id === 3)!;
      expect(drift.children.map(c => c.name)).toEqual(['Fakturaer', 'Serviceaftaler']);
    });

    it('finds the default folder by isDefault, never by a fixed id', () => {
      const moved = [folder(3, 'Drift'), folder(42, 'Arkiv', null, {isDefault: true}), folder(2, 'Andet')];
      expect(buildFolderTree(moved).map(n => n.folder.id)).toEqual([42, 2, 3]);
      expect(folderActions(moved[1], moved).canDelete).toBe(false);
      expect(folderActions(moved[2], moved).canDelete).toBe(true);
    });

    it('shows a subfolder whose parent was not returned as a top folder instead of losing it', () => {
      const orphan = folder(20, 'Løs', 99);
      expect(buildFolderTree([folders[0], orphan]).map(n => n.folder.name)).toEqual(['Arkiv', 'Løs']);
    });
  });

  it('findFolder treats null and unknown ids as no folder', () => {
    expect(findFolder(folders, null)).toBeUndefined();
    expect(findFolder(folders, 404)).toBeUndefined();
    expect(findFolder(folders, 4)?.name).toBe('Fakturaer');
  });

  it('folderPath gives parent and folder', () => {
    expect(folderPath(folders, 4)).toEqual(['Drift', 'Fakturaer']);
    expect(folderPathLabel(folders, 4)).toBe('Drift › Fakturaer');
    expect(folderPath(folders, 3)).toEqual(['Drift']);
    expect(folderPath(folders, 404)).toEqual([]);
  });

  describe('effective access (spec section 3)', () => {
    it('a subfolder without own groups inherits its parent\'s', () => {
      expect(effectiveFolderGroups(folders, 7)).toEqual([ledelse]);
      expect(accessSourceFolder(folders, 7)?.id).toBe(6);
    });

    it('own groups replace the parent\'s and are never merged', () => {
      expect(effectiveFolderGroups(folders, 8)).toEqual([bogholderi]);
      expect(accessSourceFolder(folders, 8)?.id).toBe(8);
    });

    it('the access dialog edits only the groups a folder sets itself', () => {
      expect(ownFolderGroups(folders[6])).toEqual([]); // Tilsyn inherits Ledelse
      expect(ownFolderGroups(folders[5])).toEqual([ledelse]);
    });

    it('an inheriting subfolder whose parent is not in the list has no known source', () => {
      const orphan = folder(21, 'Løs', 99, {readGroups: [ledelse], restricted: true});
      expect(accessSourceFolder([orphan], 21)).toBeNull();
      expect(effectiveFolderGroups([orphan], 21)).toEqual([ledelse]);
    });

    it('no groups up the chain = open', () => {
      expect(effectiveFolderGroups(folders, 4)).toEqual([]);
      expect(accessSourceFolder(folders, 4)).toBeNull();
    });

    it('a document with own access keeps its own groups, even none in a restricted folder', () => {
      expect(effectiveFileGroups({id: 1, folderId: 7, ownAccess: true, readGroups: []}, folders)).toEqual([]);
      expect(effectiveFileGroups({id: 1, folderId: 7, ownAccess: false, readGroups: []}, folders)).toEqual([ledelse]);
    });

    it('compares groups by id, ignoring order', () => {
      expect(sameGroups([ledelse, bogholderi], [bogholderi, ledelse])).toBe(true);
      expect(sameGroups([ledelse], [bogholderi])).toBe(false);
      expect(sameGroups([], [])).toBe(true);
      expect(groupNames([ledelse, bogholderi])).toBe('Ledelse, Bogholderi');
    });
  });

  describe('moveImpact (D11)', () => {
    const open = {id: 1, folderId: 1, ownAccess: false, readGroups: []};
    const inFakturaer = {id: 2, folderId: 4, ownAccess: false, readGroups: []};
    const own = {id: 3, folderId: 1, ownAccess: true, readGroups: [ledelse]};
    const alreadyThere = {id: 4, folderId: 7, ownAccess: false, readGroups: [ledelse]};

    it('counts only documents whose effective access changes', () => {
      expect(moveImpact([open, inFakturaer, own, alreadyThere], folders, 7)).toEqual({changed: 2, targetGroups: [ledelse]});
    });

    it('a move between folders with the same access changes nothing', () => {
      expect(moveImpact([open, inFakturaer], folders, 5)).toEqual({changed: 0, targetGroups: []});
    });

    it('reads the before-groups from the document row, so a folder missing from the list still counts', () => {
      const unlisted = {id: 6, folderId: 99, ownAccess: false, readGroups: [bogholderi]};
      const unlistedOpen = {id: 7, folderId: 99, ownAccess: false, readGroups: []};
      expect(moveImpact([unlisted, unlistedOpen], folders, 1)).toEqual({changed: 1, targetGroups: []});
      expect(moveImpact([unlisted, unlistedOpen], folders, 2)).toEqual({changed: 1, targetGroups: [bogholderi]});
    });

    it('moving from a restricted folder into an open one opens the document', () => {
      const restricted = {id: 5, folderId: 7, ownAccess: false, readGroups: [ledelse]};
      expect(moveImpact([restricted], folders, 1)).toEqual({changed: 1, targetGroups: []});
    });
  });

  describe('folderActions', () => {
    it('the default folder can be renamed and get subfolders, but never moved, restricted or deleted (D12)', () => {
      const a = folderActions(folders[0], folders);
      expect(a.canCreateSubfolder).toBe(true);
      expect([a.canMove, a.canRestrict, a.canDelete]).toEqual([false, false, false]);
      expect(a.moveReason).toBe('The default folder cannot be moved');
      expect(a.restrictReason).toBe('The default folder cannot have restricted read access');
      expect(a.deleteReason).toBe('The default folder cannot be deleted');
    });

    it('a subfolder cannot get subfolders (D2)', () => {
      const a = folderActions(folders[3], folders);
      expect(a.canCreateSubfolder).toBe(false);
      expect(a.createSubfolderReason).toBe('At most 2 levels');
      expect(a.canMove).toBe(true);
    });

    it('a top folder with subfolders cannot move under another folder', () => {
      const a = folderActions(folders[2], folders);
      expect(a.canMove).toBe(false);
      expect(a.moveReason).toBe('At most 2 levels');
    });

    it('Slet is disabled only on visible evidence; a count of 0 under a property filter leaves the refusal to the server', () => {
      expect(folderActions(folders[2], folders).canDelete).toBe(false);
      expect(folderActions(folders[2], folders).deleteReason).toBe('Only empty folders can be deleted ({{documents}} documents, {{subfolders}} subfolders)');
      expect(folderActions(folders[2], folders).deleteReasonParams).toEqual({documents: 46, subfolders: 2});
      expect(folderActions(folders[8], folders).deleteReasonParams).toBeNull();
      expect(folderActions(folders[7], folders).canDelete).toBe(true); // Kontrakter: count 0 in this view
      expect(folderActions(folders[8], folders).canDelete).toBe(true);
    });

    it('a subfolder is visible evidence even when no document is counted in this view', () => {
      const parent = folder(40, 'Forældre', null, {count: 0});
      const a = folderActions(parent, [parent, folder(41, 'Barn', 40)]);
      expect(a.canDelete).toBe(false);
      expect(a.deleteReasonParams).toEqual({documents: 0, subfolders: 1});
    });
  });

  describe('accessGroupItems', () => {
    it('keeps a current group without live members, marked, after the live ones', () => {
      expect(accessGroupItems([ledelse], [bogholderi, ledelse], 'ingen medlemmer')).toEqual([
        {id: 5, name: 'Ledelse'}, {id: 6, name: 'Bogholderi (ingen medlemmer)'},
      ]);
      expect(accessGroupItems([ledelse], [], 'x')).toEqual([{id: 5, name: 'Ledelse'}]);
    });
  });

  describe('folderOptions', () => {
    it('lists both levels in tree order with full paths', () => {
      expect(folderOptions(folders).map(o => o.label)).toEqual([
        'Arkiv', 'Drift', 'Drift › Fakturaer', 'Drift › Serviceaftaler', 'Miljø', 'Miljø › Kontrakter', 'Miljø › Tilsyn', 'Tom', 'Økonomi',
      ]);
      const tilsyn = folderOptions(folders).find(o => o.id === 7)!;
      expect(tilsyn).toEqual({id: 7, label: 'Miljø › Tilsyn', depth: 1, restricted: true, groupsLabel: 'Ledelse', isDefault: false});
    });

    it('tells two subfolders with the same name apart by their path', () => {
      const twins = [...folders, folder(30, 'Fakturaer', 2)];
      const labels = folderOptions(twins).filter(o => o.label.endsWith('Fakturaer')).map(o => o.label);
      expect(labels).toEqual(['Drift › Fakturaer', 'Økonomi › Fakturaer']);
    });
  });

  describe('folderMoveOptions', () => {
    it('a subfolder may go to the top level or under another top folder, not under its own parent', () => {
      expect(folderMoveOptions(folders[3], folders, 'Top level').map(o => [o.id, o.label])).toEqual([
        [ARCHIVE_TOP_LEVEL_ID, 'Top level'], [1, 'Arkiv'], [6, 'Miljø'], [9, 'Tom'], [2, 'Økonomi'],
      ]);
    });

    it('a top folder may go under any other top folder', () => {
      expect(folderMoveOptions(folders[8], folders, 'Top level').map(o => o.id)).toEqual([1, 3, 6, 2]);
    });
  });
});
