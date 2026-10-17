import {HttpErrorResponse} from '@angular/common/http';
import {BehaviorSubject, concat, defer, merge, of, throwError} from 'rxjs';
import {ArchiveAccountDisabledHandler, archiveErrorCode} from './archive-account-disabled.handler';

/** D7: a resigned or deactivated user gets 403 {"error":"account_disabled"} from every archive call. */
describe('ArchiveAccountDisabledHandler', () => {
  let auth: {logout: jest.Mock};
  let toastr: {error: jest.Mock};
  let isAuth$: BehaviorSubject<boolean>;
  let handler: ArchiveAccountDisabledHandler;

  beforeEach(() => {
    auth = {logout: jest.fn()};
    toastr = {error: jest.fn()};
    const translate = {instant: (key: string) => key};
    isAuth$ = new BehaviorSubject<boolean>(true);
    const store = {select: () => isAuth$};
    handler = new ArchiveAccountDisabledHandler(auth as any, toastr as any, translate as any, store as any);
  });

  const failWith = (status: number, body: unknown) => throwError(() => new HttpErrorResponse({status, error: body}));

  /** The error reaches the subscriber unchanged and the session is left alone. */
  const expectPassThrough = (status: number, body: unknown, done: jest.DoneCallback): void => {
    failWith(status, body).pipe(handler.handle()).subscribe({
      complete: () => done(new Error('completed instead of erroring')),
      error: (err: HttpErrorResponse) => {
        expect(err.status).toBe(status);
        expect(auth.logout).not.toHaveBeenCalled();
        expect(toastr.error).not.toHaveBeenCalled();
        done();
      },
    });
  };

  it('ends the session and completes without emitting on account_disabled', done => {
    const next = jest.fn();
    failWith(403, {error: 'account_disabled'}).pipe(handler.handle()).subscribe({
      next,
      error: err => done(err),
      complete: () => {
        expect(next).not.toHaveBeenCalled();
        expect(toastr.error).toHaveBeenCalledWith('Your account is disabled');
        expect(auth.logout).toHaveBeenCalledTimes(1);
        done();
      },
    });
  });

  it('reads the code from a Blob body, as get-file and get-files answer', done => {
    const body = new Blob([JSON.stringify({error: 'account_disabled'})], {type: 'application/json'});
    failWith(403, body).pipe(handler.handle()).subscribe({
      error: err => done(err),
      complete: () => {
        expect(toastr.error).toHaveBeenCalledWith('Your account is disabled');
        expect(auth.logout).toHaveBeenCalledTimes(1);
        done();
      },
    });
  });

  it('logs out once when parallel calls all get account_disabled', done => {
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
  });

  it('logs out again after a new login (isAuth false then true)', done => {
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
  });

  it('does not re-arm on a late successful response; a later disabled response logs out only once', done => {
    const disabled = () => failWith(403, {error: 'account_disabled'}).pipe(handler.handle());
    concat(disabled(), of('late ok').pipe(handler.handle()), disabled()).subscribe({
      error: err => done(err),
      complete: () => {
        expect(auth.logout).toHaveBeenCalledTimes(1);
        expect(toastr.error).toHaveBeenCalledTimes(1);
        done();
      },
    });
  });

  it('passes a 403 with any other Blob body through untouched', done => {
    expectPassThrough(403, new Blob(['<html>Forbidden</html>'], {type: 'text/html'}), done);
  });

  it('passes any other 403 through untouched (the inbox hides itself on its own 403)', done => {
    expectPassThrough(403, {error: 'something_else'}, done);
  });

  it('passes a non-admin 403 with an empty body through untouched', done => {
    expectPassThrough(403, null, done);
  });

  // In the app the host interceptor swallows a 404 before it gets here; the handler itself must not act on it.
  it('passes other statuses through untouched', done => {
    expectPassThrough(404, {error: 'account_disabled'}, done);
  });

  it('passes a non-HTTP error through untouched', done => {
    const boom = new Error('boom');
    throwError(() => boom).pipe(handler.handle()).subscribe({
      error: err => {
        expect(err).toBe(boom);
        expect(auth.logout).not.toHaveBeenCalled();
        done();
      },
    });
  });

  it('archiveErrorCode accepts an object, a JSON string, a Blob and garbage', async () => {
    expect(await archiveErrorCode({error: 'account_disabled'})).toBe('account_disabled');
    expect(await archiveErrorCode('{"error":"account_disabled"}')).toBe('account_disabled');
    expect(await archiveErrorCode(new Blob(['{"error":"account_disabled"}']))).toBe('account_disabled');
    expect(await archiveErrorCode('<html>')).toBeNull();
    expect(await archiveErrorCode(null)).toBeNull();
    expect(await archiveErrorCode({error: 42})).toBeNull();
  });
});
