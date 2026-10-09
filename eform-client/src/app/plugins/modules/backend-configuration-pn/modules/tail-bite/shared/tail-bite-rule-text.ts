import {TailBiteRuleInput, TailBiteRuleSettings} from '../../../models';

/** The part of TranslateService the helpers need, so they stay testable without Angular. */
export interface TailBiteTranslator {
  instant(key: string, params?: Record<string, unknown>): string;
}

/** "5 bitten pigs or 1 severe within 7 days, counted per level 1" in the user's language. */
export function describeRule(t: TailBiteTranslator, r: TailBiteRuleSettings): string {
  const params = {pigs: r.minBittenPigs, severe: r.minSevere, days: r.windowDays};
  let key: string;
  if (r.minBittenPigs !== null && r.minSevere !== null) {
    key = '{{pigs}} bitten pigs or {{severe}} severe within {{days}} days';
  } else if (r.minBittenPigs !== null) {
    key = '{{pigs}} bitten pigs within {{days}} days';
  } else {
    key = '{{severe}} severe within {{days}} days';
  }
  const threshold = t.instant(key, params);
  return `${threshold}, ${t.instant('counted per level {{depth}}', {depth: r.countDepth})}`;
}

/** Editable rule form; numbers are null while the input is empty. */
export interface TailBiteRuleDraft {
  ruleId: number | null;
  locationId: number | null;
  minBittenPigs: number | null;
  minSevere: number | null;
  windowDays: number | null;
  countDepth: number;
  version: number;
}

export const isWhole = (v: number | null, min: number, max = Number.MAX_SAFE_INTEGER): boolean =>
  v !== null && Number.isInteger(v) && v >= min && v <= max;

/** What stops a draft from being saved; null when it is valid. Mirrors the server's checks. */
export type TailBiteDraftProblem = 'location' | 'threshold-missing' | 'threshold-invalid' | 'window';

export function draftProblem(d: TailBiteRuleDraft): TailBiteDraftProblem | null {
  if (d.locationId === null) {
    return 'location';
  }
  if (d.minBittenPigs === null && d.minSevere === null) {
    return 'threshold-missing';
  }
  if ((d.minBittenPigs !== null && !isWhole(d.minBittenPigs, 1)) || (d.minSevere !== null && !isWhole(d.minSevere, 1))) {
    return 'threshold-invalid';
  }
  return isWhole(d.windowDays, 1, 90) ? null : 'window';
}

/**
 * The request for a draft, or null while it cannot be valid (see draftProblem). The "not above its own
 * location" check is left to the server; the form only offers levels at or below the location.
 */
export function ruleInputFromDraft(d: TailBiteRuleDraft): TailBiteRuleInput | null {
  if (draftProblem(d) !== null) {
    return null;
  }
  return {
    locationId: d.locationId!,
    minBittenPigs: d.minBittenPigs,
    minSevere: d.minSevere,
    windowDays: d.windowDays!,
    countDepth: d.countDepth,
  };
}
