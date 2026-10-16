import {ChemicalInventoryModel, ChemicalLocationModel} from '../../../models';
import {requiresAction} from './chemical-alert-rules.helper';

/** One W3 row: a location with its open placements. */
export interface ChemicalLocationRow {
  location: ChemicalLocationModel;
  /** Open placements - archiving is blocked while this is > 0 (spec §5.1, W3). */
  openCount: number;
  /** Open placements that "kræver handling" (spec §9). */
  actionCount: number;
}

/** Mockup W3: "JPG eller PNG, højst 10 MB" (the server also takes HEIC and up to 20 MB, for the app). */
export const LOCATION_PHOTO_TYPES = ['image/jpeg', 'image/png'];
export const LOCATION_PHOTO_MAX_BYTES = 10 * 1024 * 1024;

/** Active locations by sort order (ties by id), then the archived ones, with their open and action counts. */
export function buildLocationRows(inventory: ChemicalInventoryModel | null, today: string): ChemicalLocationRow[] {
  if (!inventory) {
    return [];
  }
  const entries = new Map(inventory.registerEntries.map((e) => [e.chemicalId, e]));
  const open = inventory.placements.filter((p) => !p.removedAt);
  return inventory.locations
    .map((location) => {
      const here = open.filter((p) => p.locationId === location.id);
      return {
        location,
        openCount: here.length,
        actionCount: here.filter((p) => requiresAction(entries.get(p.chemicalId) ?? null, today)).length,
      };
    })
    .sort((a, b) => Number(a.location.archived) - Number(b.location.archived)
      || a.location.sortOrder - b.location.sortOrder || a.location.id - b.location.id);
}

/**
 * The PUT …/locations/order body after dragging row previousIndex to currentIndex. Only active rows
 * count (the server wants every active location once, and no archived one); a drop past them lands last.
 */
export function reorderedLocationIds(rows: ChemicalLocationRow[], previousIndex: number, currentIndex: number): number[] {
  const ids = rows.filter((r) => !r.location.archived).map((r) => r.location.id);
  const [moved] = ids.splice(previousIndex, 1);
  ids.splice(currentIndex, 0, moved);
  return ids;
}

export function validateLocationPhoto(file: File): 'type' | 'size' | null {
  if (!LOCATION_PHOTO_TYPES.includes(file.type)) {
    return 'type';
  }
  return file.size > LOCATION_PHOTO_MAX_BYTES ? 'size' : null;
}
