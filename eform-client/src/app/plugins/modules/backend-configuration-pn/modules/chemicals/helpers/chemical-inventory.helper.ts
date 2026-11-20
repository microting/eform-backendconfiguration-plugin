import {
  CHEMICAL_STATUS_APPROVED,
  CHEMICAL_STATUS_DEREGISTERED,
  CHEMICAL_STATUS_EXPIRED,
  ChemicalDeadlineKind,
  ChemicalInventoryModel,
  ChemicalLocationModel,
  ChemicalPlacementModel,
  ChemicalRegisterEntryModel,
  ChemicalStockEntryKind,
  ChemicalStockEntryModel,
  ChemicalStockEntryOrigin,
  ChemicalStockUnit,
} from '../../../models';
import {ChemicalInventoryFiltersModel} from '../../../state';
import {
  ChemicalDeadlineDisplay,
  ChemicalSeverity,
  actionSeverity,
  governingDeadline,
  isActionStatus,
  requiresAction,
} from './chemical-alert-rules.helper';

/** One W1 row: an OPEN placement joined with its location and register entry. */
export interface ChemicalInventoryRow {
  placement: ChemicalPlacementModel;
  entry: ChemicalRegisterEntryModel | null;
  location: ChemicalLocationModel | null;
  productName: string;
  registrationNo: string;
  /** The Ejendom column (W1 under the picker's "All", and for one property). */
  propertyName: string;
  locationName: string;
  statusLabelKey: string | null;
  statusText: string;
  /** Afmeldt or Udløbet: the status chip is drawn as a warning. */
  statusBad: boolean;
  severity: ChemicalSeverity;
  requiresAction: boolean;
  deadline: ChemicalDeadlineDisplay | null;
  /** 'Chemicals: Sales deadline' / 'Chemicals: Use deadline', null without a deadline. */
  deadlineLabelKey: string | null;
  /** 'kg' / 'L' for the Lager cell, '' when the placement has no stock unit. */
  stockUnitLabel: string;
  pictograms: string[];
}

const STOCK_UNIT_LABELS: Record<ChemicalStockUnit, string> = {[ChemicalStockUnit.L]: 'L', [ChemicalStockUnit.Kg]: 'kg'};

/** 'kg' / 'L', '' without a unit. */
export function stockUnitLabel(unit: ChemicalStockUnit | null): string {
  return unit ? STOCK_UNIT_LABELS[unit] : '';
}

const DEADLINE_LABEL_KEYS: Record<ChemicalDeadlineKind, string> = {
  [ChemicalDeadlineKind.Sales]: 'Chemicals: Sales deadline',
  [ChemicalDeadlineKind.UseAndPossession]: 'Chemicals: Use deadline',
};

export function chemicalStatusLabelKey(status: number | null): string | null {
  switch (status) {
    case CHEMICAL_STATUS_APPROVED: return 'Chemicals: Status approved';
    case CHEMICAL_STATUS_DEREGISTERED: return 'Chemicals: Status deregistered';
    case CHEMICAL_STATUS_EXPIRED: return 'Chemicals: Status expired';
    default: return null;
  }
}

export function isStockEnabled(inventory: ChemicalInventoryModel | null, propertyId: number | null): boolean {
  return !!inventory?.properties.find((p) => p.propertyId === propertyId)?.stockEnabled;
}

/** The Lager column under "All": shown when any property of the inventory keeps stock. */
export function anyStockEnabled(inventory: ChemicalInventoryModel | null): boolean {
  return !!inventory?.properties.some((p) => p.stockEnabled);
}

/** The locations of one property, archived ones included (a move or a registration filters those itself). */
export function locationsOfProperty(inventory: ChemicalInventoryModel | null, propertyId: number): ChemicalLocationModel[] {
  return (inventory?.locations ?? []).filter((l) => l.propertyId === propertyId);
}

function propertyNames(inventory: ChemicalInventoryModel): Map<number, string> {
  return new Map(inventory.properties.map((p) => [p.propertyId, p.name]));
}

