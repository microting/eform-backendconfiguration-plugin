import {Component, OnInit, inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef, MatDialogTitle, MatDialogContent, MatDialogActions} from '@angular/material/dialog';
import {Observable} from 'rxjs';
import {CommonDictionaryModel, OperationResult, SharedTagModel} from 'src/app/common/models';
import {
  BackendConfigurationPnArchiveFoldersService,
  BackendConfigurationPnFileTagsService,
  BackendConfigurationPnInboxService,
  BackendConfigurationPnPropertiesService,
} from '../../../../services';
import {FileInboxDocumentModel, InboxListItemModel, InboxSuggestionKind, InboxSuggestionModel} from '../../../../models';
import {
  ArchiveFolderOption,
  INBOX_PRESELECT_CONFIDENCE,
  dateOnlyOf,
  folderOptions,
  inboxConfidence,
  inboxDateSuggestion,
  inboxFolderSuggestion,
  inboxIsUnknownSender,
  preselectedFolderId,
} from '../../../../helpers';
import {NgIf, NgFor, NgTemplateOutlet, DatePipe} from '@angular/common';
import {FormsModule} from '@angular/forms';
import {TranslatePipe} from '@ngx-translate/core';
import {MatIcon} from '@angular/material/icon';
import {MatFormField, MatLabel} from '@angular/material/form-field';
import {MatInput} from '@angular/material/input';
import {MtxSelect} from '@ng-matero/extensions/select';
import {ArchiveFolderPickerComponent} from '../archive-folder-picker/archive-folder-picker.component';
import {PdfViewerModule} from 'ng2-pdf-viewer';

/**
 * Review and file one Ready document: the PDF on the left; on the right pickers over every live
 * property and file tag, with the suggestions as quick toggles (preselected at confidence ≥ 0.5) and
 * their evidence text. A person always files by hand; at least one property and a folder are required.
 * The folder is preselected only from the hub's suggestion (confidence ≥ 0.5, a folder that still exists);
 * otherwise the person must choose one. The document date starts as the hub's suggestion and is filed as
 * suggested until a person approves it or sets another date.
 *
 * Closes with `true` when anything changed (filed, undone or rejected), so the list reloads.
 */
@Component({
  selector: 'app-inbox-review-dialog',
  imports: [NgIf, NgFor, NgTemplateOutlet, DatePipe, FormsModule, TranslatePipe, MatIcon, MatFormField, MatLabel, MatInput, MtxSelect, MatDialogTitle, MatDialogContent, MatDialogActions, ArchiveFolderPickerComponent, PdfViewerModule],
  templateUrl: './inbox-review-dialog.component.html',
})
export class InboxReviewDialogComponent implements OnInit {
  readonly data: InboxListItemModel = inject(MAT_DIALOG_DATA);
  private dialogRef = inject(MatDialogRef<InboxReviewDialogComponent>);
  private inboxService = inject(BackendConfigurationPnInboxService);
  private propertiesService = inject(BackendConfigurationPnPropertiesService);
  private fileTagsService = inject(BackendConfigurationPnFileTagsService);
  private foldersService = inject(BackendConfigurationPnArchiveFoldersService);

  readonly Kind = InboxSuggestionKind;
  readonly isUnknownSender = inboxIsUnknownSender;
  readonly confidence = inboxConfidence;
  readonly propertySuggestions = this.suggestionsOf(InboxSuggestionKind.Property);
  readonly tagSuggestions = this.suggestionsOf(InboxSuggestionKind.Tag);
  /** At most one folder suggestion per document, preselected like properties at confidence ≥ 0.5. */
  readonly folderSuggestion = inboxFolderSuggestion(this.data.suggestions);
  /** At most one date suggestion per document; filed unchanged, it stays marked as suggested (needs checking). */
  readonly dateSuggestion = inboxDateSuggestion(this.data.suggestions);
  folderOptions: ArchiveFolderOption[] = [];
  private knownFolderIds = new Set<number>();
  selectedFolderId: number | null = null;
  /** yyyy-MM-dd: targetDate is a Date after the host's DateInterceptor, dateOnlyOf reads its UTC day (serverDay). */
  documentDate: string | null = dateOnlyOf(this.dateSuggestion?.targetDate);
  /** True once a person approved the date or set it. */
  dateApproved = false;
  editingDate = false;

  availableProperties: CommonDictionaryModel[] = [];
  availableTags: SharedTagModel[] = [];
  selectedPropertyIds = this.preselected(this.propertySuggestions);
  selectedTagIds = this.preselected(this.tagSuggestions);
  name = this.data.fileName.replace(/\.pdf$/i, '');

  pdfBytes: Uint8Array | null = null;
  pdfFailed = false;
  filed = false;
  busy = false;
  private changed = false;

  /** The folder suggestion, unless it points at a folder deleted or unreadable since; such a suggestion is hidden. */
  get visibleFolderSuggestion(): InboxSuggestionModel | null {
    return this.folderSuggestion && this.folderKnown(this.folderSuggestion) ? this.folderSuggestion : null;
  }

