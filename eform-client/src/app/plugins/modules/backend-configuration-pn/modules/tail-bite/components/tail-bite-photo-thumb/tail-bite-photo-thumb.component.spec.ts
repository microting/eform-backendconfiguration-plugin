import {describe, it, expect, beforeEach, afterEach, vi} from 'vitest';
import type {Mock} from 'vitest';
import {TestBed} from '@angular/core/testing';
import {EMPTY, Observable, Subject, of, throwError} from 'rxjs';
import {TailBitePhotoBlobs} from '../../shared/tail-bite-photo-blobs';
import {TailBitePhotoThumbComponent} from './tail-bite-photo-thumb.component';

describe('TailBitePhotoThumbComponent', () => {
  const blob = new Blob(['x'], {type: 'image/jpeg'});
  // jsdom has no object URLs; these stand in for the browser's and are put back after each test.
  const original = {create: (URL as any).createObjectURL, revoke: (URL as any).revokeObjectURL};
  let create: Mock;
  let revoke: Mock;
  let urls: number;

  const thumb = (fetch: (uuid: string) => Observable<Blob>, photoUuid = 'a') => {
    const c = TestBed.runInInjectionContext(() => new TailBitePhotoThumbComponent());
    c.photoUuid = photoUuid;
    c.blobs = new TailBitePhotoBlobs(fetch);
    c.ngOnChanges();
    return c;
  };

  beforeEach(() => {
    urls = 0;
    create = vi.fn(() => `blob:test/${++urls}`);
    revoke = vi.fn();
    (URL as any).createObjectURL = create;
    (URL as any).revokeObjectURL = revoke;
  });

  afterEach(() => {
    (URL as any).createObjectURL = original.create;
    (URL as any).revokeObjectURL = original.revoke;
  });

  it('shows the photo through an object URL and revokes it on destroy', () => {
    const fetch = vi.fn().mockReturnValue(of(blob));
    const c = thumb(fetch);
    expect(fetch).toHaveBeenCalledWith('a');
    expect(create).toHaveBeenCalledWith(blob);
    expect(c.url).toBe('blob:test/1');
    expect(c.failed).toBe(false);
    c.ngOnDestroy();
    expect(revoke).toHaveBeenCalledWith('blob:test/1');
    expect(c.url).toBeNull();
  });

  it('revokes the old object URL when it is given another photo', () => {
    const c = thumb(() => of(blob));
    c.photoUuid = 'b';
    c.ngOnChanges();
    expect(revoke).toHaveBeenCalledWith('blob:test/1');
    expect(c.url).toBe('blob:test/2');
  });

  it('shows the placeholder when the fetch fails', () => {
    const c = thumb(() => throwError(() => new Error('down')));
    expect(c.failed).toBe(true);
    expect(c.url).toBeNull();
    expect(create).not.toHaveBeenCalled();
  });

  it('shows the placeholder when the fetch completes without a photo (the host interceptor\'s 404)', () => {
    const c = thumb(() => EMPTY);
    expect(c.failed).toBe(true);
  });

  it('shows the placeholder, and drops the URL, when the bytes are not an image', () => {
    const c = thumb(() => of(blob));
    c.onImageError();
    expect(revoke).toHaveBeenCalledWith('blob:test/1');
    expect(c.url).toBeNull();
    expect(c.failed).toBe(true);
  });

  it('makes no URL for a photo that arrives after the tile is gone', () => {
    const late = new Subject<Blob>();
    const c = thumb(() => late);
    c.ngOnDestroy();
    late.next(blob);
    expect(create).not.toHaveBeenCalled();
    expect(c.url).toBeNull();
  });
});
