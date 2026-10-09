import {
  TailBiteLocationTree,
  TailBiteOccupancy,
  TailBiteOutbreakDetail,
  TailBiteOutbreakRegistrations,
  TailBiteRuleVersion,
} from '../../../../models';
import {parseServerUtc} from '../../shared/tail-bite-dates';
import {TailBiteOutbreakStatus, outbreakStatus} from '../../shared/tail-bite-outbreak-status';
import {buildTreeRows, countingOccupancy, locationTitle, nodeMap} from '../../shared/tail-bite-tree';

export interface TailBiteOutbreakRowView {
  registrationId: number;
  /** A registration touching several locations has several rows; only the first carries the per-registration controls. */
  firstOfRegistration: boolean;
  rowId: number;
  location: string;
  effectiveAt: Date | null;
  minor: number;
  severe: number;
  actions: string;
  siteName: string;
  cancelled: boolean;
  cancelReason: string | null;
  photoCount: number;
}

export interface TailBiteOutbreakView {
  title: string;
  status: TailBiteOutbreakStatus;
  openedAt: Date | null;
  /** The rule version that opened the outbreak, if its history is readable. */
  rule: TailBiteRuleVersion | null;
  ruleLocation: string;
  rows: TailBiteOutbreakRowView[];
  /** Minor + severe, as the server counts them (cancelled registrations excluded). */
  bittenPigs: number;
  locationCount: number;
  /** Pig count of the summing location (own, else the sum below it) and the newest "valid from" of the counts it is made of. */
  pigs: number | null;
  pigsFrom: Date | null;
  ratePer100: number | null;
}

/** Everything the outbreak page shows, from the five calls it makes. */
export function buildOutbreakView(
  detail: TailBiteOutbreakDetail,
  registrations: TailBiteOutbreakRegistrations,
  tree: TailBiteLocationTree,
  occupancy: TailBiteOccupancy[],
  history: TailBiteRuleVersion[],
  deletedLocationLabel: string,
): TailBiteOutbreakView {
  const byId = nodeMap(tree.locations);
  const name = (id: number) => (byId.has(id) ? locationTitle(id, byId) : deletedLocationLabel);
  // The registrations' own list names deleted types too; the tree's live list covers the rest.
  const actionName = new Map([...tree.actionTypes, ...registrations.actionTypes].map((a) => [a.id, a.name]));
  const rows = registrations.rows.map((r, index) => ({
    registrationId: r.registrationId,
    firstOfRegistration: registrations.rows.findIndex((x) => x.registrationId === r.registrationId) === index,
    rowId: r.rowId,
    location: byId.get(r.locationId)?.name ?? deletedLocationLabel,
    effectiveAt: parseServerUtc(r.effectiveAt),
    minor: r.minor,
    severe: r.severe,
    actions: r.actionTypeIds.map((id) => actionName.get(id) ?? `#${id}`).join(', ') || '–',
    siteName: r.siteName,
    cancelled: r.cancelled,
    cancelReason: r.cancelReason,
    photoCount: r.photoCount,
  }));
  const counted = registrations.rows.filter((r) => !r.cancelled);
  const bittenPigs = detail.summary.bittenPigs;
  const summing = detail.summary.locationId;
  const pigs = buildTreeRows(tree, [], occupancy).find((r) => r.node.id === summing)?.pigs ?? null;
  const behind = countingOccupancy(tree, occupancy, summing)
    .map((o) => parseServerUtc(o.validFrom))
    .filter((d): d is Date => d !== null)
    .sort((a, b) => b.getTime() - a.getTime());
  const rule = history.find((v) => v.version === detail.ruleVersion) ?? null;
  return {
    title: name(summing),
    status: outbreakStatus(detail.summary),
    openedAt: parseServerUtc(detail.summary.openedAt),
    rule,
    ruleLocation: rule ? name(rule.locationId) : '',
    rows,
    bittenPigs,
    locationCount: new Set(counted.map((r) => r.locationId)).size,
    pigs,
    pigsFrom: behind[0] ?? null,
    ratePer100: pigs ? Math.round((bittenPigs / pigs) * 1000) / 10 : null,
  };
}
