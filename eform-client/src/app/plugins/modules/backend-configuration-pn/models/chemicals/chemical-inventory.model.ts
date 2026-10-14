import {ChemicalRemovalReason, ChemicalStockEntryKind, ChemicalStockEntryOrigin, ChemicalStockUnit} from './chemical-enums';
import {ChemicalPermissionFlagsModel} from './chemical-admin.model';
import {ChemicalRegisterEntryModel} from './chemical-register.model';

export interface ChemicalPropertyAccessModel {
  propertyId: number;
  name: string;
  permissions: ChemicalPermissionFlagsModel;
  stockEnabled: boolean;
  /** The calling user's worker id on this property; null when the caller has no worker there. */
  callerWorkerId: number | null;
}

export interface ChemicalLocationModel {
  id: number;
  propertyId: number;
  name: string;
  description: string;
  photoFileName: string | null;
  sortOrder: number;
  archived: boolean;
  updatedAt: string;
}

export interface ChemicalPlacementModel {
  id: number;
  locationId: number;
  propertyId: number;
  chemicalId: number;
  productId: number | null;
  placementNote: string;
  registeredByUserId: number;
  registeredByName: string;
  registeredAt: string;
  removedByUserId: number | null;
  removedByName: string | null;
  removedAt: string | null;
  removalReason: ChemicalRemovalReason | null;
  removalNote: string | null;
  movedFromPlacementId: number | null;
  balance: number;
  unit: ChemicalStockUnit | null;
  updatedAt: string;
  /** Id of the stock entry that zeroed the balance when the placement was removed; null otherwise. */
  writeOffEntryId: number | null;
}

export interface ChemicalStockEntryModel {
  id: number;
  placementId: number;
  kind: ChemicalStockEntryKind;
  containerSize: number | null;
  unit: ChemicalStockUnit;
  amount: number;
  containerCount: number | null;
  batchLot: string | null;
  note: string | null;
  byUserId: number;
  byName: string;
  at: string;
  /** Server-computed running balance after this entry. */
  balanceAfter: number;
  origin: ChemicalStockEntryOrigin;
  /** The other placement of a move (MovedOut/MovedIn); null otherwise. */
  counterpartPlacementId: number | null;
}

export interface ChemicalPlacementChangeModel {
  placements: ChemicalPlacementModel[];
  stockEntries: ChemicalStockEntryModel[];
  registerEntries: ChemicalRegisterEntryModel[];
}

/** GET properties/{id}/inventory: one property, locations incl. archived, open + 12-month-closed placements. */
export interface ChemicalInventoryModel {
  properties: ChemicalPropertyAccessModel[];
  locations: ChemicalLocationModel[];
  placements: ChemicalPlacementModel[];
  stockEntries: ChemicalStockEntryModel[];
  registerEntries: ChemicalRegisterEntryModel[];
  syncToken: string;
  full: boolean;
}
