import {InboxDocumentStatus} from '../models';

/** Translation keys per InboxDocumentStatus. */
export const INBOX_STATUS_LABELS: Record<InboxDocumentStatus, string> = {
  [InboxDocumentStatus.Preparing]: 'Being prepared',
  [InboxDocumentStatus.SenderPending]: 'Approve sender',
  [InboxDocumentStatus.Ready]: 'Ready to file',
  [InboxDocumentStatus.Failed]: 'Could not be read',
  [InboxDocumentStatus.Filed]: 'Filed',
  [InboxDocumentStatus.Rejected]: 'Rejected',
};

/** Suggestions at or above this confidence are shown in the list and preselected in the review dialog. */
export const INBOX_PRESELECT_CONFIDENCE = 0.5;

/** Dot classes (existing workspace `.dashboard-dot`) and translation key for a suggestion's confidence. */
export function inboxConfidence(confidence: number): {dotClass: string; label: string} {
  if (confidence >= 0.75) {
    return {dotClass: 'dashboard-dot dashboard-dot--ok', label: 'High confidence'};
  }
  if (confidence >= INBOX_PRESELECT_CONFIDENCE) {
    return {dotClass: 'dashboard-dot dashboard-dot--warn', label: 'Medium confidence'};
  }
  return {dotClass: 'dashboard-dot dashboard-dot--bad', label: 'Low confidence'};
}
