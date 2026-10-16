import {ChemicalAlertThreshold, ChemicalDeadlineKind} from './chemical-enums';

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

/** Body of POST …/history and …/history/export (Plan E Task 9 ChemicalHistoryQuery). Dates yyyy-MM-dd, inclusive. */
export interface ChemicalHistoryQueryModel {
  from: string;
  to: string;
  userId: number | null;
  action: number | null;
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
