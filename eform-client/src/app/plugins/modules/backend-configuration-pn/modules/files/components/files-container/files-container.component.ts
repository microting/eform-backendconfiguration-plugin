import {Component, OnDestroy, OnInit, ViewChild, inject} from '@angular/core';
import {BreakpointObserver} from '@angular/cdk/layout';
import {Overlay} from '@angular/cdk/overlay';
import {MatDialog} from '@angular/material/dialog';
import {Router, RouterLink} from '@angular/router';
import {Store} from '@ngrx/store';
import {TranslateService, TranslatePipe} from '@ngx-translate/core';
import {ToastrService} from 'ngx-toastr';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {saveAs} from 'file-saver';
import {Observable, Subscription, concat, filter, toArray, zip} from 'rxjs';
import {CommonDictionaryModel, DeleteModalSettingModel, Paged, SharedTagModel} from 'src/app/common/models';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {DeleteModalComponent} from 'src/app/common/modules/eform-shared/components';
import {AppMenuStateService} from 'src/app/common/store';
import {selectCurrentUserIsAdmin} from 'src/app/state';
import {ArchiveFolderModel, ArchiveNamedItem, FilesModel} from '../../../../models';
import {
  BackendConfigurationPnArchiveFoldersService,
  BackendConfigurationPnFilesService,
  BackendConfigurationPnFileTagsService,
  BackendConfigurationPnPropertiesService,
  BackendConfigurationPnWorkerTagsService,
} from '../../../../services';
import {
  ARCHIVE_TOP_LEVEL_ID,
  ArchiveFolderOption,
  accessSourceFolder,
  effectiveFolderGroups,
  findFolder,
  folderMoveOptions,
  folderOptions,
  folderPath,
  whoCanRead,
  ownFolderGroups,
} from '../../../../helpers';
import {
  FilesFiltrationModel,
  filesSelectFolder,
  filesToggleDateCheck,
  selectFilesFilters,
  selectFilesPagination,
} from '../../../../state';
import {FilesStateService} from '../../store';
import {ArchiveMoveFlowService} from '../../services/archive-move-flow.service';
import {archivePresentation} from '../../archive-file-kind';
import {DownloadFilesNameArchiveComponent} from '../files-actions/download-files-name-archive/download-files-name-archive.component';
import {FileTagsComponent} from '../file-tags/file-tags.component';
import {ArchiveAccessDialogComponent, ArchiveAccessDialogData, ArchiveAccessDialogResult} from '../archive-access-dialog/archive-access-dialog.component';
import {ArchiveFolderChooseDialogComponent, ArchiveFolderChooseDialogData} from '../archive-folder-choose-dialog/archive-folder-choose-dialog.component';
import {ArchiveFolderNameDialogComponent, ArchiveFolderNameDialogData} from '../archive-folder-name-dialog/archive-folder-name-dialog.component';
import {ArchivePropertiesDialogComponent, ArchivePropertiesDialogData} from '../archive-properties-dialog/archive-properties-dialog.component';
import {ArchivePanelSaved, ArchiveDocumentPanelComponent} from '../archive-document-panel/archive-document-panel.component';
import {NgIf, NgFor, NgSwitch, NgSwitchCase, NgSwitchDefault} from '@angular/common';
import {MatIcon} from '@angular/material/icon';
import {MatIconButton} from '@angular/material/button';
import {MatCard} from '@angular/material/card';
import {MatTooltip} from '@angular/material/tooltip';
import {ArchiveSectionNavComponent} from '../archive-section-nav/archive-section-nav.component';
import {ArchiveFolderTreeComponent} from '../archive-folder-tree/archive-folder-tree.component';
import {FilesTableComponent} from '../files-table/files-table.component';
import {FilesFiltersComponent} from '../files-filters/files-filters.component';

type ArchiveListMode = 'folder' | 'search' | 'date';

/**
 * The archive page: folder tree | documents | document panel (a drawer below 1100 px).
 * A search or the date check lists all folders; otherwise one folder is listed.
 */
