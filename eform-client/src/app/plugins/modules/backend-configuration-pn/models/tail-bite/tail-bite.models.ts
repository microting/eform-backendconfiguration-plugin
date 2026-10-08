/**
 * Tail biting (halebid) web admin. Mirrors the C# records in
 * BackendConfiguration.Pn/Services/TailBite/TailBiteModels.cs and TailBiteWebModels.cs
 * as Newtonsoft writes them: camelCase, enums as numbers, DateTime as an ISO string.
 * Every server date is typed `string | Date`: the host app's DateInterceptor turns ISO date-time strings in a response
 * into Dates before this code sees them (see ServerDate and serverDay in modules/tail-bite/shared/tail-bite-dates.ts).
 */

/** TailBiteFactor in the base: the six risk-assessment factors, stored as codes. */
export enum TailBiteFactor {
  Water = 0,
  Feed = 1,
  ActivityMaterial = 2,
  Climate = 3,
  Health = 4,
  Management = 5,
}

/** TailBiteOccupancySource in the base. Only Manual is written in v1. */
export enum TailBiteOccupancySource {
  Manual = 0,
  Integration = 1,
}

export interface TailBitePropertyStatus {
  propertyId: number;
  name: string;
  enabled: boolean;
}

/**
 * A worker on the property for the outbreak page: site id and name. A resigned worker is listed so a past responsible
 * person keeps a name, with `assignable` false: the pickers do not offer them.
 */
export interface TailBiteAssignableWorker {
  siteId: number;
  name: string;
  assignable: boolean;
}

/** One worker on a property. A worker can hold several PropertyWorker rows; the toggle sets all of them. */
export interface TailBiteWorker {
  siteId: number;
  name: string;
  isManager: boolean;
  propertyWorkerIds: number[];
}

/** Depth is the absolute distance from the root (root = 0). Removed nodes stay in the tree for history. */
export interface TailBiteLocationNode {
  id: number;
  parentId: number | null;
  name: string;
  sortOrder: number;
  qrCode: string;
  depth: number;
  removed: boolean;
}

export interface TailBiteActionType {
  id: number;
  code: string;
  name: string;
  sortOrder: number;
}

export interface TailBiteLocationTree {
  propertyId: number;
  treeVersion: number;
  locations: TailBiteLocationNode[];
  actionTypes: TailBiteActionType[];
}

/** The thresholds of a rule; at least one of minBittenPigs / minSevere is set. */
export interface TailBiteRuleSettings {
  minBittenPigs: number | null;
  minSevere: number | null;
  windowDays: number;
  countDepth: number;
}

export interface TailBiteRule extends TailBiteRuleSettings {
  id: number;
  locationId: number;
  version: number;
  updatedAt: string | Date | null;
}

export interface TailBiteRuleVersion extends TailBiteRuleSettings {
  version: number;
  locationId: number;
  changedAt: string | Date | null;
}

export interface TailBiteRuleInput extends TailBiteRuleSettings {
  locationId: number;
}

/** perSummingLocation: location id (as a JSON object key) -> outbreaks that would have opened there. */
export interface TailBiteRulePreview {
  outbreaksWouldOpen: number;
  perSummingLocation: {[locationId: string]: number};
}

export interface TailBiteOccupancy {
  locationId: number;
  pigCount: number;
  source: TailBiteOccupancySource;
  validFrom: string | Date;
}

export interface TailBiteOutbreakSummary {
  id: number;
  locationId: number;
  openedAt: string | Date;
  assessed: boolean;
  openActions: number;
  closed: boolean;
  bittenPigs: number;
  severePigs: number;
}

export interface TailBiteFactorAnswers {
  water: boolean;
  feed: boolean;
  activityMaterial: boolean;
  climate: boolean;
  health: boolean;
  management: boolean;
}

export interface TailBiteOutbreakAction {
  id: number;
  factor: TailBiteFactor;
  description: string;
  responsibleSiteId: number;
  followUpDate: string | Date;
  doneAt: string | Date | null;
  withdrawnAt: string | Date | null;
}

export interface TailBiteOutbreakDetail {
  summary: TailBiteOutbreakSummary;
  ruleId: number;
  ruleVersion: number;
  registrationIds: number[];
  answers: TailBiteFactorAnswers | null;
  actions: TailBiteOutbreakAction[];
}

export interface TailBiteOutbreakRegistrationRow {
  registrationId: number;
  rowId: number;
  locationId: number;
  effectiveAt: string | Date;
  minor: number;
  severe: number;
  actionTypeIds: number[];
  siteId: number;
  siteName: string;
  cancelled: boolean;
  cancelReason: string | null;
  photoCount: number;
}

/** An action type a registration names; deleted types included, since a registration keeps its links to them. */
export interface TailBiteActionTypeName {
  id: number;
  name: string;
}

export interface TailBiteOutbreakRegistrations {
  propertyId: number;
  rows: TailBiteOutbreakRegistrationRow[];
  /** Names for every action type the rows name (the tree lists live types only). */
  actionTypes: TailBiteActionTypeName[];
}

/** followUpDate is a date-only "yyyy-MM-dd" string, so the picked day never shifts across a UTC offset. */
export interface TailBiteNewAction {
  factor: TailBiteFactor;
  description: string;
  responsibleSiteId: number;
  followUpDate: string;
}

export interface TailBiteSaveAssessmentRequest {
  answers: TailBiteFactorAnswers;
  newActions: TailBiteNewAction[];
}

/** Display order, answer key and translation keys of every factor. */
export const TAIL_BITE_FACTORS: ReadonlyArray<{
  factor: TailBiteFactor;
  answerKey: keyof TailBiteFactorAnswers;
  label: string;
  question: string;
}> = [
  {factor: TailBiteFactor.Water, answerKey: 'water', label: 'Water supply',
    question: 'Are there problems with the water supply or the drinkers?'},
  {factor: TailBiteFactor.Feed, answerKey: 'feed', label: 'Feed and feeding',
    question: 'Has the feed changed, or are there problems with the feeding?'},
  {factor: TailBiteFactor.ActivityMaterial, answerKey: 'activityMaterial', label: 'Rooting and activity material',
    question: 'Is rooting or activity material missing?'},
  {factor: TailBiteFactor.Climate, answerKey: 'climate', label: 'Barn climate',
    question: 'Is there draught, too high a temperature or poor air?'},
  {factor: TailBiteFactor.Health, answerKey: 'health', label: 'Herd health',
    question: 'Is there disease or another health problem in the group?'},
  {factor: TailBiteFactor.Management, answerKey: 'management', label: 'Farm management',
    question: 'Have routines, stocking or staffing changed?'},
];