  get folderSuggestionSelected(): boolean {
    return !!this.folderSuggestion && this.selectedFolderId === this.folderSuggestion.targetId;
  }

  get canFile(): boolean {
    return !this.busy
      && this.selectedPropertyIds.length > 0
      && this.selectedFolderId !== null
      && this.name.trim().length > 0;
  }

  ngOnInit(): void {
    this.propertiesService.getAllPropertiesDictionary().subscribe(res => {
      if (res?.success && res.model) {
        const properties = res.model;
        this.availableProperties = properties;
        // A suggestion can point at a property removed since; never send an id the picker cannot show.
        this.selectedPropertyIds = this.selectedPropertyIds.filter(id => properties.some(p => p.id === id));
      }
    });
    this.fileTagsService.getTags().subscribe(res => {
      if (res?.success && res.model) {
        const tags = res.model;
        this.availableTags = tags;
        this.selectedTagIds = this.selectedTagIds.filter(id => tags.some(t => t.id === id));
      }
    });
    this.foldersService.getFolders([]).subscribe(res => {
      if (res?.success && res.model) {
        const folders = res.model;
        this.knownFolderIds = new Set(folders.map(f => f.id));
        this.folderOptions = folderOptions(folders);
        // No fallback to the default folder: without a usable suggestion the person must choose one.
        this.selectedFolderId = preselectedFolderId(this.folderSuggestion, folders);
      }
    });
    this.inboxService.getPdf(this.data.id).subscribe({
      next: blob =>
        blob
          .arrayBuffer()
          .then(buffer => (this.pdfBytes = new Uint8Array(buffer)))
          .catch(() => (this.pdfFailed = true)),
      error: () => (this.pdfFailed = true),
    });
  }

  isSelected(s: InboxSuggestionModel): boolean {
    return this.selectionOf(s.kind).includes(s.targetId);
  }

  /** A suggestion toggle and the picker share one selection; a new array lets mtx-select see the change. */
  toggle(s: InboxSuggestionModel): void {
    const current = this.selectionOf(s.kind);
    const next = current.includes(s.targetId)
      ? current.filter(id => id !== s.targetId)
      : [...current, s.targetId];
    if (s.kind === InboxSuggestionKind.Property) {
      this.selectedPropertyIds = next;
    } else {
      this.selectedTagIds = next;
    }
  }

  /** A refused folder (deleted or unreadable since: success=false) is toasted and keeps the dialog open. */
  fileIt(): void {
    // canFile already requires a folder; the explicit null check narrows selectedFolderId to number.
    if (!this.canFile || this.selectedFolderId === null) {
      return;
    }
    const model: FileInboxDocumentModel = {
      name: this.name.trim(),
      propertyIds: this.selectedPropertyIds,
      tagIds: this.selectedTagIds,
      folderId: this.selectedFolderId,
      documentDate: this.documentDate,
      documentDateApproved: this.documentDate !== null && this.dateApproved,
    };
    this.run(this.inboxService.file(this.data.id, model), () => (this.filed = this.changed = true));
  }

  folderKnown(s: InboxSuggestionModel): boolean {
    return this.knownFolderIds.has(s.targetId);
  }

  /** A date a person enters counts as approved; an emptied field files without a date. */
  setDate(value: string): void {
    this.documentDate = value || null;
    this.dateApproved = !!value;
  }

  undo(): void {
    this.run(this.inboxService.undo(this.data.id), () => (this.filed = false));
  }

  /** `block` also adds a Block rule, so later mail from this sender is refused. */
  reject(block: boolean): void {
    this.run(this.inboxService.reject(this.data.id, block), () => this.dialogRef.close(true));
  }

  close(): void {
    this.dialogRef.close(this.changed);
  }

  private suggestionsOf(kind: InboxSuggestionKind): InboxSuggestionModel[] {
    return this.data.suggestions.filter(s => s.kind === kind).sort((a, b) => b.confidence - a.confidence);
  }

  private preselected(suggestions: InboxSuggestionModel[]): number[] {
    return [...new Set(suggestions.filter(s => s.confidence >= INBOX_PRESELECT_CONFIDENCE).map(s => s.targetId))];
  }

  /** Runs one action with the buttons disabled; `onSuccess` only when the server says success. */
  private run(call$: Observable<OperationResult>, onSuccess: () => void): void {
    this.busy = true;
    call$.subscribe({
      next: res => {
        this.busy = false;
        if (res?.success) {
          onSuccess();
        }
      },
      error: () => (this.busy = false),
      // The host interceptor swallows a 404 and completes empty (C4); the buttons must not stay disabled.
      complete: () => (this.busy = false),
    });
  }

  private selectionOf(kind: InboxSuggestionKind): number[] {
    return kind === InboxSuggestionKind.Property ? this.selectedPropertyIds : this.selectedTagIds;
  }
}
