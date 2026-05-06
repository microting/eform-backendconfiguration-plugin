export type CalendarRepeatRule =
  | 'none'
  | 'daily'
  | 'weeklyOne'
  | 'weeklyAll'
  | 'monthlyDom'
  | 'yearlyOne'
  | 'custom';

export interface CalendarTaskModel {
  id: number;
  title: string;
  startHour: number;         // fractional hour, e.g. 9.5 = 09:30
  duration: number;           // duration in hours
  startText: string;         // "09:30"
  endText: string;           // "10:00"
  tags: string[];
  assigneeIds: number[];
  boardId: number;
  color: string;
  descriptionHtml: string;
  repeatRule: CalendarRepeatRule;
  repeatMeta?: CalendarRepeatMeta;
  taskDate: string;          // ISO "YYYY-MM-DD"
  repeatSeriesId?: string;
  completed: boolean;
  driveLink?: string;
  propertyId: number;
  isFromCompliance?: boolean;
  complianceId?: number;
  deadline?: string;
  nextExecutionTime?: string;
  planningId?: number;
  isAllDay?: boolean;
  exceptionId?: number;

  // Persisted custom-repeat fields surfaced from AreaRulePlanning so the
  // edit-modal can reconstruct a full CalendarRepeatMeta for an existing row.
  // All optional/nullable — older backends and rows without a custom rule
  // simply omit them.
  repeatType?: number | null;
  repeatEvery?: number | null;
  repeatEndMode?: number | null;
  repeatOccurrences?: number | null;
  repeatUntilDate?: string | null;
  dayOfWeek?: number | null;
  dayOfMonth?: number | null;
  repeatWeekdaysCsv?: string | null;

  // File attachments persisted on the AreaRulePlanning. All occurrences of a
  // recurring rule share the same attachment list — see
  // 2026-05-06-calendar-event-attachments-design.md (Q1 master-rule scope).
  attachments?: CalendarTaskAttachment[];
}

export interface CalendarTaskAttachment {
  id: number;
  originalFileName: string;
  mimeType: string;
  sizeBytes: number;
  downloadUrl: string;
}

export interface CalendarRepeatMeta {
  kind: string;
  n?: number;
  weekday?: number;
  weekdays?: number[];
  dom?: number;
  month?: number;
  endMode: 'never' | 'after' | 'until';
  afterCount?: number;
  untilTs?: number;
}

export interface CalendarTaskLayoutModel extends CalendarTaskModel {
  _colIndex: number;
  _colCount: number;
}
