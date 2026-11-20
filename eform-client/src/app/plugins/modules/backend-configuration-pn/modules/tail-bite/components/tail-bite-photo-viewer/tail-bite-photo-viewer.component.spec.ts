import {describe, it, expect, beforeEach, afterEach, vi} from 'vitest';
import type {Mock} from 'vitest';
import {TestBed} from '@angular/core/testing';
import {MAT_DIALOG_DATA} from '@angular/material/dialog';
import {EMPTY, of} from 'rxjs';
import {BackendConfigurationPnTailBiteService} from '../../../../services';
import {TailBitePhotoBlobs} from '../../shared/tail-bite-photo-blobs';
import {TailBiteGalleryItem} from '../tail-bite-outbreak-detail/tail-bite-outbreak-view';
import {TailBitePhotoViewerComponent, TailBitePhotoViewerData} from './tail-bite-photo-viewer.component';

describe('TailBitePhotoViewerComponent', () => {
  const jpeg = new Blob(['x'], {type: 'image/jpeg'});
  const png = new Blob(['x'], {type: 'image/png'});
  const item = (uuid: string): TailBiteGalleryItem =>
    ({uuid, registrationId: 21, effectiveAt: new Date(Date.UTC(2026, 10, 19, 5, 2)), pens: 'Sti 312A', siteName: 'Jane Doe'});
  const items = [item('a'), item('b'), item('c')];
  // jsdom has no object URLs; these stand in for the browser's and are put back after each test.
  const original = {create: (URL as any).createObjectURL, revoke: (URL as any).revokeObjectURL};
  let create: Mock;
  let revoke: Mock;
  let getPhotoBlob: Mock;
  let urls: number;

  const viewer = (data: TailBitePhotoViewerData) => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        {provide: MAT_DIALOG_DATA, useValue: data},
        {provide: BackendConfigurationPnTailBiteService, useValue: {getPhotoBlob}},
      ],
    });
    const c = TestBed.runInInjectionContext(() => new TailBitePhotoViewerComponent());
    c.ngOnInit();
    return c;
  };

  beforeEach(() => {
    urls = 0;
    create = vi.fn(() => `blob:test/${++urls}`);
    revoke = vi.fn();
    (URL as any).createObjectURL = create;
    (URL as any).revokeObjectURL = revoke;
    getPhotoBlob = vi.fn().mockReturnValue(of(jpeg));
  });

  afterEach(() => {
    (URL as any).createObjectURL = original.create;
    (URL as any).revokeObjectURL = original.revoke;
  });

  it('opens on the given photo and fetches it itself without the page\'s blobs', () => {
    const c = viewer({items, index: 1});
    expect(c.item!.uuid).toBe('b');
    expect(getPhotoBlob).toHaveBeenCalledWith('b');
    expect(c.url).toBe('blob:test/1');
    expect(c.fileName).toBe('halebid-b.jpg');
  });

  it('reads the page\'s blobs when it is given them', () => {
    const fetch = vi.fn().mockReturnValue(of(png));
    const c = viewer({items, index: 0, blobs: new TailBitePhotoBlobs(fetch)});
    expect(fetch).toHaveBeenCalledWith('a');
    expect(getPhotoBlob).not.toHaveBeenCalled();
    expect(c.fileName).toBe('halebid-a.png');
  });

  it('wraps around with next and previous', () => {
    const c = viewer({items, index: 2});
    c.next();
    expect(c.index).toBe(0);
    c.previous();
    expect(c.index).toBe(2);
    c.previous();
    expect(c.index).toBe(1);
    // Each photo is fetched once; going back to one reuses its Blob.
    expect(getPhotoBlob.mock.calls.map(([uuid]) => uuid)).toEqual(['c', 'a', 'b']);
  });

  it('revokes the shown photo\'s URL when it moves on, and the last one on close', () => {
    const c = viewer({items, index: 0});
    c.next();
    expect(revoke).toHaveBeenCalledWith('blob:test/1');
    expect(c.url).toBe('blob:test/2');
    c.ngOnDestroy();
    expect(revoke).toHaveBeenCalledWith('blob:test/2');
    expect(c.url).toBeNull();
  });

  it('says so when a photo cannot be loaded, and shows the next one again', () => {
    getPhotoBlob.mockReturnValueOnce(EMPTY);
    const c = viewer({items, index: 0});
    expect(c.failed).toBe(true);
    expect(c.url).toBeNull();
    c.next();
    expect(c.failed).toBe(false);
    expect(c.url).toBe('blob:test/1');
  });
});
