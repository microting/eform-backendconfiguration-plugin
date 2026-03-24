import {CalendarRepeatMeta, CalendarRepeatRule} from './calendar-task.model';

export interface CalendarTaskCreateModel {
  title: string;
  startHour: number;
  dur: number;
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
}

export interface CalendarTaskUpdateModel extends CalendarTaskCreateModel {
  id: number;
  repeatSeriesId?: string;
}

export type RepeatEditScope = 'this' | 'thisAndFollowing' | 'all';
export type RepeatDeleteScope = 'this' | 'thisAndFollowing' | 'all';
