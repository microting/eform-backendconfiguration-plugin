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
