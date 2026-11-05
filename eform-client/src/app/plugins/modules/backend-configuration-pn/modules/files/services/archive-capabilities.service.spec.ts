import {describe, it, expect, beforeEach, afterEach, vi} from 'vitest';
import type {Mock} from 'vitest';
import {TestBed} from '@angular/core/testing';
import {HttpErrorResponse} from '@angular/common/http';
import {Store} from '@ngrx/store';
import {BehaviorSubject, EMPTY, NEVER, Observable, Subject, firstValueFrom, of, throwError} from 'rxjs';
import {selectCurrentUserIsAdmin, selectCurrentUserIsFirstUser} from 'src/app/state';
import {selectAuthIsAuth} from 'src/app/state/auth/auth.selector';
import {ArchiveCapabilitiesModel} from '../../../models';
import {ArchiveAccountDisabledHandler, BackendConfigurationPnArchiveFoldersService} from '../../../services';
import {ArchiveCapabilitiesService, CAPABILITIES_TIMEOUT_MS, NO_ARCHIVE_CAPABILITIES} from './archive-capabilities.service';

const leder: ArchiveCapabilitiesModel = {canManage: true, canDelete: true, canUseInbox: true, isFirstUser: false};
const plain: ArchiveCapabilitiesModel = {canManage: false, canDelete: false, canUseInbox: false, isFirstUser: false};
type CapabilitiesResponse = {success: boolean; message: string; model: Partial<ArchiveCapabilitiesModel>};
const response = (model: Partial<ArchiveCapabilitiesModel>): CapabilitiesResponse => ({success: true, message: '', model});
const answer = (model: Partial<ArchiveCapabilitiesModel>) => of(response(model));
const serverError = () => throwError(() => new HttpErrorResponse({status: 500}));
const roleCaps = (admin: boolean): ArchiveCapabilitiesModel =>
  ({canManage: admin, canDelete: admin, canUseInbox: admin, isFirstUser: true});

