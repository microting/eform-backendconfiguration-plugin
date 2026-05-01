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
  // CSV of JS getDay() weekday indices ("1,3,5") — only populated for
  // multi-day weekly custom rules. Cleared (sent as null) for any non-custom
  // rule so the backend column is wiped on rule change.
  repeatWeekdaysCsv?: string | null;
}

export interface CalendarTaskUpdateModel extends CalendarTaskCreateModel {
  id: number;
  repeatSeriesId?: string;
}

export type RepeatEditScope = 'this' | 'thisAndFollowing' | 'all';
export type RepeatDeleteScope = 'this' | 'thisAndFollowing' | 'all';
