import {format} from 'date-fns';

/**
 * A date the server sent. The host app's DateInterceptor (src/app/common/interceptors/date.interceptor.ts) turns every
 * ISO date-time string in a response body into a Date (date-fns parseJSON, which reads an offset-less value as UTC), so a
 * value from the API arrives as a Date; one built by hand or in a test may still be the ISO string.
 */
export type ServerDate = string | Date;

/**
 * The calendar day ("yyyy-MM-dd") of a date-only server value. The server sends "yyyy-MM-ddT00:00:00", which the
 * interceptor reads as midnight UTC, so the UTC day of the Date is the server's calendar day in every browser time zone.
 */
export function serverDay(value: ServerDate): string {
  return (value instanceof Date ? value.toISOString() : value).substring(0, 10);
}

/**
 * The server's timestamps are UTC. The tail-bite web endpoints send them with a trailing Z;
 * the foundation's outbreak endpoints send them without an offset (DateTimeKind.Unspecified).
 * An offset-less value is read as UTC, never as local time.
 * For server timestamps only; date-only values go through dateOnlyToLocal.
 */
export function parseServerUtc(value: ServerDate | null | undefined): Date | null {
  if (!value) {
    return null;
  }
  if (value instanceof Date) {
    // Already read by the interceptor (as UTC when it had no offset).
    return isNaN(value.getTime()) ? null : value;
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
export function isOverdue(followUpDate: ServerDate, today: Date): boolean {
  return serverDay(followUpDate) < toDateOnly(today);
}

/** A date-only server value ("yyyy-MM-ddT00:00:00") as a local Date on that calendar day. */
export function dateOnlyToLocal(value: ServerDate): Date {
  const [y, m, d] = serverDay(value).split('-').map(Number);
  return new Date(y, m - 1, d);
}