describe('ArchiveCapabilitiesService', () => {
  let getCapabilities: Mock;
  let forbidden$: Subject<void>;
  let isAuth$: BehaviorSubject<boolean>;
  let isAdmin: boolean;
  let service: ArchiveCapabilitiesService;
  let store: {select: (selector: unknown) => Observable<unknown>};

  beforeEach(() => {
    getCapabilities = vi.fn().mockReturnValue(answer(leder));
    forbidden$ = new Subject<void>();
    isAuth$ = new BehaviorSubject<boolean>(true);
    isAdmin = false;
    store = {
      select: (selector: unknown) => {
        if (selector === selectAuthIsAuth) return isAuth$;
        if (selector === selectCurrentUserIsAdmin) return of(isAdmin);
        if (selector === selectCurrentUserIsFirstUser) return of(true);
        throw new Error('unexpected selector');
      },
    };
    TestBed.configureTestingModule({
      providers: [
        {provide: BackendConfigurationPnArchiveFoldersService, useValue: {getCapabilities}},
        {provide: ArchiveAccountDisabledHandler, useValue: {forbidden$}},
        {provide: Store, useValue: store},
      ],
    });
    service = TestBed.inject(ArchiveCapabilitiesService);
  });

  afterEach(() => vi.useRealTimers());

  const current = () => firstValueFrom(service.capabilities$);

  it('offers nothing before the first answer', async () => {
    expect(await current()).toEqual(NO_ARCHIVE_CAPABILITIES);
    expect(getCapabilities).not.toHaveBeenCalled();
  });

  it('loads the server answer and publishes it', async () => {
    expect(await firstValueFrom(service.load())).toEqual(leder);
    expect(await current()).toEqual(leder);
  });

  it('reads a missing key as false', async () => {
    getCapabilities.mockReturnValue(answer({canManage: true}));
    expect(await firstValueFrom(service.load())).toEqual({canManage: true, canDelete: false, canUseInbox: false, isFirstUser: false});
  });

  it.each([true, false])('falls back to the role when the host completes a 404 empty (an older plugin): admin=%s', async admin => {
    getCapabilities.mockReturnValue(EMPTY);
    isAdmin = admin;
    expect(await firstValueFrom(service.load())).toEqual(roleCaps(admin));
  });

  it.each([true, false])('falls back to the role when the request never answers: admin=%s', async admin => {
    vi.useFakeTimers();
    getCapabilities.mockReturnValue(NEVER);
    isAdmin = admin;
    const loading = firstValueFrom(service.load());
    await vi.advanceTimersByTimeAsync(CAPABILITIES_TIMEOUT_MS);
    expect(await loading).toEqual(roleCaps(admin));
    expect(await current()).toEqual(roleCaps(admin));
  });

  it('parallel loads share one timeout and one request', async () => {
    vi.useFakeTimers();
    getCapabilities.mockReturnValue(NEVER);
    isAdmin = true;
    const first = firstValueFrom(service.load());
    const second = firstValueFrom(service.load());
    await vi.advanceTimersByTimeAsync(CAPABILITIES_TIMEOUT_MS);
    expect(await first).toEqual(roleCaps(true));
    expect(await second).toEqual(roleCaps(true));
    expect(getCapabilities).toHaveBeenCalledTimes(1);
  });

  it('keeps the last answer when a reload completes empty', async () => {
    await firstValueFrom(service.load());
    getCapabilities.mockReturnValue(EMPTY);
    isAdmin = true;
    expect(await firstValueFrom(service.load())).toEqual(leder);
    expect(await current()).toEqual(leder);
  });

  it('keeps the last answer when a reload times out', async () => {
    await firstValueFrom(service.load());
    vi.useFakeTimers();
    getCapabilities.mockReturnValue(NEVER);
    const loading = firstValueFrom(service.load());
    await vi.advanceTimersByTimeAsync(CAPABILITIES_TIMEOUT_MS);
    expect(await loading).toEqual(leder);
  });

  it('emits no capabilities when even the role fallback fails', async () => {
    getCapabilities.mockReturnValue(EMPTY);
    store.select = () => throwError(() => new Error('no user'));
    expect(await firstValueFrom(service.load())).toEqual(NO_ARCHIVE_CAPABILITIES);
  });

  it('keeps the last answer when a reload fails', async () => {
    await firstValueFrom(service.load());
    getCapabilities.mockReturnValue(serverError());
    expect(await firstValueFrom(service.load())).toEqual(leder);
  });

  it('a failure before any answer falls back to the role', async () => {
    getCapabilities.mockReturnValue(serverError());
    isAdmin = true;
    expect((await firstValueFrom(service.load())).canManage).toBe(true);
  });

  it('parallel loads share one request; the next load asks again', async () => {
    const pending = new Subject<CapabilitiesResponse>();
    getCapabilities.mockReturnValue(pending);
    const first = firstValueFrom(service.load());
    const second = firstValueFrom(service.load());
    pending.next(response(leder));
    pending.complete();
    expect(await first).toEqual(leder);
    expect(await second).toEqual(leder);
    expect(getCapabilities).toHaveBeenCalledTimes(1);
    getCapabilities.mockReturnValue(answer(plain));
    await firstValueFrom(service.load());
    expect(getCapabilities).toHaveBeenCalledTimes(2);
  });

  it('reloads after an [ArchiveManager] refusal, so a revoke hides the controls', async () => {
    await firstValueFrom(service.load());
    getCapabilities.mockReturnValue(answer(plain));
    forbidden$.next();
    expect(getCapabilities).toHaveBeenCalledTimes(2);
    expect(await current()).toEqual(plain);
  });

  it('a refusal during a load asks again once that load completes, as its answer may predate the revoke', async () => {
    const pending = new Subject<CapabilitiesResponse>();
    getCapabilities.mockReturnValue(pending);
    const loading = firstValueFrom(service.load());
    forbidden$.next();
    expect(getCapabilities).toHaveBeenCalledTimes(1);
    pending.next(response(leder));
    expect(await loading).toEqual(leder);
    getCapabilities.mockReturnValue(answer(plain));
    pending.complete();
    expect(getCapabilities).toHaveBeenCalledTimes(2);
    expect(await current()).toEqual(plain);
  });

  it('several refusals during a load queue one follow-up, and it does not repeat', async () => {
    const pending = new Subject<CapabilitiesResponse>();
    getCapabilities.mockReturnValue(pending);
    const loading = firstValueFrom(service.load());
    forbidden$.next();
    forbidden$.next();
    forbidden$.next();
    pending.next(response(leder));
    getCapabilities.mockReturnValue(answer(plain));
    pending.complete();
    await loading;
    expect(getCapabilities).toHaveBeenCalledTimes(2);
    await firstValueFrom(service.load());
    expect(getCapabilities).toHaveBeenCalledTimes(3);
  });

  it('sign-out drops a follow-up queued by a refusal', async () => {
    const pending = new Subject<CapabilitiesResponse>();
    getCapabilities.mockReturnValue(pending);
    const loading = firstValueFrom(service.load());
    forbidden$.next();
    isAuth$.next(false);
    pending.next(response(leder));
    pending.complete();
    await loading;
    expect(getCapabilities).toHaveBeenCalledTimes(1);
    expect(await current()).toEqual(NO_ARCHIVE_CAPABILITIES);
  });

  it('sign-out forgets the previous user, including the last answer', async () => {
    await firstValueFrom(service.load());
    isAuth$.next(false);
    expect(await current()).toEqual(NO_ARCHIVE_CAPABILITIES);
    getCapabilities.mockReturnValue(serverError());
    expect((await firstValueFrom(service.load())).canManage).toBe(false);
  });

  it('sign-out ignores an answer still in flight and the next load asks again', async () => {
    const old = new Subject<CapabilitiesResponse>();
    getCapabilities.mockReturnValue(old);
    const loading = firstValueFrom(service.load());
    isAuth$.next(false);
    getCapabilities.mockReturnValue(answer(plain));
    expect(await firstValueFrom(service.load())).toEqual(plain);
    expect(getCapabilities).toHaveBeenCalledTimes(2);
    old.next(response(leder));
    old.complete();
    await loading;
    expect(await current()).toEqual(plain);
    getCapabilities.mockReturnValue(serverError());
    expect(await firstValueFrom(service.load())).toEqual(plain);
  });
});
