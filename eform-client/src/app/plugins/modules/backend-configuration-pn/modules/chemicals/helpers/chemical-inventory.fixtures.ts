import {
  ChemicalInventoryModel,
  ChemicalLocationModel,
  ChemicalPlacementModel,
  ChemicalRegisterEntryModel,
} from '../../../models';

/**
 * Test data builders for the W1 specs (helper, page, page template). Not exported from the
 * helpers barrel: production code never imports them. A model change touches only this file.
 */

export function entry(id: number, name: string, partial: Partial<ChemicalRegisterEntryModel> = {}): ChemicalRegisterEntryModel {
  return {
    chemicalId: id, name, registrationNo: `${id}-1`, status: 5, statusText: 'Produkt godkendt',
    salesDeadline: null, useAndPossessionDeadline: null, authorisationDate: null, authorisationExpirationDate: null,
    authorisationTerminationDate: null, hazardPictograms: ['GHS07'], signalWord: 2, signalWordText: 'Advarsel',
    hazardStatements: [], activeSubstances: [], authorisationHolder: 'Syngenta',
    products: [{productId: id * 10, name: `${name} 1 L`, barcode: '', sdsFileName: `sds${id}`, sdsChecksum: `sds${id}`}],
    updatedAt: '2026-09-01T00:00:00', ...partial,
  };
}

export function placement(id: number, locationId: number, chemicalId: number, partial: Partial<ChemicalPlacementModel> = {}): ChemicalPlacementModel {
  return {
    id, locationId, propertyId: 1, chemicalId, productId: null, placementNote: `Hylde ${id}`, registeredByUserId: 5,
    registeredByName: 'Jens Hansen', registeredAt: '2026-08-12T08:00:00', removedByUserId: null, removedByName: null,
    removedAt: null, removalReason: null, removalNote: null, movedFromPlacementId: null, balance: 0, unit: null,
    updatedAt: '2026-08-12T08:00:00', writeOffEntryId: null, ...partial,
  };
}

export function location(id: number, name: string, partial: Partial<ChemicalLocationModel> = {}): ChemicalLocationModel {
  return {id, propertyId: 1, name, description: '', photoFileName: null, sortOrder: id, archived: false, updatedAt: '', ...partial};
}

export function inventoryOf(parts: {
  propertyId?: number;
  stockEnabled?: boolean;
  locations?: ChemicalLocationModel[];
  placements?: ChemicalPlacementModel[];
  registerEntries?: ChemicalRegisterEntryModel[];
} = {}): ChemicalInventoryModel {
  return {
    properties: [{propertyId: parts.propertyId ?? 1, name: 'Hovedgården', permissions: null, stockEnabled: !!parts.stockEnabled, callerWorkerId: null}],
    locations: parts.locations ?? [],
    placements: parts.placements ?? [],
    stockEntries: [],
    registerEntries: parts.registerEntries ?? [],
    syncToken: '',
    full: true,
  };
}
