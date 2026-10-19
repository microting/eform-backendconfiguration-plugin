import {describe, it, expect} from 'vitest';
import {ChemicalStockEntryKind, ChemicalStockEntryOrigin, ChemicalStockUnit} from '../../../models';
import {entry, inventoryOf, location, placement, stockEntry} from './chemical-inventory.fixtures';
import {
  CHEMICAL_MAX_AMOUNT,
  buildInventoryRows,
  chemicalStatusLabelKey,
  isValidQuantity,
  filterInventoryRows,
  isStockEnabled,
  placementStockHistory,
  placementsOfChemical,
  sdsFileNameFor,
  summarizeActions,
} from './chemical-inventory.helper';

const TODAY = '2026-09-29';

const inventory = inventoryOf({
  stockEnabled: true,
  locations: [location(10, 'Værksted', {sortOrder: 2}), location(11, 'Lade', {sortOrder: 1})],
  placements: [
    placement(1, 10, 100),
    placement(2, 11, 200, {balance: 3.5, unit: ChemicalStockUnit.Kg}),
    placement(3, 11, 300),
    placement(4, 11, 100, {removedAt: '2026-09-01T00:00:00'}),
    placement(5, 10, 999),
  ],
  registerEntries: [
    entry(100, 'Virkon S'),
    entry(200, 'Karate 2,5 WG', {status: 6, statusText: 'Produkt afmeldt', useAndPossessionDeadline: '2027-03-01T00:00:00'}),
    entry(300, 'Stalddesinfektion Plus', {status: 7, statusText: 'Produkt udløbet', useAndPossessionDeadline: '2026-07-01T00:00:00'}),
  ],
});

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

  it('gives the template ready-made labels instead of BMD and enum integers (m-8)', () => {
    const karate = rows.find((r) => r.placement.id === 2);
    const virkon = rows.find((r) => r.placement.id === 1);

    expect([karate.statusBad, rows.find((r) => r.placement.id === 3).statusBad, virkon.statusBad]).toEqual([true, true, false]);
    expect([karate.stockUnitLabel, virkon.stockUnitLabel]).toEqual(['kg', '']);
    expect(karate.deadlineLabelKey).toBe('Chemicals: Use deadline');
    expect(virkon.deadlineLabelKey).toBeNull();
    expect(buildInventoryRows({...inventory, registerEntries: [entry(100, 'Virkon S', {salesDeadline: '2027-01-01T00:00:00'})]}, TODAY)
      .find((r) => r.placement.id === 1).deadlineLabelKey).toBe('Chemicals: Sales deadline');
  });

  it('knows whether stock is enabled and which SDS a row opens', () => {
    expect(isStockEnabled(inventory, 1)).toBe(true);
    expect(isStockEnabled(null, 1)).toBe(false);
    expect(sdsFileNameFor(rows.find((r) => r.placement.id === 2))).toBe('sds200');
    expect(sdsFileNameFor(rows.find((r) => r.placement.id === 5))).toBeNull();
  });
});

describe('placement stock history and sibling placements', () => {
  const withEntries = {
    ...inventory,
    stockEntries: [
      stockEntry(1, 2, ChemicalStockEntryKind.Received, 5, '2026-08-12T08:00:00', 5),
      stockEntry(3, 2, ChemicalStockEntryKind.Consumed, -0.5, '2026-09-24T08:00:00', 3.5),
      stockEntry(2, 2, ChemicalStockEntryKind.Consumed, -1, '2026-09-02T08:00:00', 4),
      stockEntry(4, 3, ChemicalStockEntryKind.Received, 9, '2026-09-01T08:00:00', 9),
    ],
  };

  it('lists the placement\'s movements newest first with the running balance', () => {
    const history = placementStockHistory(withEntries, 2);

    expect(history.map((h) => [h.entry.id, h.balanceAfter])).toEqual([[3, 3.5], [2, 4], [1, 5]]);
  });

  it('shows the server\'s balance after each entry and never recomputes it (E-06)', () => {
    // The inventory carries only the last 12 months: older entries are missing, so a client sum would be wrong.
    const truncated = {
      ...inventory,
      stockEntries: [
        stockEntry(11, 2, ChemicalStockEntryKind.Consumed, -1, '2026-09-02T08:00:00', 11),
        stockEntry(12, 2, ChemicalStockEntryKind.Consumed, -2, '2026-09-03T08:00:00', 9),
      ],
    };

    expect(placementStockHistory(truncated, 2).map((h) => h.balanceAfter)).toEqual([9, 11]);
  });

  it('orders entries booked at the same moment by id, newest first', () => {
    const sameTime = {
      ...inventory,
      stockEntries: [
        stockEntry(21, 2, ChemicalStockEntryKind.Received, 2, '2026-09-02T08:00:00', 2),
        stockEntry(22, 2, ChemicalStockEntryKind.Consumed, -2, '2026-09-02T08:00:00', 0),
      ],
    };

    expect(placementStockHistory(sameTime, 2).map((h) => h.entry.id)).toEqual([22, 21]);
  });

  it('labels the kind of each entry and where a move or a removal wrote it (E-06)', () => {
    const labelled = {
      ...inventory,
      stockEntries: [
        stockEntry(31, 2, ChemicalStockEntryKind.Received, 5, '2026-09-01T08:00:00', 5),
        stockEntry(32, 2, ChemicalStockEntryKind.MovedOut, -2, '2026-09-02T08:00:00', 3,
          {origin: ChemicalStockEntryOrigin.Move, counterpartPlacementId: 9}),
        stockEntry(33, 2, ChemicalStockEntryKind.Consumed, -3, '2026-09-03T08:00:00', 0,
          {origin: ChemicalStockEntryOrigin.RemovalWriteOff}),
      ],
    };

    expect(placementStockHistory(labelled, 2).map((h) => [h.kindLabelKey, h.originLabelKey])).toEqual([
      ['Chemicals: Stock kind 2', 'Chemicals: Stock origin write-off'],
      ['Chemicals: Stock kind 4', 'Chemicals: Stock origin move'],
      ['Chemicals: Stock kind 1', null],
    ]);
  });

  it('is empty without an inventory', () => {
    expect(placementStockHistory(null, 2)).toEqual([]);
    expect(placementsOfChemical(null, 100)).toEqual([]);
  });

  it('lists every open placement of the same chemical', () => {
    expect(placementsOfChemical(inventory, 100).map((p) => p.id)).toEqual([1]);
  });
});

describe('isValidQuantity (the server\'s ChemicalQuantity rules)', () => {
  it('accepts up to three decimals and at most one million', () => {
    expect(CHEMICAL_MAX_AMOUNT).toBe(1_000_000);
    for (const value of [0.001, 0.1, 2.5, 1.125, 999_999.999, 1_000_000]) {
      expect(isValidQuantity(value, false)).toBe(true);
    }
  });

  it('refuses more than three decimals, more than one million, negatives and non-numbers', () => {
    for (const value of [0.0005, 1.0005, 1_000_000.001, 2_000_000, -1, NaN, Infinity, null, undefined]) {
      expect(isValidQuantity(value as number, true)).toBe(false);
    }
  });

  it('accepts zero only where the server does (a counted balance)', () => {
    expect(isValidQuantity(0, true)).toBe(true);
    expect(isValidQuantity(0, false)).toBe(false);
  });
});
