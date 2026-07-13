export interface CalendarPrepareCompleteResult {
  sdkCaseId: number;
  templateId: number | null;
  propertyId: number;
  complianceId: number;
  assignedSiteId: number | null;
  deadline: string;
  eventStart: string;
}
