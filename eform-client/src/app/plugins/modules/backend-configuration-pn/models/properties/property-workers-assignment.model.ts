export class PropertyAssignWorkersModel {
  siteId: number;
  assignments: PropertyAssignmentWorkerModel[] = [];
  timeRegistrationEnabled: boolean;
  taskManagementEnabled: boolean;
}

export class PropertyAssignmentWorkerModel {
  propertyId: number;
  isChecked: boolean;
  isLocked: boolean;
}


export class PlanAssignmentWorkerModel {
  siteId: number;
  isChecked: boolean;
}
