import {
  AfterViewInit, Component, ElementRef, EventEmitter, HostListener, Input, OnChanges, OnDestroy, Output, SimpleChanges, ViewChild,
} from '@angular/core';
import {MatDialog} from '@angular/material/dialog';
import {TranslateService, TranslatePipe} from '@ngx-translate/core';
import {Observable, Subject, concat, defaultIfEmpty, defer, map, takeUntil, takeWhile, toArray} from 'rxjs';
import {CommonDictionaryModel, OperationResult, SharedTagModel} from 'src/app/common/models';
import {ArchiveFolderModel, ArchiveNamedItem, DocumentDateState, FileModel} from '../../../../models';
import {BackendConfigurationPnFilesService} from '../../../../services';
import {
  ARCHIVE_PATH_SEPARATOR,
  ArchiveFolderOption,
  ArchivePanelValue,
  accessGroupItems,
  accessSourceFolder,
  effectiveFolderGroups,
  findFolder,
  whoCanRead,
  hasPanelChanges,
  isPanelValid,
  panelChanges,
  panelValueOf,
} from '../../../../helpers';
import {ArchiveMoveFlowService} from '../../services/archive-move-flow.service';
import {NgIf, DatePipe} from '@angular/common';
import {FormsModule} from '@angular/forms';
import {MatIcon} from '@angular/material/icon';
import {MatIconButton} from '@angular/material/button';
import {MatTooltip} from '@angular/material/tooltip';
import {MatFormField, MatLabel} from '@angular/material/form-field';
import {MatInput} from '@angular/material/input';
import {MtxSelect} from '@ng-matero/extensions/select';
import {ArchiveFolderPickerComponent} from '../archive-folder-picker/archive-folder-picker.component';
import {PdfViewerModule} from 'ng2-pdf-viewer';
import {saveAs} from 'file-saver';
import {ArchiveFileKind, archiveFileKind, revokeObjectUrl} from '../../archive-file-kind';

export interface ArchivePanelSaved {
  fileId: number;
  /** True for "Gem og næste": the page opens the next document. */
  next: boolean;
}

/**
 * The opened document as a large view over the archive area (folder tree and table are hidden, not removed):
 * a bar with back, title, position, previous/next and download; all pages of the preview beside the fields.
 * Edits are kept locally and saved together: name + properties, etiketter, document date, read access, and the
 * folder last, because a folder move may need the D11 confirmation and can be cancelled.
 */
@Component({
  selector: 'app-archive-document-panel',
  imports: [NgIf, DatePipe, FormsModule, TranslatePipe, MatIcon, MatIconButton, MatTooltip, MatFormField, MatLabel, MatInput, MtxSelect, ArchiveFolderPickerComponent, PdfViewerModule],
  templateUrl: './archive-document-panel.component.html',
})
export class ArchiveDocumentPanelComponent implements OnChanges, AfterViewInit, OnDestroy {
  @Input() fileId!: number;
  @Input() folders: ArchiveFolderModel[] = [];
  @Input() folderOptions: ArchiveFolderOption[] = [];
  @Input() properties: CommonDictionaryModel[] = [];
  @Input() availableTags: SharedTagModel[] = [];
  /** Live medarbejdergrupper; only loaded for admins and Leders. */
  @Input() workerTags: CommonDictionaryModel[] = [];
  /** Admin or Leder: edit Læseadgang and move across access. */
  @Input() canManage = false;
  /** "2 af 4" in the list, "1 af 2 valgte" while several documents are selected; null hides it. */
  @Input() positionLabel: string | null = null;
  @Input() hasPrevious = false;
  @Input() hasNext = false;
  @Output() saved = new EventEmitter<ArchivePanelSaved>();
  @Output() closed = new EventEmitter<void>();
  /** The page opens the previous / next document, like a click on its row. */
  @Output() previousFile = new EventEmitter<void>();
  @Output() nextFile = new EventEmitter<void>();
  @ViewChild('closeButton', {read: ElementRef}) closeButton?: ElementRef<HTMLButtonElement>;

  readonly DateState = DocumentDateState;
  file: FileModel | null = null;
  original: ArchivePanelValue | null = null;
  /** Dates are `yyyy-MM-dd` strings here (panelValueOf reads the server Date via serverDay). */
  edited: ArchivePanelValue | null = null;
  accessItems: ArchiveNamedItem[] = [];
  editingDate = false;
  pdfBytes: Uint8Array | null = null;
  /** Object URL of an image document's blob; revoked when another document opens and on destroy. */
  imageUrl: string | null = null;
  /** The one fetched blob: read as PDF bytes or shown as an image, once the document's extension is known. */
  private previewBlob: Blob | null = null;
  busy = false;
  /** What had focus when the view opened (the row); the fallback when the shown document's row is not found on close. */
  private returnFocusTo: HTMLElement | null = null;
  /** Cancels pending reads when the panel closes, so a late image reply cannot create an object URL after cleanup. */
  private destroy$ = new Subject<void>();

