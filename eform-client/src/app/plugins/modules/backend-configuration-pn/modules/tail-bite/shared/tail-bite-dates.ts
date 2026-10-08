import {format} from 'date-fns';

/**
 * The server's timestamps are UTC. The tail-bite web endpoints send them with a trailing Z;
 * the foundation's outbreak endpoints send them without an offset (DateTimeKind.Unspecified).
 * An offset-less value is read as UTC, never as local time.
 * For server timestamps only; date-only values go through dateOnlyToLocal.
 */
export function parseServerUtc(value: string | null | undefined): Date | null {
  if (!value) {
    return null;
  }
  const hasZone = /([zZ]|[+-]\d{2}:?\d{2})$/.test(value);
  const date = new Date(hasZone ? value : `${value}Z`);
  return isNaN(date.getTime()) ? null : date;
}

/** A calendar day picked in the UI as the date-only string the API binds without shifting it. */
export function toDateOnly(date: Date): string {
  return format(date, 'yyyy-MM-dd');
}

/** Midnight UTC of a picked calendar day, for fields the API stores as a UTC instant (occupancy). */
export function toUtcMidnightIso(date: Date): string {
  return `${toDateOnly(date)}T00:00:00Z`;
}

/**
 * The ValidFrom of a pig count picked as calendar `day`: midnight UTC of that day, but never later than `now`.
 * East of UTC, "today" starts before midnight UTC (00:30 in UTC+2 is 22:30 UTC the day before), and a ValidFrom in
 * the future would not be the current count until UTC midnight (the server only reads counts valid now).
 */
export function occupancyValidFrom(day: Date, now: Date = new Date()): string {
  const midnight = toUtcMidnightIso(day);
  return Date.parse(midnight) > now.getTime() ? now.toISOString() : midnight;
}

/** A follow-up date ("yyyy-MM-dd…") lies before `today` (local calendar day). */
export function isOverdue(followUpDate: string, today: Date): boolean {
  return followUpDate.substring(0, 10) < toDateOnly(today);
}

/** A date-only server value ("yyyy-MM-ddT00:00:00") as a local Date on that calendar day. */
export function dateOnlyToLocal(value: string): Date {
  const [y, m, d] = value.substring(0, 10).split('-').map(Number);
  return new Date(y, m - 1, d);
}