@AutoUnsubscribe()
@Component({
  selector: 'app-files-container',
  imports: [NgIf, NgFor, NgSwitch, NgSwitchCase, NgSwitchDefault, TranslatePipe, RouterLink, MatIcon, MatIconButton, MatCard, MatTooltip, ArchiveSectionNavComponent, ArchiveFolderTreeComponent, FileTagsComponent, ArchiveDocumentPanelComponent, FilesTableComponent, FilesFiltersComponent],
  templateUrl: './files-container.component.html',
})
export class FilesContainerComponent implements OnInit, OnDestroy {
  /** Below this width the panel is a drawer. */
  static readonly DRAWER_QUERY = '(max-width: 1099.98px)';

  private store = inject(Store);
  public dialog = inject(MatDialog);
  private overlay = inject(Overlay);
  public filesStateService = inject(FilesStateService);
  private tagsService = inject(BackendConfigurationPnFileTagsService);
  private translateService = inject(TranslateService);
  private toastrService = inject(ToastrService);
  private filesService = inject(BackendConfigurationPnFilesService);
  private foldersService = inject(BackendConfigurationPnArchiveFoldersService);
  private propertiesService = inject(BackendConfigurationPnPropertiesService);
  private workerTagsService = inject(BackendConfigurationPnWorkerTagsService);
  private moveFlow = inject(ArchiveMoveFlowService);
  private breakpointObserver = inject(BreakpointObserver);
  private router = inject(Router);
  private appMenuStateService = inject(AppMenuStateService);

  readonly selectedKey = '{{count}} selected';

  @ViewChild('tagsModal') tagsModal: FileTagsComponent;
  // Resolved once menus hydrate — calling getTitleByUrl from the template leaks a subscription per tick.
  pageTitle = '';
  isAdmin = false;
  /** Below 1100 px: the panel is a drawer over the page, not a third grid column. */
  narrow = false;
  availableTags: SharedTagModel[] = [];
  properties: CommonDictionaryModel[] = [];
  workerTags: CommonDictionaryModel[] = [];
  folders: ArchiveFolderModel[] = [];
  options: ArchiveFolderOption[] = [];
  files: Paged<FilesModel> = new Paged<FilesModel>();
  filesLoaded = false;
  filters: FilesFiltrationModel;
  sortField = 'Id';
  sortDesc = false;
  selectedFileIds: number[] = [];
  currentFileId: number | null = null;
  highlightedFileId: number | null = null;
  dateCheckCount = 0;
  dragActive = false;
  private draggingFileIds: number[] = [];
  /** Set by a panel save: the document to show once the list has reloaded (undefined = keep as is). */
  private pendingCurrentId: number | null | undefined = undefined;
  private highlightTimer: ReturnType<typeof setTimeout> | undefined;

  titleSub$: Subscription;
  filtersSub$: Subscription;
  paginationSub$: Subscription;
  narrowSub$: Subscription;
  isAdminSub$: Subscription;
  translatesSub$: Subscription;
  filesDeletedSub$: Subscription;
  clickDownloadFilesSub$: Subscription;
  downloadFilesSub$: Subscription;

  ngOnInit(): void {
    this.titleSub$ = this.appMenuStateService.leftAppMenus$.subscribe(() => {
      this.pageTitle = this.appMenuStateService.getTitleByUrl(this.router.url);
    });
    this.filtersSub$ = this.store.select(selectFilesFilters).subscribe(filters => (this.filters = filters));
    this.paginationSub$ = this.store.select(selectFilesPagination).subscribe(p => {
      this.sortField = p.sort;
      this.sortDesc = p.isSortDsc;
    });
    this.narrowSub$ = this.breakpointObserver.observe(FilesContainerComponent.DRAWER_QUERY).subscribe(state => (this.narrow = state.matches));
    this.isAdminSub$ = this.store.select(selectCurrentUserIsAdmin).subscribe(isAdmin => {
      this.isAdmin = isAdmin;
      if (isAdmin && !this.workerTags.length) {
        this.getWorkerTags();
      }
    });
    this.getTags();
    this.getProperties();
    this.refresh();
  }

  // Also required by @AutoUnsubscribe: Angular only calls a destroy hook the class declares.
  ngOnDestroy(): void {
    clearTimeout(this.highlightTimer);
  }

