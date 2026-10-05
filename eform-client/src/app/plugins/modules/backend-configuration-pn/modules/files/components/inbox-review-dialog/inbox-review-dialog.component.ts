import {Component, OnInit, inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {Observable} from 'rxjs';
import {CommonDictionaryModel, OperationResult, SharedTagModel} from 'src/app/common/models';
import {
  BackendConfigurationPnFileTagsService,
  BackendConfigurationPnInboxService,
  BackendConfigurationPnPropertiesService,
} from '../../../../services';
import {InboxListItemModel, InboxSuggestionKind, InboxSuggestionModel} from '../../../../models';
import {INBOX_PRESELECT_CONFIDENCE, inboxConfidence, inboxIsUnknownSender} from '../../../../helpers';

/**
 * Review and file one Ready document: the PDF on the left; on the right pickers over every live
 * property and file tag, with the suggestions as quick toggles (preselected at confidence ≥ 0.5) and
 * their evidence text. A person always presses Arkivér; at least one property is required.
 *
 * Closes with `true` when anything changed (filed, undone or rejected), so the list reloads.
 */
@Component({
  selector: 'app-inbox-review-dialog',
  templateUrl: './inbox-review-dialog.component.html',
  standalone: false,
})
export class InboxReviewDialogComponent implements OnInit {
  readonly data: InboxListItemModel = inject(MAT_DIALOG_DATA);
  private dialogRef = inject(MatDialogRef<InboxReviewDialogComponent>);
  private inboxService = inject(BackendConfigurationPnInboxService);
  private propertiesService = inject(BackendConfigurationPnPropertiesService);
  private fileTagsService = inject(BackendConfigurationPnFileTagsService);

  readonly Kind = InboxSuggestionKind;
  readonly isUnknownSender = inboxIsUnknownSender;
  readonly confidence = inboxConfidence;
  readonly propertySuggestions = this.suggestionsOf(InboxSuggestionKind.Property);
  readonly tagSuggestions = this.suggestionsOf(InboxSuggestionKind.Tag);

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

  get canFile(): boolean {
    return !this.busy && this.selectedPropertyIds.length > 0 && this.name.trim().length > 0;
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

  fileIt(): void {
    if (!this.canFile) {
      return;
    }
    const model = {name: this.name.trim(), propertyIds: this.selectedPropertyIds, tagIds: this.selectedTagIds};
    this.run(this.inboxService.file(this.data.id, model), () => (this.filed = this.changed = true));
  }

  undo(): void {
    this.run(this.inboxService.undo(this.data.id), () => (this.filed = false));
  }

  reject(): void {
    this.run(this.inboxService.reject(this.data.id), () => this.dialogRef.close(true));
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
    });
  }

  private selectionOf(kind: InboxSuggestionKind): number[] {
    return kind === InboxSuggestionKind.Property ? this.selectedPropertyIds : this.selectedTagIds;
  }
}