/**
 * The W1 location filter. Under "All" a name alone is ambiguous ("Lade" on two farms), so each option is
 * "<Ejendom> – <Lokation>", by property name then location order; for one property the plain names as sent.
 */
export function inventoryLocationOptions(
  inventory: ChemicalInventoryModel | null, allProperties: boolean,
): Array<{id: number; name: string}> {
  if (!inventory) {
    return [];
  }
  if (!allProperties) {
    return inventory.locations.map((l) => ({id: l.id, name: l.name}));
  }
  const names = propertyNames(inventory);
  const propertyOf = (l: ChemicalLocationModel) => names.get(l.propertyId) ?? '';
  return [...inventory.locations]
    .sort((a, b) => propertyOf(a).localeCompare(propertyOf(b), 'da') || a.propertyId - b.propertyId
      || a.sortOrder - b.sortOrder || a.id - b.id)
    .map((l) => ({id: l.id, name: `${propertyOf(l)} – ${l.name}`}));
}

export function buildInventoryRows(inventory: ChemicalInventoryModel | null, today: string): ChemicalInventoryRow[] {
  if (!inventory) {
    return [];
  }
  const locations = new Map(inventory.locations.map((l) => [l.id, l]));
  const entries = new Map(inventory.registerEntries.map((e) => [e.chemicalId, e]));
  const properties = propertyNames(inventory);
  return inventory.placements
    .filter((p) => !p.removedAt)
    .map((placement) => {
      const entry = entries.get(placement.chemicalId) ?? null;
      const location = locations.get(placement.locationId) ?? null;
      const deadline = governingDeadline(entry, today);
      return {
        placement, entry, location,
        productName: entry?.name ?? `#${placement.chemicalId}`,
        registrationNo: entry?.registrationNo ?? '',
        propertyName: properties.get(placement.propertyId) ?? '',
        locationName: location?.name ?? '',
        statusLabelKey: chemicalStatusLabelKey(entry?.status ?? null),
        statusText: entry?.statusText ?? '',
        statusBad: isActionStatus(entry?.status ?? null),
        severity: actionSeverity(entry, today),
        requiresAction: requiresAction(entry, today),
        deadline,
        deadlineLabelKey: deadline ? DEADLINE_LABEL_KEYS[deadline.kind] : null,
        stockUnitLabel: stockUnitLabel(placement.unit),
        pictograms: entry?.hazardPictograms ?? [],
      };
    })
    .sort((a, b) =>
      a.propertyName.localeCompare(b.propertyName, 'da') || a.placement.propertyId - b.placement.propertyId
      || (a.location?.sortOrder ?? Number.MAX_SAFE_INTEGER) - (b.location?.sortOrder ?? Number.MAX_SAFE_INTEGER)
      || a.productName.localeCompare(b.productName, 'da')
      || a.placement.id - b.placement.id);
}

const STATUS_BY_FILTER: Record<string, number> = {
  approved: CHEMICAL_STATUS_APPROVED,
  deregistered: CHEMICAL_STATUS_DEREGISTERED,
  expired: CHEMICAL_STATUS_EXPIRED,
};

export function filterInventoryRows(rows: ChemicalInventoryRow[], filters: ChemicalInventoryFiltersModel): ChemicalInventoryRow[] {
  const needle = (filters.search ?? '').trim().toLowerCase();
  return rows.filter((row) =>
    (!needle || [row.productName, row.registrationNo, row.placement.placementNote, row.locationName, row.propertyName]
      .some((text) => (text ?? '').toLowerCase().includes(needle)))
    && (filters.locationId === null || row.placement.locationId === filters.locationId)
    && (filters.status === 'all' || row.entry?.status === STATUS_BY_FILTER[filters.status])
    && (!filters.requiresActionOnly || row.requiresAction));
}

/** The "Kræver handling" chip and banner counts. */
export interface ChemicalActionSummary {
  total: number;
  overdue: number;
  warning: number;
}