  // ---- view state ---------------------------------------------------------------
  get listMode(): ArchiveListMode {
    if (this.filters?.needsDateCheck) {
      return 'date';
    }
    return this.filters?.nameFilter ? 'search' : 'folder';
  }

  get selectedFolderId(): number | null {
    return this.filters?.folderId ?? null;
  }

  get selectedPath(): string[] {
    return this.selectedFolderId === null ? [] : folderPath(this.folders, this.selectedFolderId);
  }

  get subfolders(): ArchiveFolderModel[] {
    const folder = findFolder(this.folders, this.selectedFolderId);
    return folder && folder.parentId === null ? this.folders.filter(f => f.parentId === folder.id) : [];
  }

  /** Who reads the selected folder, and which folder it inherits from (not named when that parent is not visible). */
  get crumbAccessLabel(): string {
    const id = this.selectedFolderId;
    if (id === null) {
      return '';
    }
    const groups = effectiveFolderGroups(this.folders, id);
    const who = whoCanRead(groups, key => this.translateService.instant(key));
    const source = accessSourceFolder(this.folders, id);
    return source && source.id !== id
      ? this.translateService.instant('Read access: {{who}} (inherits from {{folder}})', {who, folder: source.name})
      : this.translateService.instant('Read access: {{who}}', {who});
  }

  get hasNext(): boolean {
    return this.currentFileId !== null && this.nextFileId(this.currentFileId) !== null;
  }

  get positionLabel(): string | null {
    if (this.currentFileId === null || this.selectedFileIds.length < 2) {
      return null;
    }
    const index = this.selectedFileIds.indexOf(this.currentFileId);
    return index < 0
      ? null
      : this.translateService.instant('{{index}} of {{count}} selected', {index: index + 1, count: this.selectedFileIds.length});
  }

  private get selectedRows(): FilesModel[] {
    return this.files.entities.filter(f => this.selectedFileIds.includes(f.id));
  }

  // ---- loading --------------------------------------------------------------------
  /**
   * Tree first (its counts follow the property filter), then the documents and the date-check count.
   * The store starts with no folder (which would list every folder), so the first load selects the
   * default folder — found by `isDefault`, never by id. The same happens when the selected folder is gone.
   */
  refresh(): void {
    this.foldersService.getFolders(this.filters.propertyIds).subscribe(res => {
      if (!res?.success || !res.model) {
        return;
      }
      this.folders = res.model;
      this.options = folderOptions(res.model);
      if (!findFolder(this.folders, this.selectedFolderId)) {
        const fallback = this.folders.find(f => f.isDefault) ?? this.folders[0];
        if (fallback) {
          this.store.dispatch(filesSelectFolder(fallback.id));
        }
      }
      this.getFiles();
      this.getDateCheckCount();
    });
  }

  getFiles(): void {
    this.filesStateService.getFiles().subscribe(res => {
      if (!res?.success || !res.model) {
        return;
      }
      this.files = res.model;
      this.filesLoaded = true;
      const ids = new Set(res.model.entities.map(f => f.id));
      this.selectedFileIds = this.selectedFileIds.filter(id => ids.has(id));
      if (this.pendingCurrentId !== undefined) {
        this.currentFileId = this.pendingCurrentId !== null && ids.has(this.pendingCurrentId) ? this.pendingCurrentId : null;
        this.pendingCurrentId = undefined;
      } else if (this.currentFileId !== null && !ids.has(this.currentFileId)) {
        this.currentFileId = null;
      }
    });
  }

  getDateCheckCount(): void {
    this.filesStateService.getDateCheckCount().subscribe(count => (this.dateCheckCount = count));
  }

  getTags(): void {
    this.tagsService.getTags().subscribe(res => {
      if (res?.success) {
        this.availableTags = res.model;
      }
    });
  }

  getProperties(): void {
    this.propertiesService.getAllPropertiesDictionary().subscribe(res => {
      if (res?.success && res.model) {
        this.properties = res.model;
      }
    });
  }

  getWorkerTags(): void {
    this.workerTagsService.getWorkerTags().subscribe(res => {
      if (res?.success && res.model) {
        this.workerTags = res.model;
      }
    });
  }

