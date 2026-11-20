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
  /** Photo uuids of the registration, on its first row only; empty on its other rows and on a cancelled registration. */
  photos: string[];
}

/** One photo in the page's gallery and in the photo viewer. */
export interface TailBiteGalleryItem {
  uuid: string;
  registrationId: number;
  effectiveAt: Date | null;
  /** The registration's pens, joined ", ": a registration covering several pens is listed once. */
  pens: string;
  siteName: string;
}

export interface TailBiteOutbreakView {
  title: string;
  status: TailBiteOutbreakStatus;
  openedAt: Date | null;
  /** The rule version that opened the outbreak, if its history is readable. */
  rule: TailBiteRuleVersion | null;
  ruleLocation: string;
  rows: TailBiteOutbreakRowView[];
  /** Every photo of the outbreak, newest registration first (a registration's own photos oldest first). */
  gallery: TailBiteGalleryItem[];
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
  const photosOf = new Map<number, string[]>();
  for (const p of registrations.photos) {
    photosOf.set(p.registrationId, [...(photosOf.get(p.registrationId) ?? []), p.photoUuid]);
  }
  const rows = registrations.rows.map((r, index): TailBiteOutbreakRowView => {
    const firstOfRegistration = registrations.rows.findIndex((x) => x.registrationId === r.registrationId) === index;
    return {
      registrationId: r.registrationId,
      firstOfRegistration,
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
      photos: firstOfRegistration && !r.cancelled ? photosOf.get(r.registrationId) ?? [] : [],
    };
  });
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
    gallery: buildGallery(rows),
    bittenPigs,
    locationCount: new Set(counted.map((r) => r.locationId)).size,
    pigs,
    pigsFrom: behind[0] ?? null,
    ratePer100: pigs ? Math.round((bittenPigs / pigs) * 1000) / 10 : null,
  };
}

/**
 * The gallery: one item per photo, newest registration first. The sort is stable, so a registration's photos keep the
 * server's oldest-first order. A registration covering several pens is listed once, its pens joined ", ".
 */
function buildGallery(rows: TailBiteOutbreakRowView[]): TailBiteGalleryItem[] {
  // One row per pen of a registration, so two pens that share a name (Sti 1 in two sections) both stay.
  const pens = new Map<number, string[]>();
  for (const r of rows) {
    pens.set(r.registrationId, [...(pens.get(r.registrationId) ?? []), r.location]);
  }
  return rows
    .filter((r) => r.photos.length > 0)
    .sort((a, b) => (b.effectiveAt?.getTime() ?? 0) - (a.effectiveAt?.getTime() ?? 0))
    .flatMap((r) => r.photos.map((uuid) => ({
      uuid, registrationId: r.registrationId, effectiveAt: r.effectiveAt, pens: pens.get(r.registrationId)!.join(', '),
      siteName: r.siteName,
    })));
}
