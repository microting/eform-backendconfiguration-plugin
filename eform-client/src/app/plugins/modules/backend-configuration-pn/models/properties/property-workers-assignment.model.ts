export class PropertyAssignWorkersModel {
  siteId: number;
  assignments: PropertyAssignmentWorkerModel[] = [];
}

export class PropertyAssignmentWorkerModel {
  propertyId: number;
  isChecked: boolean;
}