  // ---- navigation -----------------------------------------------------------------
  selectFolder(folderId: number): void {
    this.store.dispatch(filesSelectFolder(folderId));
    this.resetSelectionAndReload();
  }

  toggleDateCheck(): void {
    this.store.dispatch(filesToggleDateCheck(!this.filters.needsDateCheck));
    this.resetSelectionAndReload();
  }

  private resetSelectionAndReload(): void {
    this.currentFileId = null;
    this.selectedFileIds = [];
    this.getFiles();
  }

  onSort(field: string): void {
    this.filesStateService.onSortTable(field);
    this.getFiles();
  }

  /** The user's choice wins over a save that is still reloading the list. */
  openFile(row: FilesModel): void {
    this.pendingCurrentId = undefined;
    this.currentFileId = row.id;
  }

  closePanel(): void {
    this.currentFileId = null;
  }

  /**
   * A save for a document that is no longer the one shown (the user opened another row while it ran) only
   * reloads the list: it neither highlights nor moves the panel. "Gem og næste" advances only on `next`.
   */
  onPanelSaved(event: ArchivePanelSaved): void {
    if (event.fileId === this.currentFileId) {
      this.pendingCurrentId = event.next ? this.nextFileId(event.fileId) : event.fileId;
      this.highlightedFileId = event.fileId;
      clearTimeout(this.highlightTimer);
      this.highlightTimer = setTimeout(() => (this.highlightedFileId = null), 3000);
    }
    this.refresh();
  }

  /** The next selected document when several are selected, else the next row. */
  private nextFileId(fileId: number): number | null {
    const order = this.selectedFileIds.length > 1 && this.selectedFileIds.includes(fileId)
      ? this.selectedFileIds
      : this.files.entities.map(f => f.id);
    const index = order.indexOf(fileId);
    return index >= 0 && index + 1 < order.length ? order[index + 1] : null;
  }

  // ---- moving documents -------------------------------------------------------------
  moveSelected(): void {
    const rows = this.selectedRows;
    this.chooseFolder('Move to folder', this.options, this.selectedFolderId).subscribe(target => this.moveRows(rows, target));
  }

  onRowDragStart(row: FilesModel): void {
    this.draggingFileIds = this.selectedFileIds.includes(row.id) ? this.selectedFileIds : [row.id];
    this.dragActive = true;
  }

  onRowDragEnd(): void {
    this.dragActive = false;
  }

  onFolderDrop(folderId: number): void {
    const rows = this.files.entities.filter(f => this.draggingFileIds.includes(f.id));
    this.dragActive = false;
    this.draggingFileIds = [];
    this.moveRows(rows, folderId);
  }

  /** ArchiveMoveFlowService.move emits exactly once; only a real move reloads the page. */
  private moveRows(rows: FilesModel[], targetFolderId: number): void {
    this.moveFlow.move(rows, this.folders, targetFolderId, this.isAdmin).subscribe(moved => {
      if (moved) {
        this.selectedFileIds = [];
        this.refresh();
      }
    });
  }

  // ---- bulk actions -----------------------------------------------------------------
  /** One PUT per document, in turn. A step the host completes empty (404) does not stop the others. */
  setSelectedProperties(): void {
    const ids = this.selectedFileIds;
    const data: ArchivePropertiesDialogData = {properties: this.properties, count: ids.length};
    this.dialog.open(ArchivePropertiesDialogComponent, {...dialogConfigHelper(this.overlay, data), minWidth: 420})
      .afterClosed()
      .pipe(filter((value): value is number[] => Array.isArray(value) && value.length > 0))
      .subscribe(propertyIds => concat(...ids.map(id => this.filesService.updateFileProperties({id, properties: propertyIds})))
        .pipe(toArray())
        .subscribe(() => this.refresh()));
  }

