import {CalendarTaskModel} from '../../../../models/calendar';

/**
 * #1302 / #1140 — the date the task list opens its edit modal on.
 *
 * A `tasks/index` row's `taskDate` is the SERIES START. The edit modal
 * (`TaskCreateEditModalComponent`) locks itself when that date+time is in the
 * past and `onSave()` returns early on the same check, so every series that
 * started before today opened read-only from the list — for admins too.
 *
 * Rule:
 *  - the series start has not started yet (a future-start series, or a one-off
 *    later today) → keep `taskDate` (unchanged behaviour);
 *  - otherwise → the first of `upcomingOccurrenceDates` whose start time is
 *    still ahead. Those dates come from the server's own occurrence iterators
 *    (the ones the calendar renders with), so this never re-derives a
 *    recurrence client-side. "Start" rather than end time because `onSave()`
 *    gates on the START time — an occurrence already under way would open
 *    editable but refuse to save;
 *  - nothing qualifies (one-off in the past, ended series) → keep `taskDate`,
 *    which opens read-only exactly as before.
 *
 * Times are compared in the browser's local time, like the modal does
 * (`isInPast` builds a local Date from the date + "HH:mm").
 */
export function resolveEditOccurrenceDate(
  task: Pick<CalendarTaskModel, 'taskDate' | 'startHour' | 'upcomingOccurrenceDates'>,
  now: Date = new Date(),
): string {
  const startsAfterNow = (isoDate: string): boolean => {
    const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(isoDate ?? '');
    if (!m) {
      return false;
    }
    const start = new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]));
    start.setMinutes(Math.round((task.startHour ?? 0) * 60));
    return start.getTime() > now.getTime();
  };

  if (!task.taskDate || startsAfterNow(task.taskDate)) {
    return task.taskDate;
  }
  return (task.upcomingOccurrenceDates ?? []).find(startsAfterNow) ?? task.taskDate;
}
