import {ChemicalAlertThreshold, ChemicalDeadlineKind, ChemicalRemovalReason, ChemicalStockUnit} from './chemical-enums';

/** Plan E Task 7 ChemicalDigestItemModel. */
export interface ChemicalDigestItemModel {
  placementId: number;
  chemicalId: number;
  productName: string;
  registrationNo: string;
  locationName: string;
  placementNote: string;
  deadlineKind: ChemicalDeadlineKind | null;
  /** ISO timestamp of a stored date: show its date part without a zone conversion (date pipe with 'UTC'). */
  deadline: string | null;
  threshold: ChemicalAlertThreshold | null;
  status: number | null;
  statusText: string;
  registeredByName: string;
  registeredAt: string | null;
}

/**
 * GET properties/{id}/digest-preview - the four sections of Plan D Contract C5 as the NEXT weekly
 * e-mail will list them (evaluated on its Monday 07:00 slot, `isoWeek` is that slot's week).
 */
export interface ChemicalDigestModel {
  propertyId: number;
  propertyName: string;
  isoWeek: number;
  generatedAt: string;
  passedDeadlines: ChemicalDigestItemModel[];
  upcomingDeadlines: ChemicalDigestItemModel[];
  deregisteredOrExpired: ChemicalDigestItemModel[];
  newRegistrations: ChemicalDigestItemModel[];
  recipients: string[];
  hasActionItems: boolean;
}

/** A file download: the bytes and the server-chosen name (Content-Disposition). */
export interface ChemicalDownload {
  blob: Blob;
  fileName: string;
}

/**
 * Body of POST …/history and …/history/export (Plan E Task 11 ChemicalHistoryRequest). From/To are exactly
 * yyyy-MM-dd (no time, no offset; the server refuses anything else), both inclusive Copenhagen days.
 */
export interface ChemicalHistoryQueryModel {
  from: string;
  to: string;
  /** 0 = System (BMD sync and alerts). */
  userId: number | null;
  action: ChemicalHistoryAction | null;
  locationId: number | null;
  page: number;
  pageSize: number;
}

/**
 * A failed download. `message` is the server's own reason for a refused request (400/409/422);
 * null for anything else (403, 404, 5xx, no answer), where the caller shows its own text.
 */
export interface ChemicalDownloadError {
  status: number;
  message: string | null;
}

/** Values of the plugin's ChemicalHistoryActionEnum (Plan E Task 9). */
export enum ChemicalHistoryAction {
  Registered = 1, Moved = 2, Removed = 3, NoteChanged = 4, StockReceived = 5, StockConsumed = 6, StockAdjusted = 7,
  LocationCreated = 8, LocationRenamed = 9, LocationArchived = 10, LocationPhotoChanged = 11,
  PermissionChanged = 12, SettingsChanged = 13, StatusChanged = 14, AlertSent = 15,
}

/**
 * One immutable W6 line (Plan E Task 9 ChemicalHistoryEventModel). Which fields are set depends on the action;
 * ChemicalHistoryDetailsFormatter (C#) and formatHistoryDetails (TS) read the same ones.
 */
export interface ChemicalHistoryEventModel {
  /** UTC timestamp; the server may send it without an offset (see asUtcIso). */
  at: string;
  /** 0 = System (BMD sync and alerts). */
  userId: number;
  userName: string;
  action: ChemicalHistoryAction;
  placementId: number | null;
  chemicalId: number | null;
  productName: string;
  registrationNo: string;
  locationId: number | null;
  locationName: string;
  targetLocationId: number | null;
  targetLocationName: string;
  /** Signed: a consumption or a write-off is negative. */
  amount: number | null;
  unit: ChemicalStockUnit | null;
  balanceAfter: number | null;
  removalReason: ChemicalRemovalReason | null;
  note: string;
  field: string;
  oldValue: string;
  newValue: string;
  status: number | null;
  statusText: string;
  threshold: ChemicalAlertThreshold | null;
  deadlineKind: ChemicalDeadlineKind | null;
  pushCount: number;
  emailCount: number;
  workerName: string;
  /** Flag names: View, Register, Remove, Stock, ManageLocations, Admin. */
  permissionsAdded: string[];
  permissionsRemoved: string[];
}

export interface ChemicalHistoryUserModel {
  userId: number;
  name: string;
}

/** One page (newest first), the total after filtering, and every user seen in the period (never System). */
export interface ChemicalHistoryPageModel {
  events: ChemicalHistoryEventModel[];
  total: number;
  users: ChemicalHistoryUserModel[];
}
