import {describe, it, expect} from 'vitest';
import {entry, inventoryOf, location, placement} from './chemical-inventory.fixtures';
import {
  LOCATION_PHOTO_MAX_BYTES,
  activeLocationIds,
  buildLocationRows,
  validateLocationPhoto,
} from './chemical-locations.helper';

const inventory = inventoryOf({
  locations: [
    location(1, 'L1', {sortOrder: 3}),
    location(2, 'L2', {sortOrder: 1}),
    location(3, 'L3', {sortOrder: 2, archived: true}),
    location(4, 'L4', {sortOrder: 2}),
  ],
  placements: [
    placement(10, 2, 100),
    placement(11, 2, 200),
    placement(12, 1, 100),
    placement(13, 1, 100, {removedAt: '2026-09-01T00:00:00'}),
  ],
  registerEntries: [entry(100, 'Godkendt', {status: 5}), entry(200, 'Afmeldt', {status: 6})],
});

function photo(type: string, size?: number): File {
  const file = new File(['x'], 'photo', {type});
  if (size !== undefined) {
    Object.defineProperty(file, 'size', {value: size});
  }
  return file;
}

describe('chemical locations helper', () => {
  it('orders active locations by sort order, archived last, with open and action counts', () => {
    const rows = buildLocationRows(inventory, '2026-09-29');

    expect(rows.map((r) => [r.location.id, r.openCount, r.actionCount])).toEqual([[2, 2, 1], [4, 0, 0], [1, 1, 0], [3, 0, 0]]);
  });

  it('breaks a sort-order tie by id, so the list never jumps between loads', () => {
    const rows = buildLocationRows(inventoryOf({locations: [location(9, 'B', {sortOrder: 1}), location(5, 'A', {sortOrder: 1})]}), '2026-09-29');

    expect(rows.map((r) => r.location.id)).toEqual([5, 9]);
  });

  it('gives no rows before the inventory has loaded', () => {
    expect(buildLocationRows(null, '2026-09-29')).toEqual([]);
  });

  it('gives the order body: the ACTIVE locations in row order, never an archived one', () => {
    const rows = buildLocationRows(inventory, '2026-09-29');

    expect(activeLocationIds(rows)).toEqual([2, 4, 1]);
    expect(activeLocationIds([rows[3], rows[2], rows[0]])).toEqual([1, 2]);
  });

  it('counts "kræver handling" exactly as W1 does (one rule for both screens)', () => {
    // A passed use deadline on an approved product: overdue in W1, so one action here.
    const withDeadline = inventoryOf({
      locations: [location(1, 'L1')],
      placements: [placement(10, 1, 300)],
      registerEntries: [entry(300, 'Udløbet', {status: 5, useAndPossessionDeadline: '2026-01-01'})],
    });

    expect(buildLocationRows(withDeadline, '2026-09-29').map((r) => r.actionCount)).toEqual([1]);
  });

  it('accepts JPG and PNG up to 10 MB', () => {
    expect(validateLocationPhoto(photo('image/jpeg'))).toBeNull();
    expect(validateLocationPhoto(photo('image/png'))).toBeNull();
    expect(validateLocationPhoto(photo('image/png', LOCATION_PHOTO_MAX_BYTES))).toBeNull();
    expect(validateLocationPhoto(photo('image/gif'))).toBe('type');
    expect(validateLocationPhoto(photo('image/jpeg', 10 * 1024 * 1024 + 1))).toBe('size');
  });
});
