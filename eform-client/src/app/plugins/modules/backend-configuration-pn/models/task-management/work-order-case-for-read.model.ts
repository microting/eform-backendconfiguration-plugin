export class WorkOrderCaseForReadModel {
  id: number;
  groupId: string;
  propertyId: number;
  areaName: string;
  assignedSiteId: number;
  pictureNames: string[];
  description: string;
  priority: string;
  caseStatusEnum: number;
}
