import {inject} from '@angular/core';
import {CanActivateFn, Router, UrlTree} from '@angular/router';
import {Observable, map} from 'rxjs';
import {ArchiveCapabilitiesService} from '../services/archive-capabilities.service';

export const ARCHIVE_LIST_URL = '/plugins/backend-configuration-pn/files';

/**
 * Indbakke and Indstillinger e-mail: admins and Leders (capabilities.canManage), read fresh on every navigation.
 * Everyone else lands on the Arkiv list instead of a page whose every call would be refused.
 */
export const CanManageGuard: CanActivateFn = (): Observable<boolean | UrlTree> => {
  const router = inject(Router);
  return inject(ArchiveCapabilitiesService).load().pipe(
    map(capabilities => (capabilities.canManage ? true : router.parseUrl(ARCHIVE_LIST_URL))),
  );
};
