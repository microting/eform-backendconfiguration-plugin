import type {CompliancePeriodPreset} from './compliance-report-state.service';

/**
 * Per-user browser persistence of the Compliance page's chosen PERIOD (#1299):
 * "Når Sæt periode vælges, så skal denne periode være default, indtil perioden
 * ændres". Any preset or a committed custom range is remembered, across reload
 * and login, until the user picks another one.
 *
 * Deliberately NOT persisted (status quo): property, calendar, tags, status,
 * employee, the view mode and the sort. Only the period was asked for.
 *
 * Mirrors the calendar's `calendar-filters.storage.ts` (#1303): the key carries
 * the user id because localStorage is per browser, not per account, and every
 * storage access is wrapped — localStorage can be missing, full, or throw on
 * access (private mode, blocked site data). A failure here must never break the
 * page; it only means the period is not remembered.
 */
export interface SavedCompliancePeriod {
  periodPreset: CompliancePeriodPreset;
  /** 'YYYY-MM-DD'; set only for a committed `custom` range. */
  customFrom: string | null;
  customTo: string | null;
}

/** What a read hands back to the state service: dates already parsed. */
export interface RestoredCompliancePeriod {
  periodPreset: CompliancePeriodPreset;
  customFrom: Date | null;
  customTo: Date | null;
}

export const COMPLIANCE_PERIOD_STORAGE_KEY_PREFIX = 'bcpn.compliance.period.';

/** Every preset a saved payload may name. Anything else is treated as corrupt. */
export const COMPLIANCE_PERIOD_PRESETS: readonly CompliancePeriodPreset[] = [
  '1',
  '3',
  '6',
  '12',
  'ytd',
  'ytd1y',
  'custom',
];

export function compliancePeriodStorageKey(userId: number): string {
  return `${COMPLIANCE_PERIOD_STORAGE_KEY_PREFIX}${userId}`;
}

export function isComplianceUserId(value: unknown): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value > 0;
}

function isPreset(value: unknown): value is CompliancePeriodPreset {
  return typeof value === 'string' && (COMPLIANCE_PERIOD_PRESETS as readonly string[]).indexOf(value) !== -1;
}

/**
 * A 'YYYY-MM-DD' string naming a real calendar day, as a LOCAL midnight Date —
 * or null. Rejects anything `new Date()` would silently roll over
 * (2026-02-31 → 3 March) and ISO timestamps, whose UTC parse would shift the
 * day in Danish time.
 */
export function parseSavedComplianceDate(value: unknown): Date | null {
  if (typeof value !== 'string') return null;
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (!match) return null;
  const year = +match[1];
  const month = +match[2];
  const day = +match[3];
  const d = new Date(year, month - 1, day);
  if (d.getFullYear() !== year || d.getMonth() !== month - 1 || d.getDate() !== day) return null;
  return d;
}

function toIsoDate(d: Date): string {
  return `${d.getFullYear()}-${(d.getMonth() + 1).toString().padStart(2, '0')}-${d
    .getDate()
    .toString()
    .padStart(2, '0')}`;
}

function isValidDate(d: unknown): d is Date {
  return d instanceof Date && !isNaN(d.getTime());
}

/**
 * The user's saved period, sanitised. Null when nothing is saved, the payload is
 * corrupt, the preset is unknown, a `custom` range is incomplete, unparsable or
 * backwards, or storage throws — the page then keeps its default (`År til
 * dato`).
 */
export function readSavedCompliancePeriod(userId: number): RestoredCompliancePeriod | null {
  if (!isComplianceUserId(userId)) return null;
  try {
    const raw = window.localStorage.getItem(compliancePeriodStorageKey(userId));
    if (!raw) return null;
    const parsed = JSON.parse(raw);
    if (!parsed || typeof parsed !== 'object' || !isPreset(parsed.periodPreset)) return null;
    if (parsed.periodPreset !== 'custom') {
      return {periodPreset: parsed.periodPreset, customFrom: null, customTo: null};
    }
    const customFrom = parseSavedComplianceDate(parsed.customFrom);
    const customTo = parseSavedComplianceDate(parsed.customTo);
    if (!customFrom || !customTo || customFrom > customTo) return null;
    return {periodPreset: 'custom', customFrom, customTo};
  } catch {
    return null;
  }
}

/**
 * Saves the period. A `custom` preset is saved only with a complete, ordered
 * COMMITTED range: choosing `Sæt periode` without pressing `Opdater periode`
 * has not chosen a period yet, so the previously saved one stays the default.
 */
export function writeSavedCompliancePeriod(
  userId: number,
  period: {periodPreset: CompliancePeriodPreset; customFrom: Date | null; customTo: Date | null}
): void {
  if (!isComplianceUserId(userId) || !period || !isPreset(period.periodPreset)) return;
  let saved: SavedCompliancePeriod;
  if (period.periodPreset === 'custom') {
    const {customFrom, customTo} = period;
    if (!isValidDate(customFrom) || !isValidDate(customTo)) return;
    const from = toIsoDate(customFrom);
    const to = toIsoDate(customTo);
    if (from > to) return;
    saved = {periodPreset: 'custom', customFrom: from, customTo: to};
  } else {
    saved = {periodPreset: period.periodPreset, customFrom: null, customTo: null};
  }
  try {
    window.localStorage.setItem(compliancePeriodStorageKey(userId), JSON.stringify(saved));
  } catch {
    // Quota exceeded / storage blocked: the period is simply not remembered.
  }
}