  /** C5: opens on "use the folder's access", so one Save click never changes access by itself. */
  setSelectedAccess(): void {
    const ids = this.selectedFileIds;
    this.openAccessDialog({
      mode: 'files', title: 'Read access for {{count}} documents', titleParams: {count: ids.length},
      workerTags: this.workerTags, currentGroups: [], ownAccess: false,
    }).subscribe(result => this.filesService
      .setFilesAccess({fileIds: ids, ownAccess: result.ownAccess, tagIds: result.tagIds})
      .subscribe(res => {
        if (res?.success) {
          if (result.ownAccess) { // inheriting documents read per their own folder, so there is no single "who" to name
            this.toastAccessChanged(res.model?.accessChanged ?? 0, this.workerTags.filter(t => result.tagIds.includes(t.id)));
          }
          this.refresh();
        }
      }));
  }

  downloadSelectedFiles(): void {
    const ids = this.selectedFileIds;
    const modal = this.dialog.open(DownloadFilesNameArchiveComponent, {...dialogConfigHelper(this.overlay)});
    this.clickDownloadFilesSub$ = modal.componentInstance.clickDownloadFiles.subscribe(zipName => {
      this.downloadFilesSub$ = this.filesService.downloadFiles({fileIds: ids, archiveName: zipName})
        .subscribe(blob => saveAs(blob, `${zipName}.zip`));
    });
  }

  showDeleteModal(file: FilesModel): void {
    this.openDeleteModal({
      model: file, headerKey: archivePresentation(file.fileExtension).deleteKey, fieldKey: 'File name', field: 'fileName',
      cancelButtonId: 'cancelDeleteFileBtn', deleteButtonId: 'deleteFileBtn',
      onDelete: close => this.filesService.deleteFile(file.id).subscribe(operation => {
        if (operation?.success) {
          close();
          this.refresh();
        }
      }),
    });
  }

  openTagsModal(): void {
    this.tagsModal.show();
  }

  // ---- folder administration (admin) -------------------------------------------------
  createTopFolder(): void {
    this.askFolderName({title: 'New folder'}).subscribe(name =>
      this.foldersService.createFolder({name, parentId: null}).subscribe(res => this.afterFolderChange(res?.success, res?.model?.id)));
  }

  createSubfolder(parent: ArchiveFolderModel): void {
    this.askFolderName({title: 'New subfolder in {{folder}}', titleParams: {folder: parent.name}}).subscribe(name =>
      this.foldersService.createFolder({name, parentId: parent.id}).subscribe(res => this.afterFolderChange(res?.success, res?.model?.id)));
  }

  renameFolder(folder: ArchiveFolderModel): void {
    this.askFolderName({title: 'Rename folder', name: folder.name}).subscribe(name =>
      this.foldersService.updateFolder(folder.id, {name}).subscribe(res => this.afterFolderChange(res?.success)));
  }

  moveFolder(folder: ArchiveFolderModel): void {
    const options = folderMoveOptions(folder, this.folders, this.translateService.instant('Top level'));
    this.chooseFolder('Move folder {{folder}}', options, null, {folder: folder.name}).subscribe(target => {
      const toTop = target === ARCHIVE_TOP_LEVEL_ID;
      const groups = this.readGroupsAfterMove(folder, target);
      this.foldersService.updateFolder(folder.id, toTop ? {moveToTop: true} : {parentId: target}).subscribe(res => {
        if (res?.success) {
          this.toastAccessChanged(res.model?.accessChanged ?? 0, groups);
        }
        this.afterFolderChange(res?.success);
      });
    });
  }

  /** Who reads the moved folder's documents afterwards: its own groups, else the new parent's (none at the top level). */
  private readGroupsAfterMove(folder: ArchiveFolderModel, target: number): ArchiveNamedItem[] {
    if (folder.ownGroups) {
      return folder.readGroups;
    }
    if (target === ARCHIVE_TOP_LEVEL_ID) {
      return [];
    }
    return findFolder(this.folders, target)?.readGroups ?? [];
  }

