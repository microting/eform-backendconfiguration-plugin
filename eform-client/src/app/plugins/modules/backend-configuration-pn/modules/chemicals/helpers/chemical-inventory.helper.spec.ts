import {ChemicalInventoryModel, ChemicalPlacementModel, ChemicalRegisterEntryModel, ChemicalStockUnit} from '../../../models';
import {
  buildInventoryRows,
  chemicalStatusLabelKey,
  filterInventoryRows,
  isStockEnabled,
  sdsFileNameFor,
  summarizeActions,
} from './chemical-inventory.helper';

const TODAY = '2026-09-29';

function entry(id: number, name: string, partial: Partial<ChemicalRegisterEntryModel> = {}): ChemicalRegisterEntryModel {
  return {
    chemicalId: id, name, registrationNo: `${id}-1`, status: 5, statusText: 'Produkt godkendt',
    salesDeadline: null, useAndPossessionDeadline: null, authorisationDate: null, authorisationExpirationDate: null,
    authorisationTerminationDate: null, hazardPictograms: ['GHS07'], signalWord: 2, signalWordText: 'Advarsel',
    hazardStatements: [], activeSubstances: [], authorisationHolder: 'Syngenta',
    products: [{productId: id * 10, name: `${name} 1 L`, barcode: '', sdsFileName: `sds${id}`, sdsChecksum: `sds${id}`}],
    updatedAt: '2026-09-01T00:00:00', ...partial,
  };
}

function placement(id: number, locationId: number, chemicalId: number, partial: Partial<ChemicalPlacementModel> = {}): ChemicalPlacementModel {
  return {
    id, locationId, propertyId: 1, chemicalId, productId: null, placementNote: `Hylde ${id}`, registeredByUserId: 5,
    registeredByName: 'Jens Hansen', registeredAt: '2026-08-12T08:00:00', removedByUserId: null, removedByName: null,
    removedAt: null, removalReason: null, removalNote: null, movedFromPlacementId: null, balance: 0, unit: null,
    updatedAt: '2026-08-12T08:00:00', writeOffEntryId: null, ...partial,
  };
}

const inventory: ChemicalInventoryModel = {
  properties: [{propertyId: 1, name: 'Hovedgården', permissions: null, stockEnabled: true, callerWorkerId: null}],
  locations: [
    {id: 10, propertyId: 1, name: 'Værksted', description: '', photoFileName: null, sortOrder: 2, archived: false, updatedAt: ''},
    {id: 11, propertyId: 1, name: 'Lade', description: '', photoFileName: null, sortOrder: 1, archived: false, updatedAt: ''},
  ],
  placements: [
    placement(1, 10, 100),
    placement(2, 11, 200, {balance: 3.5, unit: ChemicalStockUnit.Kg}),
    placement(3, 11, 300),
    placement(4, 11, 100, {removedAt: '2026-09-01T00:00:00'}),
    placement(5, 10, 999),
  ],
  stockEntries: [],
  registerEntries: [
    entry(100, 'Virkon S'),
    entry(200, 'Karate 2,5 WG', {status: 6, statusText: 'Produkt afmeldt', useAndPossessionDeadline: '2027-03-01T00:00:00'}),
    entry(300, 'Stalddesinfektion Plus', {status: 7, statusText: 'Produkt udløbet', useAndPossessionDeadline: '2026-07-01T00:00:00'}),
  ],
  syncToken: '',
  full: true,
};

describe('chemical inventory helper', () => {
  const rows = buildInventoryRows(inventory, TODAY);

  it('lists open placements only, in location order then product name', () => {
    expect(rows.map((r) => r.placement.id)).toEqual([2, 3, 5, 1]);
  });

  it('keeps a row whose chemical is missing from the register', () => {
    const missing = rows.find((r) => r.placement.id === 5);

    expect(missing.productName).toBe('#999');
    expect(missing.severity).toBe('none');
    expect(missing.requiresAction).toBe(false);
  });

  it('applies Plan D severity to each row', () => {
    expect(rows.find((r) => r.placement.id === 2).severity).toBe('overdue'); // status 6
    expect(rows.find((r) => r.placement.id === 3).severity).toBe('overdue');
    expect(rows.find((r) => r.placement.id === 1).severity).toBe('none');
  });

  it('filters by search over product, reg.nr, placement note and location', () => {
    const f = {search: '', locationId: null, status: 'all' as const, requiresActionOnly: false};

    expect(filterInventoryRows(rows, {...f, search: 'karate'}).map((r) => r.placement.id)).toEqual([2]);
    expect(filterInventoryRows(rows, {...f, search: '300-1'}).map((r) => r.placement.id)).toEqual([3]);
    expect(filterInventoryRows(rows, {...f, search: 'hylde 1'}).map((r) => r.placement.id)).toEqual([1]);
    expect(filterInventoryRows(rows, {...f, search: 'værk'}).map((r) => r.placement.id)).toEqual([5, 1]);
  });

  it('filters by location, status and "Kræver handling"', () => {
    const f = {search: '', locationId: null, status: 'all' as const, requiresActionOnly: false};

    expect(filterInventoryRows(rows, {...f, locationId: 10}).map((r) => r.placement.id)).toEqual([5, 1]);
    expect(filterInventoryRows(rows, {...f, status: 'deregistered'}).map((r) => r.placement.id)).toEqual([2]);
    expect(filterInventoryRows(rows, {...f, status: 'expired'}).map((r) => r.placement.id)).toEqual([3]);
    expect(filterInventoryRows(rows, {...f, status: 'approved'}).map((r) => r.placement.id)).toEqual([1]);
    expect(filterInventoryRows(rows, {...f, requiresActionOnly: true}).map((r) => r.placement.id)).toEqual([2, 3]);
  });

  it('summarizes the banner', () => {
    expect(summarizeActions(rows)).toEqual({total: 2, overdue: 2, warning: 0});
  });

  it('labels BMD status codes 5/6/7 and leaves others to the register text', () => {
    expect(chemicalStatusLabelKey(5)).toBe('Chemicals: Status approved');
    expect(chemicalStatusLabelKey(6)).toBe('Chemicals: Status deregistered');
    expect(chemicalStatusLabelKey(7)).toBe('Chemicals: Status expired');
    expect(chemicalStatusLabelKey(1)).toBeNull();
  });

  it('knows whether stock is enabled and which SDS a row opens', () => {
    expect(isStockEnabled(inventory, 1)).toBe(true);
    expect(isStockEnabled(null, 1)).toBe(false);
    expect(sdsFileNameFor(rows.find((r) => r.placement.id === 2))).toBe('sds200');
    expect(sdsFileNameFor(rows.find((r) => r.placement.id === 5))).toBeNull();
  });
});
