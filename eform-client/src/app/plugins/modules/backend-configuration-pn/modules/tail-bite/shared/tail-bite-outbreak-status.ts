import {TailBiteOutbreakSummary} from '../../../models';

export type TailBiteOutbreakStatus = 'needsAssessment' | 'followUp' | 'readyToClose' | 'closed';

/** Open means not closed; "assessed" means a risk assessment exists (spec §5). */
export function outbreakStatus(s: TailBiteOutbreakSummary): TailBiteOutbreakStatus {
  if (s.closed) {
    return 'closed';
  }
  if (!s.assessed) {
    return 'needsAssessment';
  }
  return s.openActions > 0 ? 'followUp' : 'readyToClose';
}

/** Translation key per status. */
export const OUTBREAK_STATUS_LABEL: Record<TailBiteOutbreakStatus, string> = {
  needsAssessment: 'Needs assessment',
  followUp: 'Follow-up',
  readyToClose: 'Ready to close',
  closed: 'Closed',
};

/** Existing workspace badge classes (eform-angular-frontend _workspace-utilities.scss). */
export const OUTBREAK_STATUS_BADGE: Record<TailBiteOutbreakStatus, string> = {
  needsAssessment: 'badge badge-error',
  followUp: 'badge badge-warning',
  readyToClose: 'badge badge-neutral',
  closed: 'badge badge-success',
};