  constructor(
    private filesService: BackendConfigurationPnFilesService,
    private moveFlow: ArchiveMoveFlowService,
    private translateService: TranslateService,
    /** Optional so specs can construct the panel with `new`; Angular always injects it (providedIn root). */
    private dialog?: MatDialog,
  ) {}

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['fileId']) {
      this.load();
    } else if (changes['workerTags']) {
      this.refreshAccessItems(this.selectedGroups());
    }
  }

  ngAfterViewInit(): void {
    this.captureFocus();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
    this.imageUrl = revokeObjectUrl(this.imageUrl);
    this.returnFocus();
  }

  /**
   * After previous/next the shown document is no longer the opener's: focus goes to its own row, else the opener.
   * The tree and the table are visible again by now (the page drops --viewing before it removes the view).
   */
  private returnFocus(): void {
    const row = document.querySelector<HTMLElement>(`#archiveFilesTable tr.archive-file-row[data-file-id="${this.fileId}"]`);
    const target = row ?? (this.returnFocusTo?.isConnected ? this.returnFocusTo : null);
    target?.focus();
  }

  /**
   * Escape closes the view, wherever focus is: a button that disables itself (next at the last document, Save while
   * busy) drops focus to <body>. A control that handles Escape itself (an open select list) prevents the default,
   * so its Escape only closes that list; an open dialog (the D11 move confirmation) keeps its Escape too.
   */
  @HostListener('document:keydown.escape', ['$event'])
  onEscape(event: Event): void {
    if (!event.defaultPrevented && !this.dialog?.openDialogs.length) {
      event.preventDefault();
      this.closed.emit();
    }
  }

  /** Remembers the opener and moves focus to the back button, unless focus is already inside the view. */
  private captureFocus(): void {
    const active = document.activeElement;
    const host = this.closeButton?.nativeElement.closest('aside');
    if (active instanceof HTMLElement && host?.contains(active)) {
      return;
    }
    this.returnFocusTo = active instanceof HTMLElement && active !== document.body ? active : null;
    this.closeButton?.nativeElement.focus();
  }

  /** Opens a document: its data and the PDF preview, from a clean view. */
  load(): void {
    this.file = null;
    this.original = null;
    this.edited = null;
    this.editingDate = false;
    this.pdfBytes = null;
    this.previewBlob = null;
    this.imageUrl = revokeObjectUrl(this.imageUrl);
    this.loadData();
    this.loadPreview();
  }

  /** The document's whole folder path ("Økonomi › Fakturaer"), as saved. */
  get folderPathLabel(): string {
    return (this.file?.folderPath ?? []).join(ARCHIVE_PATH_SEPARATOR);
  }

  /** Saves the document's own file ("<name>.<extension>"), the bytes the preview shows. */
  download(): void {
    const file = this.file;
    if (!file) {
      return;
    }
    this.filesService.getPdfFile(file.id).pipe(takeUntil(this.destroy$)).subscribe({
      next: (blob: Blob | null) => {
        if (blob) {
          saveAs(blob, `${file.fileName}.${file.fileExtension}`);
        }
      },
      // The host's HttpErrorInterceptor has already shown the error toast; nothing is saved.
      error: () => undefined,
    });
  }

  /** What the document is previewed as; 'other' (and a document not read yet) has no preview. */
  get kind(): ArchiveFileKind {
    return archiveFileKind(this.file?.fileExtension);
  }

  /**
   * (Re)reads the document's data; the current view stays until the reply arrives. getFile completes empty on a 404
   * (the document is gone or no longer readable): then the panel closes instead of standing blank.
   */
  private loadData(): void {
    const id = this.fileId;
    this.filesService.getFile(id).pipe(defaultIfEmpty(null), takeUntil(this.destroy$)).subscribe(res => {
      if (id !== this.fileId) {
        return;
      }
      if (res === null) {
        this.closed.emit();
        return;
      }
      if (res.success && res.model) {
        this.file = res.model;
        this.original = panelValueOf(res.model);
        this.edited = panelValueOf(res.model);
        this.editingDate = false;
        this.refreshAccessItems();
        this.applyPreview();
      }
    });
  }

  /** One fetch per open (getPdfFile returns the file's bytes whatever its type). */
  private loadPreview(): void {
    const id = this.fileId;
    this.filesService.getPdfFile(id).pipe(takeUntil(this.destroy$)).subscribe({
      next: (blob: Blob | null) => {
        if (blob && id === this.fileId) {
          this.previewBlob = blob;
          this.applyPreview();
        }
      },
      error: () => (this.pdfBytes = null),
    });
  }

  /** Runs when both the blob and the document's extension have arrived (in either order); consumes the blob. */
  private applyPreview(): void {
    const blob = this.previewBlob;
    if (!blob || !this.file) {
      return;
    }
    this.previewBlob = null;
    const id = this.fileId;
    const kind = this.kind;
    if (kind === 'pdf') {
      blob.arrayBuffer().then(buffer => {
        if (id === this.fileId) {
          this.pdfBytes = new Uint8Array(buffer);
        }
      });
    } else if (kind === 'image') {
      this.imageUrl = URL.createObjectURL(blob);
    }
  }

  get valid(): boolean {
    return !!this.edited && isPanelValid(this.edited);
  }

  get changed(): boolean {
    return !!this.original && !!this.edited && hasPanelChanges(panelChanges(this.original, this.edited));
  }

  get canSave(): boolean {
    return !this.busy && this.valid && this.changed;
  }

  get canSaveNext(): boolean {
    return !this.busy && this.valid && this.hasNext;
  }

  /**
   * "Arver fra Drift: Ledelse" — what the document reads with without own access, in the folder it will be saved in.
   * When the groups come from a parent that is not in `folders`, only the groups are shown (no "Arver fra …").
   */
  get inheritedLabel(): string {
    if (!this.edited) {
      return '';
    }
    const groups = this.folderGroups(this.edited.folderId);
    const source = accessSourceFolder(this.folders, this.edited.folderId);
    const folder = source ?? findFolder(this.folders, this.edited.folderId);
    if ((!source && groups.length) || !folder) {
      return this.translateService.instant('Read access: {{who}}', {who: this.whoLabel(groups)});
    }
    return this.translateService.instant('Inherits from {{folder}}: {{who}}', {
      folder: folder.name,
      who: this.whoLabel(source?.readGroups ?? []),
    });
  }

  get ownGroupsLabel(): string {
    const ids = this.edited?.readTagIds ?? [];
    return this.whoLabel(this.accessItems.filter(item => ids.includes(item.id)));
  }

  /** Read-only line for users who cannot manage. */
  get readAccessLabel(): string {
    if (!this.edited || !this.file) {
      return '';
    }
    const groups = this.edited.ownAccess ? this.file.readGroups : this.folderGroups(this.edited.folderId);
    return this.translateService.instant('Read access: {{who}}', {who: this.whoLabel(groups)});
  }

  editDate(): void {
    this.editingDate = true;
  }

  /** Godkend: same date, now approved; saved with the rest. */
  approveDate(): void {
    if (this.edited) {
      this.edited = {...this.edited, documentDateState: DocumentDateState.Approved};
    }
  }

  /** Ret: a date (`yyyy-MM-dd`) approves it, an empty field clears it (spec section 4, PUT files/{id}/date). */
  setDate(value: string): void {
    if (this.edited) {
      this.edited = {
        ...this.edited,
        documentDate: value || null,
        documentDateState: value ? DocumentDateState.Approved : DocumentDateState.None,
      };
    }
  }

  /** Mappe: saved last, through the D11 move flow. */
  setFolder(folderId: number | null): void {
    if (this.edited && folderId !== null) {
      this.edited = {...this.edited, folderId};
    }
  }

  /** Starts from the groups the document reads with today, so switching changes nobody's access by itself. */
  useOwnAccess(): void {
    if (!this.edited) {
      return;
    }
    const current = this.file?.ownAccess ? this.file.readGroups : this.folderGroups(this.edited.folderId);
    this.edited = {...this.edited, ownAccess: true, readTagIds: current.map(g => g.id)};
    this.refreshAccessItems(current);
  }

  useFolderAccess(): void {
    if (this.edited) {
      this.edited = {...this.edited, ownAccess: false, readTagIds: []};
    }
  }

  save(next: boolean): void {
    if (!this.file || !this.original || !this.edited || this.busy) {
      return;
    }
    const id = this.fileId;
    const file = this.file;
    const original = this.original;
    const edited = this.edited;
    const changes = panelChanges(original, edited);
    if (!hasPanelChanges(changes)) {
      if (next) {
        this.saved.emit({fileId: id, next: true});
      }
      return;
    }
    const steps: Observable<boolean>[] = [];
    if (changes.nameOrProperties) {
      steps.push(this.step(() => this.filesService.updateFileName({id, newName: edited.name.trim(), propertyIds: edited.propertyIds})));
    }
    if (changes.tags) {
      steps.push(this.step(() => this.filesService.updateFileTags({id, tags: edited.tagIds})));
    }
    if (changes.date) {
      steps.push(this.step(() => this.filesService.setDocumentDate(id, {date: edited.documentDate})));
    }
    if (changes.access) {
      steps.push(this.step(() => this.filesService.setFilesAccess({
        fileIds: [id], ownAccess: edited.ownAccess, tagIds: edited.ownAccess ? edited.readTagIds : [],
      })));
    }
    if (changes.folder) {
      const readGroups = this.groupsBeforeMove(file, original, edited, changes.access);
      steps.push(defer(() => this.moveFlow.move(
        [{id, folderId: original.folderId, ownAccess: edited.ownAccess, readGroups}],
        this.folders, edited.folderId, this.canManage)));
    }
    this.busy = true;
    concat(...steps).pipe(takeWhile(ok => ok, true), toArray()).subscribe({
      next: results => this.finish(id, results.every(Boolean), next),
      error: () => this.finish(id, false, next),
    });
  }

  /**
   * Ends a save of document `id`. "Next" only when everything was saved and the panel still shows that document —
   * if the selection moved on meanwhile, the page must not advance from the wrong row. Otherwise the saved state is
   * re-read (the new document is already loading when the selection moved on).
   */
  private finish(id: number, allSaved: boolean, next: boolean): void {
    this.busy = false;
    const stillShown = id === this.fileId;
    if (stillShown && (!allSaved || !next)) {
      this.loadData();
    }
    this.saved.emit({fileId: id, next: allSaved && next && stillShown});
  }

  /** defer(): nothing is sent before the previous step succeeded (the chain's takeWhile stops at the first false). */
  private step(call: () => Observable<OperationResult>): Observable<boolean> {
    return defer(() => this.succeeded(call()));
  }

  /**
   * True only for a success=true answer. The host turns a 404 into an empty completion, not an error:
   * `defaultIfEmpty(false)` makes that "not saved", so the chain stops there.
   */
  private succeeded(call$: Observable<OperationResult>): Observable<boolean> {
    return call$.pipe(map(res => !!res?.success), defaultIfEmpty(false));
  }

  /**
   * The groups the document reads with once the access step has run, so the D11 check compares against what is
   * really in force: none that matter with own access, the old folder's after "use the folder's access", else the
   * server's effective groups.
   */
  private groupsBeforeMove(file: FileModel, original: ArchivePanelValue, edited: ArchivePanelValue, accessChanged: boolean): ArchiveNamedItem[] {
    if (edited.ownAccess) {
      return [];
    }
    return accessChanged ? this.folderGroups(original.folderId) : file.readGroups;
  }

  /** The folder's effective groups; for the document's own folder when it is not in `folders`, the server's answer. */
  private folderGroups(folderId: number): ArchiveNamedItem[] {
    if (!findFolder(this.folders, folderId) && this.file && !this.file.ownAccess && this.file.folderId === folderId) {
      return this.file.readGroups;
    }
    return effectiveFolderGroups(this.folders, folderId);
  }

  private whoLabel(groups: ArchiveNamedItem[]): string {
    return whoCanRead(groups, key => this.translateService.instant(key));
  }

  /** The groups chosen right now (own access), else the document's; a chosen stale group must survive a tag refresh. */
  private selectedGroups(): ArchiveNamedItem[] {
    const fileGroups = this.file?.readGroups ?? [];
    if (!this.edited?.ownAccess) {
      return fileGroups;
    }
    const known = [...fileGroups, ...(this.edited.folderId ? this.folderGroups(this.edited.folderId) : [])];
    return this.edited.readTagIds.map(id => known.find(g => g.id === id) ?? this.accessItems.find(g => g.id === id) ?? {id, name: ''});
  }

  /**
   * Live groups plus the document's current groups that lost their last live member (kept and marked, never dropped
   * silently — dropping one would widen the access on the next save).
   */
  private refreshAccessItems(current: ArchiveNamedItem[] = this.file?.readGroups ?? []): void {
    this.accessItems = accessGroupItems(this.workerTags, current, this.translateService.instant('no members'));
  }
}
