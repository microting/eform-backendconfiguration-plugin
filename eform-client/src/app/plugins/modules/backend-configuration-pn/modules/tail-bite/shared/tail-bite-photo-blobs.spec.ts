import {describe, it, expect, vi} from 'vitest';
import {EMPTY, of, throwError} from 'rxjs';
import {TailBitePhotoBlobs} from './tail-bite-photo-blobs';

describe('TailBitePhotoBlobs', () => {
  const blob = new Blob(['x'], {type: 'image/jpeg'});

  it('fetches a photo once, however many readers it has', () => {
    const fetch = vi.fn().mockReturnValue(of(blob));
    const blobs = new TailBitePhotoBlobs(fetch);
    const got: Blob[] = [];
    blobs.get('a').subscribe((b) => got.push(b));
    blobs.get('a').subscribe((b) => got.push(b));
    blobs.get('b').subscribe((b) => got.push(b));
    expect(fetch.mock.calls).toEqual([['a'], ['b']]);
    expect(got).toEqual([blob, blob, blob]);
  });

  it('tries again after a failed fetch', () => {
    const fetch = vi.fn().mockReturnValueOnce(throwError(() => new Error('down'))).mockReturnValue(of(blob));
    const blobs = new TailBitePhotoBlobs(fetch);
    const failed = vi.fn();
    blobs.get('a').subscribe({error: failed});
    const got: Blob[] = [];
    blobs.get('a').subscribe((b) => got.push(b));
    expect(failed).toHaveBeenCalled();
    expect(got).toEqual([blob]);
    expect(fetch).toHaveBeenCalledTimes(2);
  });

  it('keeps a fetch that completed empty, so a missing photo is not fetched again', () => {
    const fetch = vi.fn().mockReturnValue(EMPTY);
    const blobs = new TailBitePhotoBlobs(fetch);
    const completed = vi.fn();
    blobs.get('a').subscribe({complete: completed});
    blobs.get('a').subscribe({complete: completed});
    expect(completed).toHaveBeenCalledTimes(2);
    expect(fetch).toHaveBeenCalledTimes(1);
  });
});