  editFolderAccess(folder: ArchiveFolderModel): void {
    // A subfolder without own groups inherits its parent's: the dialog says so instead of "everyone".
    const parent = findFolder(this.folders, folder.parentId);
    const parentGroups = parent ? effectiveFolderGroups(this.folders, parent.id) : [];
    this.openAccessDialog({
      mode: 'folder', title: 'Read access for {{folder}}', titleParams: {folder: folder.name},
      workerTags: this.workerTags, currentGroups: ownFolderGroups(folder), ownAccess: true,
      inheritedFrom: parent && parentGroups.length ? {folder: parent.name, groups: parentGroups} : undefined,
    }).subscribe(result => this.foldersService.setFolderAccess(folder.id, {tagIds: result.tagIds}).subscribe(res => {
      if (res?.success) {
        const chosen: ArchiveNamedItem[] = result.tagIds.map(id => ({
          id, name: this.workerTags.find(t => t.id === id)?.name ?? folder.readGroups.find(g => g.id === id)?.name ?? '',
        }));
        this.toastAccessChanged(res.model?.accessChanged ?? 0, chosen.length ? chosen : parentGroups);
      }
      this.afterFolderChange(res?.success);
    }));
  }

  deleteFolder(folder: ArchiveFolderModel): void {
    this.openDeleteModal({
      model: folder, headerKey: 'Delete folder', fieldKey: 'Name', field: 'name',
      cancelButtonId: 'archiveFolderDeleteCancelBtn', deleteButtonId: 'archiveFolderDeleteConfirmBtn',
      // A refusal (folder not empty) is toasted by ApiBaseService.
      onDelete: close => this.foldersService.deleteFolder(folder.id).subscribe(res => {
        if (res?.success) {
          close();
          this.afterFolderChange(true, folder.id === this.selectedFolderId ? this.folders.find(f => f.isDefault)?.id : undefined);
        }
      }),
    });
  }

  /** The shared delete confirmation; `onDelete` gets a function that closes it. */
  private openDeleteModal(options: {
    model: FilesModel | ArchiveFolderModel; headerKey: string; fieldKey: string; field: string;
    cancelButtonId: string; deleteButtonId: string; onDelete: (close: () => void) => void;
  }): void {
    this.translatesSub$ = zip(
      this.translateService.stream(options.headerKey),
      this.translateService.stream(options.fieldKey),
    ).subscribe(([headerText, fieldHeader]) => {
      const settings: DeleteModalSettingModel = {
        model: options.model,
        settings: {
          headerText: `${headerText}?`,
          fields: [{header: fieldHeader, field: options.field}],
          cancelButtonId: options.cancelButtonId,
          deleteButtonId: options.deleteButtonId,
        },
      };
      const modal = this.dialog.open(DeleteModalComponent, {...dialogConfigHelper(this.overlay, settings)});
      this.filesDeletedSub$ = modal.componentInstance.delete.subscribe(() => options.onDelete(() => modal.close()));
    });
  }

  private afterFolderChange(success: boolean | undefined, selectFolderId?: number): void {
    if (!success) {
      return;
    }
    if (selectFolderId) {
      this.store.dispatch(filesSelectFolder(selectFolderId));
    }
    this.refresh();
  }

  private toastAccessChanged(count: number, groups: ArchiveNamedItem[]): void {
    if (count > 0) {
      this.toastrService.info(this.moveFlow.accessChangedText(count, groups));
    }
  }

  private askFolderName(data: ArchiveFolderNameDialogData): Observable<string> {
    return this.dialog.open(ArchiveFolderNameDialogComponent, {...dialogConfigHelper(this.overlay, data), minWidth: 400})
      .afterClosed()
      .pipe(filter((name): name is string => typeof name === 'string' && name.length > 0));
  }

  private chooseFolder(title: string, options: ArchiveFolderOption[], selectedId: number | null,
                       titleParams?: Record<string, string>): Observable<number> {
    const data: ArchiveFolderChooseDialogData = {title, titleParams, options, selectedId};
    return this.dialog.open(ArchiveFolderChooseDialogComponent, {...dialogConfigHelper(this.overlay, data), minWidth: 420})
      .afterClosed()
      .pipe(filter((id): id is number => typeof id === 'number'));
  }

  private openAccessDialog(data: ArchiveAccessDialogData): Observable<ArchiveAccessDialogResult> {
    return this.dialog.open(ArchiveAccessDialogComponent, {...dialogConfigHelper(this.overlay, data), minWidth: 420})
      .afterClosed()
      .pipe(filter((result): result is ArchiveAccessDialogResult => !!result));
  }
}
