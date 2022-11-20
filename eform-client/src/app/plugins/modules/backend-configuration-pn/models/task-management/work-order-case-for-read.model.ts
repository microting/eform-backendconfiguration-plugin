export class WorkOrderCaseForReadModel {
  id: number;
  propertyId: number;
  areaName: string;
  assignedSiteId: number;
  pictureNames: string[];
  description: string;
  priority: string;
  status: string;
  caseStatusEnum: number;
}
