import {entry, inventoryOf, location, placement} from './chemical-inventory.fixtures';
import {
  LOCATION_PHOTO_MAX_BYTES,
  buildLocationRows,
  reorderedLocationIds,
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

  it('computes the new order of ACTIVE locations after a drag', () => {
    const rows = buildLocationRows(inventory, '2026-09-29');

    expect(reorderedLocationIds(rows, 2, 0)).toEqual([1, 2, 4]);
    expect(reorderedLocationIds(rows, 0, 0)).toEqual([2, 4, 1]);
  });

  it('never puts an archived location in the order body, even when dropped past the active rows', () => {
    const rows = buildLocationRows(inventory, '2026-09-29');

    expect(reorderedLocationIds(rows, 0, 3)).toEqual([4, 1, 2]);
  });

  it('accepts JPG and PNG up to 10 MB', () => {
    expect(validateLocationPhoto(photo('image/jpeg'))).toBeNull();
    expect(validateLocationPhoto(photo('image/png'))).toBeNull();
    expect(validateLocationPhoto(photo('image/png', LOCATION_PHOTO_MAX_BYTES))).toBeNull();
    expect(validateLocationPhoto(photo('image/gif'))).toBe('type');
    expect(validateLocationPhoto(photo('image/jpeg', 10 * 1024 * 1024 + 1))).toBe('size');
  });
});
