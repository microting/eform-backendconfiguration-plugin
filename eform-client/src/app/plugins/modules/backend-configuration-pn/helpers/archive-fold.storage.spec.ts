import {describe, it, expect, beforeEach, afterEach, vi} from 'vitest';
import {ARCHIVE_FOLD_STORAGE_KEY, readCollapsedFolderIds, writeCollapsedFolderIds} from './archive-fold.storage';

describe('archive fold storage', () => {
  beforeEach(() => window.localStorage.clear());
  afterEach(() => {
    vi.restoreAllMocks();
    window.localStorage.clear();
  });

  it('round-trips the folded folder ids without duplicates', () => {
    writeCollapsedFolderIds([3, 6, 3]);
    expect(JSON.parse(window.localStorage.getItem(ARCHIVE_FOLD_STORAGE_KEY)!)).toEqual([3, 6]);
    expect(readCollapsedFolderIds()).toEqual([3, 6]);
  });

  it('ignores garbage', () => {
    window.localStorage.setItem(ARCHIVE_FOLD_STORAGE_KEY, '{"x":1}');
    expect(readCollapsedFolderIds()).toEqual([]);
    window.localStorage.setItem(ARCHIVE_FOLD_STORAGE_KEY, '[1,"2",3.5,4]');
    expect(readCollapsedFolderIds()).toEqual([1, 4]);
  });

  it('survives blocked storage (fold state is only a convenience)', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('blocked'); });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('blocked'); });
    expect(readCollapsedFolderIds()).toEqual([]);
    expect(() => writeCollapsedFolderIds([1])).not.toThrow();
  });
});
