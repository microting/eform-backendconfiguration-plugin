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

const isWhole = (v: number | null, min: number, max = Number.MAX_SAFE_INTEGER): boolean =>
  v !== null && Number.isInteger(v) && v >= min && v <= max;

/**
 * The request for a draft, or null while it cannot be valid. Mirrors the server's checks
 * (TailBiteSetupService.ValidateRule): a location, at least one threshold, thresholds >= 1,
 * a 1–90 day window. The "not above its own location" check is left to the server; the
 * form only offers levels at or below the location.
 */
export function ruleInputFromDraft(d: TailBiteRuleDraft): TailBiteRuleInput | null {
  if (d.locationId === null || (d.minBittenPigs === null && d.minSevere === null)) {
    return null;
  }
  if ((d.minBittenPigs !== null && !isWhole(d.minBittenPigs, 1)) || (d.minSevere !== null && !isWhole(d.minSevere, 1))) {
    return null;
  }
  if (!isWhole(d.windowDays, 1, 90)) {
    return null;
  }
  return {
    locationId: d.locationId,
    minBittenPigs: d.minBittenPigs,
    minSevere: d.minSevere,
    windowDays: d.windowDays!,
    countDepth: d.countDepth,
  };
}
