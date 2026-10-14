/** Stored flags (Admin NOT expanded) - spec section 8. */
export interface ChemicalPermissionFlagsModel {
  view: boolean;
  register: boolean;
  remove: boolean;
  stock: boolean;
  manageLocations: boolean;
  admin: boolean;
}

export interface ChemicalWorkerPermissionModel {
  workerId: number;
  workerName: string;
  flags: ChemicalPermissionFlagsModel;
}

export interface ChemicalSetWorkerPermissionCommand {
  workerId: number;
  flags: ChemicalPermissionFlagsModel;
}

export interface ChemicalSettingsModel {
  propertyId: number;
  stockEnabled: boolean;
  digestRecipients: string[];
}

export interface ChemicalSettingsBody {
  stockEnabled: boolean;
  digestRecipients: string[];
}
