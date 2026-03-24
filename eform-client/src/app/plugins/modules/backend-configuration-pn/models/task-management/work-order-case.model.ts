export class WorkOrderCaseModel {
  id: number;
  groupId: string;
  caseInitiated: Date;
  propertyName: string;
  areaName: string;
  createdByName: string;
  createdByText: string;
  lastAssignedTo: string;
  description: string;
  lastUpdateDate: Date;
  lastUpdatedBy: string;
  status: string;
  priority: number;
  pictureNames: string[];
}
