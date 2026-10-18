import {Component, EventEmitter, Input, OnChanges, Output, SimpleChanges} from '@angular/core';
import {ArchiveFolderModel} from '../../../../models';
import {
  ArchiveFolderActions,
  ArchiveFolderNode,
  buildFolderTree,
  findFolder,
  folderActions,
  readCollapsedFolderIds,
  writeCollapsedFolderIds,
} from '../../../../helpers';
import {NgIf, NgFor, NgTemplateOutlet} from '@angular/common';
import {TranslatePipe} from '@ngx-translate/core';
import {MatIcon} from '@angular/material/icon';
import {MatIconButton} from '@angular/material/button';
import {MatTooltip} from '@angular/material/tooltip';
import {MatMenu, MatMenuItem, MatMenuTrigger} from '@angular/material/menu';

/**
 * Archive folder tree (W1, left column). Admins get create and a per-folder menu; others only navigate.
 * Rows are drop targets while documents are dragged.
 */
@Component({
  selector: 'app-archive-folder-tree',
  imports: [NgIf, NgFor, NgTemplateOutlet, TranslatePipe, MatIcon, MatIconButton, MatTooltip, MatMenu, MatMenuItem, MatMenuTrigger],
  templateUrl: './archive-folder-tree.component.html',
})
export class ArchiveFolderTreeComponent implements OnChanges {
  @Input() folders: ArchiveFolderModel[] = [];
  @Input() selectedFolderId: number | null = null;
  @Input() isAdmin = false;
  /** True while table rows are being dragged; only then do rows accept a drop. */
  @Input() dragActive = false;
  @Output() folderSelected = new EventEmitter<number>();
  @Output() createTop = new EventEmitter<void>();
  @Output() createSubfolder = new EventEmitter<ArchiveFolderModel>();
  @Output() renameFolder = new EventEmitter<ArchiveFolderModel>();
  @Output() moveFolder = new EventEmitter<ArchiveFolderModel>();
  @Output() editAccess = new EventEmitter<ArchiveFolderModel>();
  @Output() deleteFolder = new EventEmitter<ArchiveFolderModel>();
  @Output() filesDropped = new EventEmitter<number>();

  tree: ArchiveFolderNode[] = [];
  dropTargetId: number | null = null;
  private collapsed = new Set<number>(readCollapsedFolderIds());
  private actionsById = new Map<number, ArchiveFolderActions>();

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['folders']) {
      this.tree = buildFolderTree(this.folders);
      this.actionsById = new Map(this.folders.map(f => [f.id, folderActions(f, this.folders)]));
      this.pruneCollapsed();
    }
    if (changes['dragActive'] && !this.dragActive) {
      // A drag that ended outside the tree (Esc, dropped elsewhere) leaves no highlighted row behind.
      this.dropTargetId = null;
    }
    this.expandSelectedParent();
  }

  isCollapsed(folderId: number): boolean {
    return this.collapsed.has(folderId);
  }

  toggle(folderId: number): void {
    if (!this.collapsed.delete(folderId)) {
      this.collapsed.add(folderId);
    }
    writeCollapsedFolderIds([...this.collapsed]);
  }

  actions(folder: ArchiveFolderModel): ArchiveFolderActions {
    return this.actionsById.get(folder.id) ?? folderActions(folder, this.folders);
  }

  trackByNode(_: number, node: ArchiveFolderNode): number {
    return node.folder.id;
  }

  trackByFolder(_: number, folder: ArchiveFolderModel): number {
    return folder.id;
  }

  onDragOver(event: DragEvent, folder: ArchiveFolderModel): void {
    if (!this.dragActive) {
      return;
    }
    event.preventDefault();
    this.dropTargetId = folder.id;
  }

  onDragLeave(folder: ArchiveFolderModel): void {
    if (this.dropTargetId === folder.id) {
      this.dropTargetId = null;
    }
  }

  onDrop(event: DragEvent, folder: ArchiveFolderModel): void {
    if (!this.dragActive) {
      return;
    }
    event.preventDefault();
    this.dropTargetId = null;
    this.filesDropped.emit(folder.id);
  }

  /** Forgets folded ids of folders that no longer exist. An empty list (not loaded yet) prunes nothing. */
  private pruneCollapsed(): void {
    if (!this.folders.length) {
      return;
    }
    const ids = new Set(this.folders.map(f => f.id));
    const kept = [...this.collapsed].filter(id => ids.has(id));
    if (kept.length !== this.collapsed.size) {
      this.collapsed = new Set(kept);
      writeCollapsedFolderIds(kept);
    }
  }

  /** Unfolds the parent of a selected subfolder, so the selection is never hidden. */
  private expandSelectedParent(): void {
    const parentId = findFolder(this.folders, this.selectedFolderId)?.parentId ?? null;
    if (parentId !== null && this.collapsed.delete(parentId)) {
      writeCollapsedFolderIds([...this.collapsed]);
    }
  }
}
