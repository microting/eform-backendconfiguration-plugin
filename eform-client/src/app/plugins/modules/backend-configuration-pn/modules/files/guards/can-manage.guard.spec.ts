import {describe, it, expect, vi, afterEach} from 'vitest';
import {TestBed} from '@angular/core/testing';
import {Store} from '@ngrx/store';
import {ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree} from '@angular/router';
import {NEVER, Observable, firstValueFrom, of} from 'rxjs';
import {ArchiveAccountDisabledHandler, BackendConfigurationPnArchiveFoldersService} from '../../../services';
import {ArchiveCapabilitiesService, CAPABILITIES_TIMEOUT_MS} from '../services/archive-capabilities.service';
import {ARCHIVE_LIST_URL, CanManageGuard} from './can-manage.guard';

function runGuard(providers: unknown[]) {
  const tree = {} as UrlTree;
  const router = {parseUrl: vi.fn().mockReturnValue(tree)};
  TestBed.configureTestingModule({providers: [{provide: Router, useValue: router}, ...providers] as never[]});
  const result = TestBed.runInInjectionContext(
    () => CanManageGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot)) as Observable<boolean | UrlTree>;
  return {result, router, tree};
}

function run(canManage: boolean) {
  const load = vi.fn().mockReturnValue(of({canManage, canDelete: canManage, canUseInbox: canManage, isFirstUser: false}));
  return {...runGuard([{provide: ArchiveCapabilitiesService, useValue: {load}}]), load};
}

/** The real service against a capabilities request that never answers (an older plugin), for a user with this role. */
function runWithoutAnswer(isAdmin: boolean) {
  return runGuard([
    {provide: Store, useValue: {select: () => of(isAdmin)}},
    {provide: ArchiveAccountDisabledHandler, useValue: {forbidden$: NEVER}},
    {provide: BackendConfigurationPnArchiveFoldersService, useValue: {getCapabilities: () => NEVER}},
  ]);
}

describe('CanManageGuard', () => {
  afterEach(() => vi.useRealTimers());

  it('lets an admin or a Leder in, with fresh capabilities', async () => {
    const {result, router, load} = run(true);
    expect(await firstValueFrom(result)).toBe(true);
    expect(load).toHaveBeenCalledTimes(1);
    expect(router.parseUrl).not.toHaveBeenCalled();
  });

  it('sends everyone else to the Arkiv list', async () => {
    const {result, router, tree} = run(false);
    expect(await firstValueFrom(result)).toBe(tree);
    expect(router.parseUrl).toHaveBeenCalledWith(ARCHIVE_LIST_URL);
    expect(ARCHIVE_LIST_URL).toBe('/plugins/backend-configuration-pn/files');
  });

  it('lets an admin in after the role fallback when the capabilities request never answers', async () => {
    vi.useFakeTimers();
    const {result} = runWithoutAnswer(true);
    const resolved = firstValueFrom(result);
    await vi.advanceTimersByTimeAsync(CAPABILITIES_TIMEOUT_MS);
    expect(await resolved).toBe(true);
  });

  it('sends a non-admin to the Arkiv list after the role fallback', async () => {
    vi.useFakeTimers();
    const {result, router, tree} = runWithoutAnswer(false);
    const resolved = firstValueFrom(result);
    await vi.advanceTimersByTimeAsync(CAPABILITIES_TIMEOUT_MS);
    expect(await resolved).toBe(tree);
    expect(router.parseUrl).toHaveBeenCalledWith(ARCHIVE_LIST_URL);
  });
});
