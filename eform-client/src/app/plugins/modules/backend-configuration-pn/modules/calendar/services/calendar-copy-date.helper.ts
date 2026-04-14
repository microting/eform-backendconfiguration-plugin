/**
 * Adjusts the source event's date forward when it is in the past so that a
 * copy lands on a valid (today or future) day.
 *
 * Rules:
 * - If sourceDate is before today → today
 * - If sourceDate is today AND sourceStartHour is before now's hour-of-day → tomorrow
 * - Otherwise (today with future time, or future date) → sourceDate unchanged
 *
 * The start time itself is preserved by the caller — this helper only computes
 * the date string.
 *
 * @param sourceDate    YYYY-MM-DD of the source event
 * @param sourceStartHour Fractional hour 0–24 (e.g. 10.5 = 10:30)
 * @param now           Optional current time (defaults to new Date()) — exposed for tests
 * @returns YYYY-MM-DD of the date the copy should default to
 */
export function computeCopyDate(
  sourceDate: string,
  sourceStartHour: number,
  now: Date = new Date()
): string {
  const today = startOfDay(now);
  const source = startOfDay(parseLocalDate(sourceDate));

  if (source.getTime() < today.getTime()) {
    return toLocalDateString(today);
  }

  if (source.getTime() === today.getTime()) {
    const nowHour = now.getHours() + now.getMinutes() / 60;
    if (sourceStartHour < nowHour) {
      const tomorrow = new Date(today);
      tomorrow.setDate(today.getDate() + 1);
      return toLocalDateString(tomorrow);
    }
  }

  return sourceDate;
}

/**
 * Parses a "YYYY-MM-DD" string as a local Date at midnight, avoiding the
 * UTC interpretation `new Date("YYYY-MM-DD")` would otherwise apply.
 */
function parseLocalDate(dateStr: string): Date {
  const [y, m, d] = dateStr.split('-').map(n => parseInt(n, 10));
  return new Date(y, (m || 1) - 1, d || 1);
}

function startOfDay(d: Date): Date {
  const out = new Date(d);
  out.setHours(0, 0, 0, 0);
  return out;
}

function toLocalDateString(d: Date): string {
  const y = d.getFullYear();
  const m = (d.getMonth() + 1).toString().padStart(2, '0');
  const day = d.getDate().toString().padStart(2, '0');
  return `${y}-${m}-${day}`;
}
