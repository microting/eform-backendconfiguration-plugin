import {
  TAIL_BITE_FACTORS,
  TailBiteFactor,
  TailBiteFactorAnswers,
  TailBiteOutbreakAction,
  TailBiteOutbreakDetail,
  TailBiteSaveAssessmentRequest,
} from '../../../../models';
import {toDateOnly} from '../../shared/tail-bite-dates';

/** A new follow-up action being typed in; every field may still be empty. */
export interface TailBiteDraftAction {
  description: string;
  responsibleSiteId: number | null;
  followUpDate: Date | null;
}

/** One factor of the form: unanswered (null), yes or no, plus the actions added in this edit. */
export interface TailBiteFactorDraft {
  factor: TailBiteFactor;
  answer: boolean | null;
  newActions: TailBiteDraftAction[];
}

export type TailBiteFactorError = 'unanswered' | 'needsAction' | 'incompleteAction';

export function emptyDraftAction(): TailBiteDraftAction {
  return {description: '', responsibleSiteId: null, followUpDate: null};
}

/** The form for an outbreak: the saved answers, or all unanswered before the first assessment. */
export function draftFromDetail(detail: TailBiteOutbreakDetail): TailBiteFactorDraft[] {
  return TAIL_BITE_FACTORS.map(({factor, answerKey}) => ({
    factor,
    answer: detail.answers ? detail.answers[answerKey] : null,
    newActions: [],
  }));
}

/** Saved actions of a factor that still count: not withdrawn (done ones count, spec §5). */
export function liveExistingActions(detail: TailBiteOutbreakDetail, factor: TailBiteFactor): TailBiteOutbreakAction[] {
  return detail.actions.filter((a) => a.factor === factor && a.withdrawnAt === null);
}

export function isCompleteAction(a: TailBiteDraftAction): boolean {
  return a.description.trim().length > 0 && a.responsibleSiteId !== null && a.followUpDate !== null;
}

function isBlankAction(a: TailBiteDraftAction): boolean {
  return a.description.trim().length === 0 && a.responsibleSiteId === null && a.followUpDate === null;
}

/**
 * Per factor, why the form cannot be saved. Mirrors TailBiteOutbreakService.RequireActionsForYesFactors:
 * a yes needs a live saved action or a complete new one. A half-filled new action is an error even when
 * another action covers the factor, so nothing typed is silently dropped. Untouched blank rows are ignored.
 */
export function assessmentErrors(
  drafts: TailBiteFactorDraft[],
  detail: TailBiteOutbreakDetail,
): Map<TailBiteFactor, TailBiteFactorError> {
  const errors = new Map<TailBiteFactor, TailBiteFactorError>();
  for (const d of drafts) {
    if (d.answer === null) {
      errors.set(d.factor, 'unanswered');
    } else if (d.answer) {
      const started = d.newActions.filter((a) => !isBlankAction(a));
      if (started.some((a) => !isCompleteAction(a))) {
        errors.set(d.factor, 'incompleteAction');
      } else if (liveExistingActions(detail, d.factor).length + started.length === 0) {
        errors.set(d.factor, 'needsAction');
      }
    }
  }
  return errors;
}

/** The request for a valid form. New actions on factors answered no are dropped: the server refuses them. */
export function buildSaveRequest(drafts: TailBiteFactorDraft[]): TailBiteSaveAssessmentRequest {
  const answers = {} as TailBiteFactorAnswers;
  for (const {factor, answerKey} of TAIL_BITE_FACTORS) {
    answers[answerKey] = drafts.find((d) => d.factor === factor)?.answer === true;
  }
  const newActions = drafts
    .filter((d) => d.answer === true)
    .flatMap((d) => d.newActions.filter(isCompleteAction).map((a) => ({
      factor: d.factor,
      description: a.description.trim(),
      responsibleSiteId: a.responsibleSiteId!,
      followUpDate: toDateOnly(a.followUpDate!),
    })));
  return {answers, newActions};
}
