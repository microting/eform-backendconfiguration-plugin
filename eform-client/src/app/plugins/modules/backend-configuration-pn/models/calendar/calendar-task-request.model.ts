import {CalendarRepeatMeta, CalendarRepeatRule} from './calendar-task.model';

export interface CalendarTaskCreateModel {
  title: string;
  startHour: number;
  duration: number;
  tags: string[];
  assigneeIds: number[];
  boardId: number;
  color: string;
  descriptionHtml: string;
  repeatRule: CalendarRepeatRule;
  repeatMeta?: CalendarRepeatMeta;
  taskDate: string;
  driveLink?: string;
  propertyId: number;
  // Selected eForm (SDK CheckList id), null when the event has no eForm.
  // Declared explicitly so the field is compiler-checked: it used to reach the
  // backend only because the modal typed its payload `any`, which made a
  // rename/typo silently drop the eForm from the request.
  eformId: number | null;
  // CSV of JS getDay() weekday indices ("1,3,5") — only populated for
  // multi-day weekly custom rules. Cleared (sent as null) for any non-custom
  // rule so the backend column is wiped on rule change.
  repeatWeekdaysCsv?: string | null;
}

export interface CalendarTaskUpdateModel extends CalendarTaskCreateModel {
  id: number;
  repeatSeriesId?: string;
  // Pre-edit occurrence date ("YYYY-MM-DD") for scope-aware edits (#885).
  originalDate?: string;
}

export type RepeatEditScope = 'this' | 'thisAndFollowing' | 'all';
export type RepeatDeleteScope = 'this' | 'thisAndFollowing' | 'thisAndFollowingIncludingCompleted' | 'all';
