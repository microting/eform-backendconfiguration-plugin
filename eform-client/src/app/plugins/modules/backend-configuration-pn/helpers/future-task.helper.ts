/**
 * #1300 — the compliance pages (Detaljer, Rapport, the legacy `/compliances`
 * table and the task tracker) must not complete or delete an UNCOMPLETED task
 * whose date lies after today. The calendar is deliberately NOT gated: it
 * completes future occurrences early on purpose.
 *
 * "Today" is the DANISH calendar date (Europe/Copenhagen), the same date the
 * server's guard (`ComplianceFutureTaskGuard`) uses, so the UI never offers an
 * action the server then refuses — whatever time zone the browser runs in. The
 * comparison is date-level: a task's start hour plays no part.
 *
 * Everything here compares `yyyy-MM-dd` keys, which sort chronologically as
 * plain strings; no `Date` arithmetic across a DST switch is involved.
 */

const COPENHAGEN_TIME_ZONE = 'Europe/Copenhagen';

let copenhagenFormatter: Intl.DateTimeFormat | null = null;

function getCopenhagenFormatter(): Intl.DateTimeFormat {
  if (!copenhagenFormatter) {
    copenhagenFormatter = new Intl.DateTimeFormat('en-GB', {
      timeZone: COPENHAGEN_TIME_ZONE,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
    });
  }
  return copenhagenFormatter;
}

function pad2(value: number): string {
  return value < 10 ? `0${value}` : `${value}`;
}

/** Today's date in Copenhagen at the instant `now`, as `yyyy-MM-dd`. */
export function copenhagenDateKey(now: Date = new Date()): string {
  const parts = getCopenhagenFormatter().formatToParts(now);
  const get = (type: Intl.DateTimeFormatPartTypes) =>
    parts.find((part) => part.type === type)?.value ?? '';
  return `${get('year')}-${get('month')}-${get('day')}`;
}

/**
 * The calendar date of a task, as `yyyy-MM-dd`, or `null` when it cannot be
 * read.
 *
 *  - A STRING is read by its leading `yyyy-MM-dd` — the compliance report's
 *    `taskDate` is exactly that, and a server timestamp starts with it.
 *  - A `Date` is read by its UTC components. The app's `DateInterceptor` turns
 *    the server's offset-less timestamps (`Compliance.Deadline`, stored as a
 *    date at midnight) into `Date`s as UTC, so the UTC components are the
 *    server's calendar date, independent of the browser's zone.
 */
export function taskDateKey(taskDate: string | Date | null | undefined): string | null {
  if (taskDate == null) {
    return null;
  }
  if (taskDate instanceof Date) {
    if (isNaN(taskDate.getTime())) {
      return null;
    }
    return `${taskDate.getUTCFullYear()}-${pad2(taskDate.getUTCMonth() + 1)}-${pad2(taskDate.getUTCDate())}`;
  }
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(taskDate);
  return match ? `${match[1]}-${match[2]}-${match[3]}` : null;
}

/**
 * True when the task's date is AFTER today's Copenhagen date. An unreadable
 * date is never "future" here: the server's guard is the authority, and a
 * client-side false positive would hide a legitimate action.
 */
export function isFutureTask(
  taskDate: string | Date | null | undefined,
  now: Date = new Date(),
): boolean {
  const key = taskDateKey(taskDate);
  return key !== null && key > copenhagenDateKey(now);
}

/**
 * The legacy `/compliances` table and the task tracker DISPLAY
 * `Compliance.Deadline − 1 day` as their "Deadline"
 * (`BackendConfigurationCompliancesService.Index`,
 * `BackendConfigurationTaskTrackerHelper`). The server's guard judges the
 * compliance's own date, so this adds the day back before comparing — the
 * button is hidden exactly when the server would refuse.
 */
export function isFutureLegacyDeadline(
  displayedDeadline: string | Date | null | undefined,
  now: Date = new Date(),
): boolean {
  const key = taskDateKey(displayedDeadline);
  if (key === null) {
    return false;
  }
  const [y, m, d] = key.split('-').map((part) => parseInt(part, 10));
  const next = new Date(Date.UTC(y, m - 1, d + 1));
  return isFutureTask(next, now);
}

/** The `source` flag the compliance pages send so the server applies its #1300 guard. */
export const COMPLIANCE_PAGE_SOURCE = 'compliance';
