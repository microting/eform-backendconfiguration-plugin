import {CalendarRepeatRule, CalendarTaskModel} from '../../../models/calendar';

/**
 * Maps the backend AreaRulePlanning repeat-type/-every integers onto the
 * frontend's `CalendarRepeatRule` union. Extracted verbatim from
 * CalendarContainerComponent so both the week-grid and the task-list page
 * derive the repeat rule identically.
 */
export function mapRepeatType(repeatType: number, repeatEvery: number): CalendarRepeatRule {
  if (!repeatType || repeatType === 0) return 'none';
  switch (repeatType) {
    case 1: return repeatEvery === 1 ? 'daily' : 'custom';
    case 2: return repeatEvery === 1 ? 'weeklyOne' : 'custom';
    case 3: return repeatEvery === 1 ? 'monthlyDom' : 'custom';
    case 4: return repeatEvery === 1 ? 'yearlyOne' : 'custom';
    default: return 'custom';
  }
}

/**
 * Converts a raw `tasks/week` (or `tasks/index`) response item into the
 * `CalendarTaskModel` that the calendar UI — and the create/edit modal
 * (`data.task`) — consumes. The DTO already matches the model shape; the only
 * derived field is `repeatRule`, computed from the persisted repeat integers.
 */
export function mapResponseToCalendarTask(dto: any): CalendarTaskModel {
  return {
    ...dto,
    repeatRule: mapRepeatType(dto.repeatType ?? 0, dto.repeatEvery ?? 1),
  };
}
