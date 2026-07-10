export interface CalendarComplianceReportRequestModel {
  propertyId: number | null;
  boardIds: number[];
  tagIds: number[];
  siteIds: number[];
  status: 'open' | 'done' | 'all';
  dateFrom: string; // yyyy-MM-dd
  dateTo: string;   // yyyy-MM-dd
}

export interface CalendarComplianceReportRowModel {
  complianceId: number;
  taskDate: string;
  startHour: number;
  duration: number;
  isAllDay: boolean;
  title: string;
  propertyId: number;
  propertyName: string;
  boardId: number | null;
  boardName: string;
  tags: string[];
  workerNames: string[];
  completed: boolean;
  doneAt: string | null;
  sdkCaseId: number;
  eformId: number | null;
  planningId: number;
  areaRulePlanningId: number | null;
}
