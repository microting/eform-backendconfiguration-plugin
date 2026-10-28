import {describe, it, expect, beforeEach, afterEach, vi} from 'vitest';
import type {Mock} from 'vitest';
import {HttpErrorResponse} from '@angular/common/http';
import {BehaviorSubject, concat, defer, merge, of, throwError} from 'rxjs';
import {ArchiveAccountDisabledHandler, archiveErrorCode} from './archive-account-disabled.handler';

/** Jest-style `done` callback for observable-based tests: `done()` resolves, `done(err)` rejects. */
type DoneFn = (err?: unknown) => void;
const withDone = (body: (done: DoneFn) => void) => (): Promise<void> =>
  new Promise<void>((resolve, reject) => body(err => (err ? reject(err) : resolve())));

/**
 * Under `ng test` the global FileReader (jsdom, patched by zone.js) has no `readAsText`, so the handler's Blob
 * branch would always read null. This minimal reader reads through `Blob.text()` instead, with the same events.
 */
class BlobTextFileReader {
  result: string | null = null;
  error: unknown = null;
  onload: (() => void) | null = null;
  onerror: (() => void) | null = null;

  readAsText(blob: Blob): void {
    blob.text().then(
      text => { this.result = text; this.onload?.(); },
      err => { this.error = err; this.onerror?.(); },
    );
  }
}

