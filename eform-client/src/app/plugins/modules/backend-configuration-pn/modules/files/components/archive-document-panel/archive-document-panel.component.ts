import {Component, EventEmitter, Input, OnChanges, Output, SimpleChanges} from '@angular/core';
import {TranslateService} from '@ngx-translate/core';
import {Observable, concat, defaultIfEmpty, defer, map, takeWhile, toArray} from 'rxjs';
import {CommonDictionaryModel, OperationResult, SharedTagModel} from 'src/app/common/models';
import {ArchiveFolderModel, ArchiveNamedItem, DocumentDateState, FileModel} from '../../../../models';
import {BackendConfigurationPnFilesService} from '../../../../services';
import {
  ArchiveFolderOption,
  ArchivePanelValue,
  accessGroupItems,
  accessSourceFolder,
  effectiveFolderGroups,
  findFolder,
  groupNames,
  hasPanelChanges,
  isPanelValid,
  panelChanges,
  panelValueOf,
} from '../../../../helpers';
import {ArchiveMoveFlowService} from '../../services/archive-move-flow.service';

export interface ArchivePanelSaved {
  fileId: number;
  /** True for "Gem og næste": the page opens the next document. */
  next: boolean;
}

/**
 * The selected document (W1, right column; a drawer below 1100 px). Edits are kept locally and saved
 * together: name + properties, etiketter, document date, read access, and the folder last, because a
 * folder move may need the D11 confirmation and can be cancelled.
 */
@Component({
  selector: 'app-archive-document-panel',
  templateUrl: './archive-document-panel.component.html',
  standalone: false,
})
export class ArchiveDocumentPanelComponent implements OnChanges {
  @Input() fileId!: number;
  @Input() folders: ArchiveFolderModel[] = [];
  @Input() folderOptions: ArchiveFolderOption[] = [];
  @Input() properties: CommonDictionaryModel[] = [];
  @Input() availableTags: SharedTagModel[] = [];
  /** Live medarbejdergrupper; only loaded for admins. */
  @Input() workerTags: CommonDictionaryModel[] = [];
  @Input() isAdmin = false;
  @Input() drawer = false;
  /** "1 af 2 valgte" while several documents are selected. */
  @Input() positionLabel: string | null = null;
  @Input() hasNext = false;
  @Output() saved = new EventEmitter<ArchivePanelSaved>();
  @Output() closed = new EventEmitter<void>();

  readonly DateState = DocumentDateState;
  file: FileModel | null = null;
  original: ArchivePanelValue | null = null;
  /** Dates are `yyyy-MM-dd` strings here (panelValueOf reads the server Date via serverDay). */
  edited: ArchivePanelValue | null = null;
  accessItems: ArchiveNamedItem[] = [];
  editingDate = false;
  pdfBytes: Uint8Array | null = null;
  busy = false;

  constructor(
    private filesService: BackendConfigurationPnFilesService,
    private moveFlow: ArchiveMoveFlowService,
    private translateService: TranslateService,
  ) {}

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['fileId']) {
      this.load();
    } else if (changes['workerTags']) {
      this.refreshAccessItems();
    }
  }

  /** Opens a document: its data and the PDF preview, from a clean view. */
  load(): void {
    this.file = null;
    this.original = null;
    this.edited = null;
    this.editingDate = false;
    this.pdfBytes = null;
    this.loadData();
    this.loadPdf();
  }

  /**
   * (Re)reads the document's data; the current view stays until the reply arrives. getFile completes empty on a 404
   * (the document is gone or no longer readable): then the panel closes instead of standing blank.
   */
  private loadData(): void {
    const id = this.fileId;
    this.filesService.getFile(id).pipe(defaultIfEmpty(null)).subscribe(res => {
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
      }
    });
  }

  private loadPdf(): void {
    const id = this.fileId;
    this.filesService.getPdfFile(id).subscribe({
      next: (blob: Blob | null) => {
        if (blob) {
          blob.arrayBuffer().then(buffer => {
            if (id === this.fileId) {
              this.pdfBytes = new Uint8Array(buffer);
            }
          });
        }
      },
      error: () => (this.pdfBytes = null),
    });
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

  /** Read-only line for non-admins. */
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
        this.folders, edited.folderId, this.isAdmin)));
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
    return groups.length ? groupNames(groups) : this.translateService.instant('everyone with archive access');
  }

  /**
   * Live groups plus the document's current groups that lost their last live member (kept and marked, never dropped
   * silently — dropping one would widen the access on the next save).
   */
  private refreshAccessItems(current: ArchiveNamedItem[] = this.file?.readGroups ?? []): void {
    this.accessItems = accessGroupItems(this.workerTags, current, this.translateService.instant('no members'));
  }
}
