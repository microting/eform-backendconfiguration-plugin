export class PropertyAssignWorkersModel {
  siteId: number;
  assignments: PropertyAssignmentWorkerModel[] = [];
  timeRegistrationEnabled: boolean;
}

export class PropertyAssignmentWorkerModel {
  propertyId: number;
  isChecked: boolean;
}


export class PlanAssignmentWorkerModel {
  siteId: number;
  isChecked: boolean;
}
