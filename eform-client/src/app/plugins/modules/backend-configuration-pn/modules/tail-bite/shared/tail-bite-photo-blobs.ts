import {Observable, defer, shareReplay} from 'rxjs';

/**
 * One fetch per photo for a page and the viewer it opens: the table's thumbnail, the gallery's and the viewer all read
 * the same Blob. It holds Blobs only; object URLs belong to the components that show them, which revoke their own.
 * A failed fetch is not kept, so the next reader fetches again; one that completed empty (the host interceptor's answer
 * to a 404, after its retries) is kept, so a missing photo is not fetched over and over. A fetch nobody reads any more
 * (the page was left) is cancelled.
 */
export class TailBitePhotoBlobs {
  private readonly cache = new Map<string, Observable<Blob>>();

  constructor(private readonly fetch: (photoUuid: string) => Observable<Blob>) {}

  get(photoUuid: string): Observable<Blob> {
    let blob = this.cache.get(photoUuid);
    if (!blob) {
      blob = defer(() => this.fetch(photoUuid)).pipe(shareReplay({bufferSize: 1, refCount: true}));
      this.cache.set(photoUuid, blob);
    }
    return blob;
  }
}
