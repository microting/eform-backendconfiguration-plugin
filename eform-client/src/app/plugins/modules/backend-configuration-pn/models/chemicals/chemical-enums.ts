/** Integer values of the C# enums: the base package (Microting.EformBackendConfigurationBase) and the plugin's ChemicalStockEntryOriginEnum. */
export enum ChemicalRemovalReason { Used = 1, Disposed = 2, Moved = 3 }

export enum ChemicalStockEntryKind { Received = 1, Consumed = 2, Adjusted = 3, MovedOut = 4, MovedIn = 5 }

/** How a stock entry came to be (plugin ChemicalStockEntryOriginEnum). */
export enum ChemicalStockEntryOrigin { Manual = 1, RemovalWriteOff = 2, Move = 3 }

export enum ChemicalStockUnit { L = 1, Kg = 2 }

export enum ChemicalAlertThreshold { SixMonths = 1, OneMonth = 2, Passed = 3, StatusChanged = 4 }

export enum ChemicalDeadlineKind { UseAndPossession = 1, Sales = 2 }

/** BMD product status codes (base package Constants.ProductStatusType) the Kemi screens name. */
export const CHEMICAL_STATUS_APPROVED = 5;
export const CHEMICAL_STATUS_DEREGISTERED = 6;
export const CHEMICAL_STATUS_EXPIRED = 7;
