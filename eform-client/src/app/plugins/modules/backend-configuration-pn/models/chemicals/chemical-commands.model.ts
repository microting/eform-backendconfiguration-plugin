import {ChemicalRemovalReason, ChemicalStockEntryKind, ChemicalStockUnit} from './chemical-enums';

/** Received/Consumed/initial: amount, or containerSize x containerCount. Adjusted: amount = counted balance. */
export interface ChemicalStockAmountModel {
  containerSize: number | null;
  containerCount: number | null;
  amount: number | null;
  unit: ChemicalStockUnit;
  batchLot: string | null;
  note: string | null;
  at: string | null;
}

export interface ChemicalRegisterPlacementCommand {
  locationId: number;
  chemicalId: number;
  productId: number | null;
  placementNote: string;
  initialStock: ChemicalStockAmountModel | null;
}

/** amount null = move everything. */
export interface ChemicalMovePlacementBody {
  targetLocationId: number;
  targetPlacementNote: string;
  amount: number | null;
}

export interface ChemicalRemovePlacementBody {
  reason: ChemicalRemovalReason;
  removedAt: string | null;
  note: string;
}

export interface ChemicalPlacementNoteBody {
  placementNote: string;
}

export interface ChemicalStockEntryBody {
  kind: ChemicalStockEntryKind;
  amount: ChemicalStockAmountModel;
}

export interface ChemicalCreateLocationCommand {
  propertyId: number;
  name: string;
  description: string;
  sortOrder: number | null;
}

export interface ChemicalUpdateLocationBody {
  name: string;
  description: string;
  sortOrder: number | null;
}
