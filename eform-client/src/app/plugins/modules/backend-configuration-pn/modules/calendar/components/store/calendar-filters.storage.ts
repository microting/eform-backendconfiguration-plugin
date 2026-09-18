import {CalendarFiltersModel} from '../../../../state';

/**
 * Per-user browser persistence of the calendar's last settings (#1303):
 * property, calendar(s), worker(s) and the week last viewed.
 *
 * Deliberately NOT persisted (status quo): the view mode, the worker-group
 * (team) filter and the planning-tag filter.
 *
 * Every storage access is wrapped: localStorage can be missing, full, or throw
 * on access (private mode, blocked site data). A failure here must never break
 * the calendar — it only means the settings are not remembered.
 */
export type SavedCalendarFilters =
  Pick<CalendarFiltersModel, 'propertyId' | 'activeBoardIds' | 'activeSiteIds' | 'currentDate'>;

export const CALENDAR_FILTERS_STORAGE_KEY_PREFIX = 'bcpn.calendar.filters.';

export function calendarFiltersStorageKey(userId: number): string {
  return `${CALENDAR_FILTERS_STORAGE_KEY_PREFIX}${userId}`;
}

function isPositiveInt(value: unknown): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value > 0;
}

function idList(value: unknown): number[] {
  return Array.isArray(value) ? value.filter(isPositiveInt) : [];
}

/**
 * A 'YYYY-MM-DD' string naming a real calendar day, or null. Rejects anything
 * `new Date()` would silently roll over (2026-02-31 -> March 3rd).
 */
export function parseSavedCalendarDate(value: unknown): string | null {
  if (typeof value !== 'string') return null;
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (!match) return null;
  const year = +match[1];
  const month = +match[2];
  const day = +match[3];
  const d = new Date(year, month - 1, day);
  if (d.getFullYear() !== year || d.getMonth() !== month - 1 || d.getDate() !== day) return null;
  return value;
}

/**
 * The user's saved settings, sanitised: ids that are not positive integers are
 * dropped, and an unparsable date is left out so the store keeps "today".
 * Returns null when nothing usable (no valid property) is saved, the payload is
 * corrupt, or storage throws. Existence of the ids is NOT checked here — the
 * calendar validates them against the freshly loaded lists.
 */
export function readSavedCalendarFilters(userId: number): Partial<SavedCalendarFilters> | null {
  if (!isPositiveInt(userId)) return null;
  try {
    const raw = window.localStorage.getItem(calendarFiltersStorageKey(userId));
    if (!raw) return null;
    const parsed = JSON.parse(raw);
    if (!parsed || typeof parsed !== 'object' || !isPositiveInt(parsed.propertyId)) return null;
    const restored: Partial<SavedCalendarFilters> = {
      propertyId: parsed.propertyId,
      activeBoardIds: idList(parsed.activeBoardIds),
      activeSiteIds: idList(parsed.activeSiteIds),
    };
    const currentDate = parseSavedCalendarDate(parsed.currentDate);
    if (currentDate) restored.currentDate = currentDate;
    return restored;
  } catch {
    return null;
  }
}

export function writeSavedCalendarFilters(userId: number, filters: CalendarFiltersModel): void {
  if (!isPositiveInt(userId) || !filters || filters.propertyId == null) return;
  const saved: SavedCalendarFilters = {
    propertyId: filters.propertyId,
    activeBoardIds: filters.activeBoardIds ?? [],
    activeSiteIds: filters.activeSiteIds ?? [],
    currentDate: filters.currentDate,
  };
  try {
    window.localStorage.setItem(calendarFiltersStorageKey(userId), JSON.stringify(saved));
  } catch {
    // Quota exceeded / storage blocked: the settings are simply not remembered.
  }
}
