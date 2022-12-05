export class WorkOrderCaseCreateModel {
  propertyId: number;
  areaName: string;
  assignedSiteId: number;
  files: File[];
  description: string;
  caseStatusEnum: number;
  priority: number;
}