/** D7: a resigned or deactivated user gets 403 {"error":"account_disabled"} from every archive call. */
describe('ArchiveAccountDisabledHandler', () => {
  let auth: {logout: Mock};
  let toastr: {error: Mock};
  let isAuth$: BehaviorSubject<boolean>;
  let handler: ArchiveAccountDisabledHandler;

  beforeEach(() => {
    vi.stubGlobal('FileReader', BlobTextFileReader);
    auth = {logout: vi.fn()};
    toastr = {error: vi.fn()};
    const translate = {instant: (key: string) => key};
    isAuth$ = new BehaviorSubject<boolean>(true);
    const store = {select: () => isAuth$};
    handler = new ArchiveAccountDisabledHandler(auth as any, toastr as any, translate as any, store as any);
  });

  afterEach(() => vi.unstubAllGlobals());

  const failWith = (status: number, body: unknown) => throwError(() => new HttpErrorResponse({status, error: body}));

  /** The error reaches the subscriber unchanged; no logout, no toast, no forbidden$. */
  const expectPassThrough = (status: number, body: unknown, done: DoneFn, announceForbidden = true): void => {
    const forbidden = vi.fn();
    handler.forbidden$.subscribe(forbidden);
    failWith(status, body).pipe(handler.handle(announceForbidden)).subscribe({
      complete: () => done(new Error('completed instead of erroring')),
      error: (err: HttpErrorResponse) => {
        expect(err.status).toBe(status);
        expect(auth.logout).not.toHaveBeenCalled();
        expect(toastr.error).not.toHaveBeenCalled();
        expect(forbidden).not.toHaveBeenCalled();
        done();
      },
    });
  };

  it('ends the session and completes without emitting on account_disabled', withDone(done => {
    const next = vi.fn();
    const forbidden = vi.fn();
    handler.forbidden$.subscribe(forbidden);
    failWith(403, {error: 'account_disabled'}).pipe(handler.handle()).subscribe({
      next,
      error: err => done(err),
      complete: () => {
        expect(next).not.toHaveBeenCalled();
        expect(toastr.error).toHaveBeenCalledWith('Your account is disabled');
        expect(auth.logout).toHaveBeenCalledTimes(1);
        expect(forbidden).not.toHaveBeenCalled();
        done();
      },
    });
  }));

  it('reads the code from a Blob body, as get-file and get-files answer', withDone(done => {
    const body = new Blob([JSON.stringify({error: 'account_disabled'})], {type: 'application/json'});
    failWith(403, body).pipe(handler.handle()).subscribe({
      error: err => done(err),
      complete: () => {
        expect(toastr.error).toHaveBeenCalledWith('Your account is disabled');
        expect(auth.logout).toHaveBeenCalledTimes(1);
        done();
      },
    });
  }));

  it('logs out once when parallel calls all get account_disabled', withDone(done => {
    const disabled = () => failWith(403, {error: 'account_disabled'}).pipe(handler.handle());
    const blobDisabled = failWith(403, new Blob([JSON.stringify({error: 'account_disabled'})])).pipe(handler.handle());
    merge(disabled(), disabled(), blobDisabled).subscribe({
      error: err => done(err),
      complete: () => {
        expect(toastr.error).toHaveBeenCalledTimes(1);
        expect(auth.logout).toHaveBeenCalledTimes(1);
        done();
      },
    });
  }));

  it('logs out again after a new login (isAuth false then true)', withDone(done => {
    const disabled = () => failWith(403, {error: 'account_disabled'}).pipe(handler.handle());
    concat(
      disabled(),
      defer(() => { isAuth$.next(false); isAuth$.next(true); return of('ok'); }),
      disabled(),
    ).subscribe({
      error: err => done(err),
      complete: () => {
        expect(auth.logout).toHaveBeenCalledTimes(2);
        expect(toastr.error).toHaveBeenCalledTimes(2);
        done();
      },
    });
  }));

  it('does not re-arm on a late successful response; a later disabled response logs out only once', withDone(done => {
    const disabled = () => failWith(403, {error: 'account_disabled'}).pipe(handler.handle());
    concat(disabled(), of('late ok').pipe(handler.handle()), disabled()).subscribe({
      error: err => done(err),
      complete: () => {
        expect(auth.logout).toHaveBeenCalledTimes(1);
        expect(toastr.error).toHaveBeenCalledTimes(1);
        done();
      },
    });
  }));

  it('passes a 403 with any other Blob body through untouched', withDone(done => {
    expectPassThrough(403, new Blob(['<html>Forbidden</html>'], {type: 'text/html'}), done);
  }));

  it('passes any other 403 through untouched (the inbox hides itself on its own 403)', withDone(done => {
    expectPassThrough(403, {error: 'something_else'}, done);
  }));

  /** The [ArchiveManager] refusal: 403 with an empty body. The error goes on; the user is told who may do it. */
  const expectManagerRefusal = (body: unknown, done: DoneFn): void => {
    const forbidden = vi.fn();
    handler.forbidden$.subscribe(forbidden);
    failWith(403, body).pipe(handler.handle()).subscribe({
      complete: () => done(new Error('completed instead of erroring')),
      error: (err: HttpErrorResponse) => {
        expect(err.status).toBe(403);
        expect(auth.logout).not.toHaveBeenCalled();
        expect(toastr.error).toHaveBeenCalledWith('Only administrators and leaders can do this');
        expect(forbidden).toHaveBeenCalledTimes(1);
        done();
      },
    });
  };

  it('an empty-body 403 errors on, says only administrators and leaders may, and asks for fresh capabilities',
    withDone(done => expectManagerRefusal(null, done)));

  it('an empty Blob 403 (a download) is the same refusal', withDone(done => expectManagerRefusal(new Blob([]), done)));

  it('an empty-body 403 passes through untouched when the endpoint opts out (FirstUserOnly)',
    withDone(done => expectPassThrough(403, null, done, false)));

  // In the app the host interceptor swallows a 404 before it gets here; the handler itself must not act on it.
  it('passes other statuses through untouched', withDone(done => {
    expectPassThrough(404, {error: 'account_disabled'}, done);
  }));

  it('passes a non-HTTP error through untouched', withDone(done => {
    const boom = new Error('boom');
    throwError(() => boom).pipe(handler.handle()).subscribe({
      error: err => {
        expect(err).toBe(boom);
        expect(auth.logout).not.toHaveBeenCalled();
        done();
      },
    });
  }));

  it('archiveErrorCode accepts an object, a JSON string, a Blob and garbage', async () => {
    expect(await archiveErrorCode({error: 'account_disabled'})).toBe('account_disabled');
    expect(await archiveErrorCode('{"error":"account_disabled"}')).toBe('account_disabled');
    expect(await archiveErrorCode(new Blob(['{"error":"account_disabled"}']))).toBe('account_disabled');
    expect(await archiveErrorCode('<html>')).toBeNull();
    expect(await archiveErrorCode(null)).toBeNull();
    expect(await archiveErrorCode({error: 42})).toBeNull();
  });
});