export function summarizeActions(rows: ChemicalInventoryRow[]): ChemicalActionSummary {
  return {
    total: rows.filter((r) => r.requiresAction).length,
    overdue: rows.filter((r) => r.severity === 'overdue').length,
    warning: rows.filter((r) => r.severity === 'warning').length,
  };
}

/** The SDS a row opens: the placement's own product if it has one, else the chemical's first SDS. */
export function sdsFileNameFor(row: ChemicalInventoryRow): string | null {
  const products = row.entry?.products ?? [];
  const own = products.find((p) => p.productId === row.placement.productId && p.sdsFileName);
  return (own ?? products.find((p) => !!p.sdsFileName))?.sdsFileName ?? null;
}

/** One W2 "Lagerhistorik" line. */
export interface ChemicalStockHistoryRow {
  entry: ChemicalStockEntryModel;
  /** The server's running balance after the entry (E-06): the inventory holds only 12 months, so it is never summed here. */
  balanceAfter: number;
  kindLabelKey: string;
  /** 'Chemicals: Stock origin move' / '… write-off' for entries a move or a removal wrote; null for manual ones. */
  originLabelKey: string | null;
}

const STOCK_KIND_LABEL_KEYS: Record<ChemicalStockEntryKind, string> = {
  [ChemicalStockEntryKind.Received]: 'Chemicals: Stock kind 1',
  [ChemicalStockEntryKind.Consumed]: 'Chemicals: Stock kind 2',
  [ChemicalStockEntryKind.Adjusted]: 'Chemicals: Stock kind 3',
  [ChemicalStockEntryKind.MovedOut]: 'Chemicals: Stock kind 4',
  [ChemicalStockEntryKind.MovedIn]: 'Chemicals: Stock kind 5',
};

const STOCK_ORIGIN_LABEL_KEYS: Partial<Record<ChemicalStockEntryOrigin, string>> = {
  [ChemicalStockEntryOrigin.Move]: 'Chemicals: Stock origin move',
  [ChemicalStockEntryOrigin.RemovalWriteOff]: 'Chemicals: Stock origin write-off',
};

/** W2 "Lagerhistorik": the placement's entries, newest first (same moment: higher id first). */
export function placementStockHistory(inventory: ChemicalInventoryModel | null, placementId: number): ChemicalStockHistoryRow[] {
  return (inventory?.stockEntries ?? [])
    .filter((e) => e.placementId === placementId)
    .sort((a, b) => b.at.localeCompare(a.at) || b.id - a.id)
    .map((entry) => ({
      entry,
      balanceAfter: entry.balanceAfter,
      kindLabelKey: STOCK_KIND_LABEL_KEYS[entry.kind],
      originLabelKey: STOCK_ORIGIN_LABEL_KEYS[entry.origin] ?? null,
    }));
}

/**
 * W2 "Placeringer": every open placement of the chemical on the property. Under "All" the inventory holds
 * every property, so the row's `propertyId` keeps the list to its own property.
 */
export function placementsOfChemical(
  inventory: ChemicalInventoryModel | null, chemicalId: number, propertyId?: number | null,
): ChemicalPlacementModel[] {
  return (inventory?.placements ?? []).filter((p) => p.chemicalId === chemicalId && !p.removedAt
    && (propertyId === undefined || propertyId === null || p.propertyId === propertyId));
}

/** The server's ChemicalQuantity.MaxAmount: one million L/kg; anything larger is a unit mistake. */
export const CHEMICAL_MAX_AMOUNT = 1_000_000;

/**
 * The server's ChemicalQuantity rules (decimal(18,3)): at most three decimals, not above
 * CHEMICAL_MAX_AMOUNT, not negative, and zero only where allowed (a counted balance).
 */
export function isValidQuantity(value: number | null | undefined, allowZero: boolean): boolean {
  if (typeof value !== 'number' || !Number.isFinite(value) || value < 0 || value > CHEMICAL_MAX_AMOUNT) {
    return false;
  }
  if (value === 0) {
    return allowZero;
  }
  const thousandths = value * 1000;
  return Math.abs(thousandths - Math.round(thousandths)) < 1e-6;
}
