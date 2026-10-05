import {Component, OnInit, inject} from '@angular/core';
import {MatDialog} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {BackendConfigurationPnInboxService} from '../../../../services';
import {
  InboxDocumentStatus,
  InboxListItemModel,
  InboxSuggestionKind,
  InboxSuggestionModel,
} from '../../../../models';
import {INBOX_PRESELECT_CONFIDENCE, INBOX_STATUS_LABELS, inboxConfidence, inboxIsUnknownSender} from '../../../../helpers';
import {InboxReviewDialogComponent} from '../inbox-review-dialog/inbox-review-dialog.component';

@Component({
  selector: 'app-inbox-container',
  templateUrl: './inbox-container.component.html',
  standalone: false,
})
export class InboxContainerComponent implements OnInit {
  private inboxService = inject(BackendConfigurationPnInboxService);
  private dialog = inject(MatDialog);
  private overlay = inject(Overlay);

  readonly Status = InboxDocumentStatus;
  readonly Kind = InboxSuggestionKind;
  readonly isUnknownSender = inboxIsUnknownSender;
  readonly statusLabels = INBOX_STATUS_LABELS;
  readonly confidence = inboxConfidence;
  /** Status filter choices after the default (null) view, in display order. */
  readonly statusOptions: InboxDocumentStatus[] = [
    InboxDocumentStatus.Ready,
    InboxDocumentStatus.Preparing,
    InboxDocumentStatus.SenderPending,
    InboxDocumentStatus.Failed,
    InboxDocumentStatus.Filed,
    InboxDocumentStatus.Rejected,
  ];
  documents: InboxListItemModel[] = [];
  loaded = false;
  /** null = the server's default view: everything open plus the last week's filed documents. */
  statusFilter: InboxDocumentStatus | null = null;
  search = '';

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.inboxService.list(this.statusFilter, this.search).subscribe(res => {
      this.loaded = true;
      if (res?.success) {
        this.documents = res.model ?? [];
      }
    });
  }

  suggested(d: InboxListItemModel, kind: InboxSuggestionKind): InboxSuggestionModel[] {
    return d.suggestions.filter(s => s.kind === kind && s.confidence >= INBOX_PRESELECT_CONFIDENCE);
  }

  statusChipClass(status: InboxDocumentStatus): string {
    switch (status) {
      case InboxDocumentStatus.Ready:
      case InboxDocumentStatus.Filed:
        return 'status-chip status-chip--aktiv';
      case InboxDocumentStatus.Preparing:
      case InboxDocumentStatus.SenderPending:
        return 'status-chip status-chip--afventer';
      default:
        return 'status-chip status-chip--inaktiv';
    }
  }

  review(d: InboxListItemModel): void {
    this.dialog
      .open(InboxReviewDialogComponent, {...dialogConfigHelper(this.overlay, d), width: '1040px', maxWidth: '96vw'})
      .afterClosed()
      .subscribe(changed => {
        if (changed) {
          this.load();
        }
      });
  }

  approveSender(d: InboxListItemModel): void {
    this.inboxService.approveSender(d.id).subscribe(res => this.reloadOnSuccess(res?.success));
  }

  /** `block` also adds a Block rule, so later mail from this address is refused. */
  rejectSender(d: InboxListItemModel, block: boolean): void {
    this.inboxService.rejectSender(d.id, block).subscribe(res => this.reloadOnSuccess(res?.success));
  }

  reject(d: InboxListItemModel): void {
    this.inboxService.reject(d.id).subscribe(res => this.reloadOnSuccess(res?.success));
  }

  private reloadOnSuccess(success: boolean | undefined): void {
    if (success) {
      this.load();
    }
  }
}
