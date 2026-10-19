import {describe, it, expect, afterEach, vi} from 'vitest';
import {ArchiveFolderTreeComponent} from './archive-folder-tree.component';
import {ArchiveFolderModel} from '../../../../models';
import {readCollapsedFolderIds, writeCollapsedFolderIds} from '../../../../helpers';

const folders: ArchiveFolderModel[] = [
  {id: 1, name: 'Arkiv', parentId: null, isDefault: true, count: 3, readGroups: [], ownGroups: false, restricted: false},
  {id: 2, name: 'Økonomi', parentId: null, isDefault: false, count: 0, readGroups: [{id: 6, name: 'Bogholderi'}], ownGroups: true, restricted: true},
  {id: 3, name: 'Drift', parentId: null, isDefault: false, count: 2, readGroups: [], ownGroups: false, restricted: false},
  {id: 4, name: 'Fakturaer', parentId: 3, isDefault: false, count: 2, readGroups: [], ownGroups: false, restricted: false},
];

function create(selectedFolderId: number | null = 1): ArchiveFolderTreeComponent {
  const tree = new ArchiveFolderTreeComponent();
  tree.folders = folders;
  tree.selectedFolderId = selectedFolderId;
  tree.isAdmin = true;
  tree.ngOnChanges({folders: {} as any, selectedFolderId: {} as any});
  return tree;
}

describe('ArchiveFolderTreeComponent', () => {
  afterEach(() => window.localStorage.clear());

  it('lists the default folder first, Danish order after it, subfolders nested', () => {
    const tree = create();
    expect(tree.tree.map(n => n.folder.name)).toEqual(['Arkiv', 'Drift', 'Økonomi']);
    expect(tree.tree[1].children.map(c => c.name)).toEqual(['Fakturaer']);
  });

  it('remembers folded folders across page loads', () => {
    const tree = create();
    tree.toggle(3);
    expect(tree.isCollapsed(3)).toBe(true);
    expect(readCollapsedFolderIds()).toEqual([3]);
    const reloaded = create();
    expect(reloaded.isCollapsed(3)).toBe(true);
    reloaded.toggle(3);
    expect(create().isCollapsed(3)).toBe(false);
  });

  it('unfolds the parent of a selected subfolder so the selection is never hidden', () => {
    writeCollapsedFolderIds([3]);
    expect(create(4).isCollapsed(3)).toBe(false);
    expect(readCollapsedFolderIds()).toEqual([]);
  });

  it('forgets folded folders that no longer exist, but not before the folders have loaded', () => {
    writeCollapsedFolderIds([3, 99]);
    const tree = new ArchiveFolderTreeComponent();
    tree.ngOnChanges({folders: {} as any});
    expect(readCollapsedFolderIds()).toEqual([3, 99]);

    create();
    expect(readCollapsedFolderIds()).toEqual([3]);
  });

  it('disables delete on the default folder and subfolders on a subfolder', () => {
    const tree = create();
    expect(tree.actions(folders[0]).canDelete).toBe(false);
    expect(tree.actions(folders[3]).canCreateSubfolder).toBe(false);
  });

  it('accepts a drop only while documents are dragged', () => {
    const tree = create();
    const dropped = vi.fn();
    tree.filesDropped.subscribe(dropped);
    const event = {preventDefault: vi.fn()} as unknown as DragEvent;

    tree.onDragOver(event, folders[2]);
    expect(event.preventDefault).not.toHaveBeenCalled();
    expect(tree.dropTargetId).toBeNull();

    tree.dragActive = true;
    tree.onDragOver(event, folders[2]);
    expect(event.preventDefault).toHaveBeenCalled();
    expect(tree.dropTargetId).toBe(3);

    tree.onDrop(event, folders[2]);
    expect(dropped).toHaveBeenCalledWith(3);
    expect(tree.dropTargetId).toBeNull();
  });

  it('clears the drop highlight when the drag ends outside the tree', () => {
    const tree = create();
    tree.dropTargetId = 3;
    tree.dragActive = false;
    tree.ngOnChanges({dragActive: {} as any});
    expect(tree.dropTargetId).toBeNull();
  });
});
